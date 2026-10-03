using IndustrialVision.Core.Enums;

namespace IndustrialVision.Core.Interfaces;

/// <summary>
/// Light controller abstraction.
/// Supports up to 8 channels (CH1–CH8).
/// Protocol is NOT assumed — real implementation requires protocol documentation.
/// </summary>
public interface ILightController : IDisposable
{
    /// <summary>Current connection status.</summary>
    ConnectionStatus Status { get; }

    /// <summary>Whether light controller is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when connection status changes.</summary>
    event EventHandler<ConnectionStatus> StatusChanged;

    /// <summary>Connect to light controller using current configuration.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnect from light controller.</summary>
    Task DisconnectAsync();

    /// <summary>Set intensity for a specific channel.</summary>
    /// <param name="channel">Channel number (1-based: 1 to 8).</param>
    /// <param name="intensity">Intensity value (range depends on controller).</param>
    Task SetChannelAsync(int channel, int intensity, CancellationToken cancellationToken = default);

    /// <summary>Turn on a specific channel at its current intensity.</summary>
    Task TurnOnAsync(int channel, CancellationToken cancellationToken = default);

    /// <summary>Turn off a specific channel.</summary>
    Task TurnOffAsync(int channel, CancellationToken cancellationToken = default);

    /// <summary>Number of channels available.</summary>
    int ChannelCount { get; }

    /// <summary>Get current intensity value for a channel (1 to 8).</summary>
    int GetChannelIntensity(int channel);

    /// <summary>Get current ON/OFF state for a channel (1 to 8).</summary>
    bool IsChannelOn(int channel);

    /// <summary>Turn off all channels.</summary>
    Task TurnOffAllAsync(CancellationToken cancellationToken = default);
}
