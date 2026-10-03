using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Exceptions;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Plc.Drivers;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Plc;

/// <summary>
/// Production PLC communication coordinator.
/// Orchestrates the low-level ILsPlcDriver with:
///   - Configurable timeouts
///   - Automatic retry and resilience
///   - Status change tracking and logging
///   - Distinct PLC error classifications
/// </summary>
public sealed class PlcService : IPlcService
{
    private readonly ILsPlcDriver _driver;
    private readonly PlcConfiguration _config;
    private readonly ILogger<PlcService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _disposed;

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsConnected => _status == ConnectionStatus.Connected || _status == ConnectionStatus.Ready;

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public PlcService(
        ILsPlcDriver driver,
        PlcConfiguration config,
        ILogger<PlcService> logger)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger.LogInformation("[PLC] Already connected.");
            return;
        }

        Status = ConnectionStatus.Connecting;
        _logger.LogInformation("[PLC] Connecting to {Name} ({Model}) at {Ip}:{Port} (Timeout: {Timeout}ms)...",
            _config.Name, _config.Model, _config.IpAddress, _config.Port, _config.ConnectionTimeoutMs);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.ConnectionTimeoutMs > 0 ? _config.ConnectionTimeoutMs : 3000);

            await _driver.ConnectAsync(timeoutCts.Token);
            Status = ConnectionStatus.Connected;
            Status = ConnectionStatus.Ready;
            _logger.LogInformation("[PLC] Connected successfully to {Name} ({Model}) at {Ip}:{Port}.",
                _config.Name, _config.Model, _config.IpAddress, _config.Port);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Status = ConnectionStatus.Error;
            _logger.LogError("[PLC_TIMEOUT] Connection timed out after {Timeout}ms to {Ip}:{Port}.",
                _config.ConnectionTimeoutMs, _config.IpAddress, _config.Port);
            throw new PlcException($"[PLC_TIMEOUT] Connection timeout to PLC at {_config.IpAddress}:{_config.Port}.");
        }
        catch (Exception ex)
        {
            Status = ConnectionStatus.Error;
            _logger.LogError(ex, "[PLC_CONNECTION_ERROR] Failed to connect to PLC: {Message}", ex.Message);
            throw new PlcException($"[PLC_CONNECTION_ERROR] Failed to connect to PLC: {ex.Message}", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        _logger.LogInformation("[PLC] Disconnecting...");
        await _lock.WaitAsync();
        try
        {
            await _driver.DisconnectAsync();
            Status = ConnectionStatus.Disconnected;
            _logger.LogInformation("[PLC] Disconnected.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            return await _driver.IsConnectedAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ReadBoolAsync(string address, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(
            "ReadBit",
            address,
            ct => _driver.ReadBitAsync(address, ct),
            cancellationToken);
    }

    public Task<bool> ReadBitAsync(string address, CancellationToken cancellationToken = default)
        => ReadBoolAsync(address, cancellationToken);

    public async Task WriteBoolAsync(string address, bool value, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(
            "WriteBit",
            address,
            async ct =>
            {
                await _driver.WriteBitAsync(address, value, ct);
                return true;
            },
            cancellationToken);
    }

    public Task WriteBitAsync(string address, bool value, CancellationToken cancellationToken = default)
        => WriteBoolAsync(address, value, cancellationToken);

    public async Task<ushort> ReadWordAsync(string address, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(
            "ReadWord",
            address,
            ct => _driver.ReadWordAsync(address, ct),
            cancellationToken);
    }

    public async Task WriteWordAsync(string address, ushort value, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(
            "WriteWord",
            address,
            async ct =>
            {
                await _driver.WriteWordAsync(address, value, ct);
                return true;
            },
            cancellationToken);
    }

    public async Task<int> ReadIntAsync(string address, CancellationToken cancellationToken = default)
    {
        var word = await ReadWordAsync(address, cancellationToken);
        return word;
    }

    public async Task WriteIntAsync(string address, int value, CancellationToken cancellationToken = default)
    {
        await WriteWordAsync(address, (ushort)value, cancellationToken);
    }

    public async Task<string> ReadStringAsync(string address, CancellationToken cancellationToken = default)
    {
        return await ReadStringAsync(address, 32, cancellationToken);
    }

    public async Task<string> ReadStringAsync(string address, int length, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(
            "ReadString",
            address,
            ct => _driver.ReadStringAsync(address, length, ct),
            cancellationToken);
    }

    public async Task WriteStringAsync(string address, string value, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(
            "WriteString",
            address,
            async ct =>
            {
                await _driver.WriteStringAsync(address, value, ct);
                return true;
            },
            cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        string operationName,
        string address,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new PlcException($"[PLC_CONFIG_ERROR] Address for {operationName} is empty.");
        }

        int maxAttempts = Math.Max(1, _config.RetryCount);
        int timeoutMs = _config.ReadTimeoutMs > 0 ? _config.ReadTimeoutMs : 3000;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                var result = await action(cts.Token);
                _logger.LogDebug("[PLC] {Op}({Address}) -> Success (Attempt {Attempt})", operationName, address, attempt);
                return result;
            }
            catch (PlcRequestException ex)
            {
                // Link is fine, only this request was rejected: no retry, keep connection status.
                _logger.LogError("[PLC_REQUEST_REJECTED] {Op}({Address}): {Message}", operationName, address, ex.Message);
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("[PLC_TIMEOUT] {Op}({Address}) timed out after {Timeout}ms (Attempt {Attempt}/{Max})",
                    operationName, address, timeoutMs, attempt, maxAttempts);

                if (attempt == maxAttempts)
                {
                    Status = ConnectionStatus.Error;
                    throw new PlcException($"[PLC_TIMEOUT] Operation {operationName} on '{address}' timed out.");
                }
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[PLC_RETRY] {Op}({Address}) failed (Attempt {Attempt}/{Max}): {Message}. Retrying...",
                    operationName, address, attempt, maxAttempts, ex.Message);
                await Task.Delay(_config.ReconnectDelayMs > 0 ? _config.ReconnectDelayMs : 500, cancellationToken);
            }
            catch (Exception ex)
            {
                Status = ConnectionStatus.Error;
                _logger.LogError(ex, "[PLC_ERROR] {Op}({Address}) failed on final attempt: {Message}",
                    operationName, address, ex.Message);
                throw new PlcException($"[PLC_ERROR] {operationName} on '{address}' failed: {ex.Message}", ex);
            }
        }

        throw new PlcException($"[PLC_ERROR] {operationName} on '{address}' failed after {maxAttempts} attempts.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
        _driver.Dispose();
        Status = ConnectionStatus.Disconnected;
    }
}
