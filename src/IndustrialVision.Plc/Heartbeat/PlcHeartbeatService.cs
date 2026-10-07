using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Plc.Heartbeat;

/// <summary>
/// Background heartbeat worker sending alive signals to PLC.
/// Supports bit toggling (0 -> 1 -> 0) or incremental counter.
/// Enabled via configuration only — defaults to disabled until specification is confirmed.
/// </summary>
public sealed class PlcHeartbeatService : IDisposable
{
    private readonly IPlcService _plc;
    private readonly PlcConfiguration _config;
    private readonly ILogger<PlcHeartbeatService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private bool _bitState;
    private ushort _counter;
    private bool _disposed;
    private string? _activeAddress;
    private string _activeMode = "Toggle";

    public int VerifiedTicks { get; private set; }
    public DateTimeOffset? LastVerifiedAt { get; private set; }
    public string? LastError { get; private set; }
    public event EventHandler<string>? HeartbeatFailed;

    public bool IsRunning => _heartbeatTask != null && !_heartbeatTask.IsCompleted;

    /// <summary>Raised when a heartbeat tick occurs (payload is current toggle state or counter odd/even).</summary>
    public event EventHandler<bool>? HeartbeatTick;

    public PlcHeartbeatService(
        IPlcService plc,
        PlcConfiguration config,
        ILogger<PlcHeartbeatService> logger)
    {
        _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start(bool manual = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_config.Heartbeat.Enabled && !manual)
        {
            _logger.LogInformation("[HEARTBEAT] Heartbeat is disabled in configuration.");
            return;
        }

        var address = _config.Heartbeat.Address;
        if (string.IsNullOrWhiteSpace(address) || address.Contains("NEEDS_"))
        {
            _logger.LogWarning("[HEARTBEAT] Enabled but address is not configured. Not starting.");
            return;
        }

        if (IsRunning) return;
        if (!_plc.IsConnected) throw new InvalidOperationException("Connect PLC before heartbeat.");
        var reserved = new[] { _config.Addresses.Ready, _config.Addresses.Trigger,
            _config.Addresses.Busy, _config.Addresses.CaptureComplete, _config.Addresses.Error,
            _config.Addresses.Reset, _config.Addresses.OK, _config.Addresses.NG, _config.Addresses.Result };
        if (reserved.Any(value => !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, address, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Heartbeat must use a separate test address.");

        _activeAddress = address;
        _activeMode = _config.Heartbeat.Mode;
        _bitState = false;
        VerifiedTicks = 0;
        LastVerifiedAt = null;
        LastError = null;
        _cts = new CancellationTokenSource();
        int interval = _config.Heartbeat.IntervalMs > 0 ? _config.Heartbeat.IntervalMs : 1000;
        _logger.LogInformation("[HEARTBEAT] Started to '{Address}' (Interval: {Interval}ms, Mode: {Mode}).",
            address, interval, _config.Heartbeat.Mode);

        var token = _cts.Token;
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(address, _activeMode, interval, token));
    }

    public async Task StopAsync(bool resetOutput = true)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_heartbeatTask != null)
            {
                try
                {
                    await _heartbeatTask;
                }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
            _cts = null;
            _heartbeatTask = null;
        }

        var address = _activeAddress;
        _activeAddress = null;
        if (resetOutput && address != null && _plc.IsConnected
            && string.Equals(_activeMode, "Toggle", StringComparison.OrdinalIgnoreCase))
            await _plc.WriteBitAsync(address, false);
        _logger.LogInformation("[HEARTBEAT] Stopped.");
    }

    private async Task HeartbeatLoopAsync(string address, string mode, int intervalMs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_plc.IsConnected)
                {
                    if (string.Equals(mode, "Counter", StringComparison.OrdinalIgnoreCase))
                    {
                        _counter++;
                        await _plc.WriteWordAsync(address, _counter, token);
                        if (await _plc.ReadWordAsync(address, token) != _counter)
                            throw new InvalidOperationException("Heartbeat counter readback does not match.");
                        MarkVerified((_counter % 2) != 0);
                    }
                    else
                    {
                        _bitState = !_bitState;
                        await _plc.WriteBitAsync(address, _bitState, token);
                        if (await _plc.ReadBitAsync(address, token) != _bitState)
                            throw new InvalidOperationException("Heartbeat bit readback does not match.");
                        MarkVerified(_bitState);
                    }
                }

                else
                {
                    LastError = "PLC is not connected.";
                    HeartbeatFailed?.Invoke(this, LastError);
                }
                await Task.Delay(intervalMs, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                HeartbeatFailed?.Invoke(this, LastError);
                _logger.LogTrace(ex, "[HEARTBEAT] Failed on '{Address}'. Will retry next interval.", address);
                try { await Task.Delay(intervalMs, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }

    private void MarkVerified(bool state)
    {
        VerifiedTicks++;
        LastVerifiedAt = DateTimeOffset.Now;
        LastError = null;
        HeartbeatTick?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
