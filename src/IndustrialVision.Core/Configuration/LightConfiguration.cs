namespace IndustrialVision.Core.Configuration;

/// <summary>
/// Light controller configuration — all values read from light.json.
/// 
/// ❌ CHƯA THỂ IMPLEMENT HARDWARE THẬT
/// Reason: Light controller protocol is UNKNOWN.
/// Required:
///   1. Controller brand/model (RSee?)
///   2. Communication protocol documentation
///   3. Command format specification
/// </summary>
public sealed class LightConfiguration
{
    /// <summary>Light controller IP address. NEEDS_CONFIGURATION.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Light controller port. NEEDS_CONFIGURATION.</summary>
    public int Port { get; set; }

    /// <summary>Communication protocol. NEEDS_PROTOCOL_INFORMATION.</summary>
    public string Protocol { get; set; } = string.Empty;

    /// <summary>Number of channels available (typically 4 or 8).</summary>
    public int ChannelCount { get; set; }

    /// <summary>Default intensity for all channels. NEEDS_CONFIGURATION.</summary>
    public int DefaultIntensity { get; set; }

    /// <summary>Command timeout in milliseconds.</summary>
    public int CommandTimeoutMs { get; set; }

    /// <summary>Per-channel configuration overrides.</summary>
    public List<LightChannelConfiguration> Channels { get; set; } = new();
}

/// <summary>
/// Per-channel configuration for the light controller.
/// </summary>
public sealed class LightChannelConfiguration
{
    /// <summary>Channel number (1-based).</summary>
    public int Channel { get; set; }

    /// <summary>Channel name for display.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Default intensity for this channel.</summary>
    public int Intensity { get; set; }

    /// <summary>Whether this channel is enabled.</summary>
    public bool Enabled { get; set; } = true;
}
