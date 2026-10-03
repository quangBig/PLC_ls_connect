using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Plc.Trigger;

/// <summary>
/// Background worker monitoring PLC Trigger signal.
/// Detects strictly Rising Edge (0 -> 1) transitions to prevent multiple captures
/// when PLC holds Trigger = ON across cycles.
/// </summary>
public sealed class PlcTriggerMonitor : IDisposable
{
    private readonly IPlcService _plc;
    private readonly PlcConfiguration _config;
    private readonly ILogger<PlcTriggerMonitor> _logger;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private bool _lastState;
    private bool _isArmed = true;
    private bool _disposed;

    /// <summary>Raised when a rising edge (0 -> 1) trigger is detected.</summary>
    public event EventHandler? TriggerFired;

    /// <summary>Raised when the raw trigger state changes (for UI monitor indicator).</summary>
    public event EventHandler<bool>? TriggerStateChanged;

    public bool IsMonitoring => _monitorTask != null && !_monitorTask.IsCompleted;

    public PlcTriggerMonitor(
        IPlcService plc,
        PlcConfiguration config,
        ILogger<PlcTriggerMonitor> logger)
    {
        _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Start monitoring PLC trigger signal in the background.
    /// </summary>
    public void StartMonitoring(int pollIntervalMs = 20)
    {
        if (IsMonitoring) return;

        var triggerAddress = _config.Addresses.Trigger;
        if (string.IsNullOrWhiteSpace(triggerAddress))
        {
            _logger.LogWarning("[TRIGGER_MONITOR] Trigger address is not configured. Monitoring not started.");
            return;
        }

        _cts = new CancellationTokenSource();
        _lastState = false;
        _isArmed = true;

        _logger.LogInformation("[TRIGGER_MONITOR] Started polling '{Address}' every {Interval}ms for Rising Edge (0->1).",
            triggerAddress, pollIntervalMs);

        _monitorTask = Task.Run(() => PollingLoopAsync(triggerAddress, pollIntervalMs, _cts.Token));
    }

    /// <summary>
    /// Stop monitoring.
    /// </summary>
    public async Task StopMonitoringAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_monitorTask != null)
            {
                try
                {
                    await _monitorTask;
                }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
            _cts = null;
            _monitorTask = null;
        }

        _logger.LogInformation("[TRIGGER_MONITOR] Stopped.");
    }

    private async Task PollingLoopAsync(string address, int pollIntervalMs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_plc.IsConnected)
                {
                    await Task.Delay(500, token);
                    continue;
                }

                bool currentState = await _plc.ReadBitAsync(address, token);

                if (currentState != _lastState)
                {
                    TriggerStateChanged?.Invoke(this, currentState);
                }

                // Rising edge detection: was 0, now 1, and monitor is armed
                if (!_lastState && currentState && _isArmed)
                {
                    _logger.LogInformation("[TRIGGER_MONITOR] ⚡ RISING EDGE DETECTED (0 -> 1) on {Address}! Firing trigger.", address);
                    _isArmed = false; // Disarm until PLC resets trigger back to 0
                    TriggerFired?.Invoke(this, EventArgs.Empty);
                }
                // Falling edge: was 1, now 0 -> re-arm
                else if (_lastState && !currentState)
                {
                    _logger.LogDebug("[TRIGGER_MONITOR] Falling edge (1 -> 0) on {Address}. Monitor re-armed.", address);
                    _isArmed = true;
                }

                _lastState = currentState;
                await Task.Delay(pollIntervalMs, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "[TRIGGER_MONITOR] Polling error on '{Address}'. Retrying...", address);
                await Task.Delay(200, token);
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
