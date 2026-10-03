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

    public void Start()
    {
        if (!_config.Heartbeat.Enabled)
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

        _cts = new CancellationTokenSource();
        int interval = _config.Heartbeat.IntervalMs > 0 ? _config.Heartbeat.IntervalMs : 1000;
        _logger.LogInformation("[HEARTBEAT] Started to '{Address}' (Interval: {Interval}ms, Mode: {Mode}).",
            address, interval, _config.Heartbeat.Mode);

        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(address, interval, _cts.Token));
    }

    public async Task StopAsync()
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

        _logger.LogInformation("[HEARTBEAT] Stopped.");
    }

    private async Task HeartbeatLoopAsync(string address, int intervalMs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_plc.IsConnected)
                {
                    if (string.Equals(_config.Heartbeat.Mode, "Counter", StringComparison.OrdinalIgnoreCase))
                    {
                        _counter++;
                        await _plc.WriteWordAsync(address, _counter, token);
                        HeartbeatTick?.Invoke(this, (_counter % 2) != 0);
                    }
                    else
                    {
                        _bitState = !_bitState;
                        await _plc.WriteBitAsync(address, _bitState, token);
                        HeartbeatTick?.Invoke(this, _bitState);
                    }
                }

                await Task.Delay(intervalMs, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "[HEARTBEAT] Failed on '{Address}'. Will retry next interval.", address);
                await Task.Delay(intervalMs, token);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
