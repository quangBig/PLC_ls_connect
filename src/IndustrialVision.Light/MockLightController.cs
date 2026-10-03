using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Light;

/// <summary>
/// Mock light controller for Simulation Mode.
/// Simulates 8-channel light control.
/// 
/// This is NOT a real light controller implementation.
/// 
/// ❌ CHƯA THỂ IMPLEMENT HARDWARE THẬT
/// Reason: Light controller protocol is UNKNOWN.
/// Required:
///   1. Controller brand/model (RSee?)
///   2. Communication protocol documentation
///   3. Command format specification
/// </summary>
public sealed class MockLightController : ILightController
{
    private readonly LightConfiguration _config;
    private readonly ILogger<MockLightController> _logger;
    private readonly Dictionary<int, int> _channelIntensities = new();
    private readonly Dictionary<int, bool> _channelStates = new();
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

    public MockLightController(LightConfiguration config, ILogger<MockLightController> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize all channels
        for (int i = 1; i <= _config.ChannelCount; i++)
        {
            _channelIntensities[i] = _config.DefaultIntensity;
            _channelStates[i] = false;
        }
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATION] Light controller connecting...");
        Status = ConnectionStatus.Connecting;
        Status = ConnectionStatus.Connected;
        Status = ConnectionStatus.Ready;
        _logger.LogInformation("[SIMULATION] Light controller connected and ready.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _logger.LogInformation("[SIMULATION] Light controller disconnecting...");
        Status = ConnectionStatus.Disconnected;
        _logger.LogInformation("[SIMULATION] Light controller disconnected.");
        return Task.CompletedTask;
    }

    public Task SetChannelAsync(int channel, int intensity, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        _channelIntensities[channel] = intensity;
        _logger.LogInformation("[SIMULATION] Light CH{Channel} intensity set to {Intensity}", channel, intensity);
        return Task.CompletedTask;
    }

    public Task TurnOnAsync(int channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        _channelStates[channel] = true;
        _logger.LogInformation("[SIMULATION] Light CH{Channel} ON (intensity: {Intensity})",
            channel, _channelIntensities.GetValueOrDefault(channel));
        return Task.CompletedTask;
    }

    public Task TurnOffAsync(int channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        _channelStates[channel] = false;
        _logger.LogInformation("[SIMULATION] Light CH{Channel} OFF", channel);
        return Task.CompletedTask;
    }

    public int ChannelCount => _config.ChannelCount > 0 ? _config.ChannelCount : 8;

    public int GetChannelIntensity(int channel)
    {
        ValidateChannel(channel);
        return _channelIntensities.GetValueOrDefault(channel, 0);
    }

    public bool IsChannelOn(int channel)
    {
        ValidateChannel(channel);
        return _channelStates.GetValueOrDefault(channel, false);
    }

    public Task TurnOffAllAsync(CancellationToken cancellationToken = default)
    {
        for (int i = 1; i <= _config.ChannelCount; i++)
        {
            _channelStates[i] = false;
        }
        _logger.LogInformation("[SIMULATION] All light channels OFF");
        return Task.CompletedTask;
    }

    private void ValidateChannel(int channel)
    {
        if (channel < 1 || channel > _config.ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel),
                $"Channel must be between 1 and {_config.ChannelCount}.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channelIntensities.Clear();
        _channelStates.Clear();
        Status = ConnectionStatus.Disconnected;
    }
}
