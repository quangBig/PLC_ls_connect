namespace IndustrialVision.Core.Configuration;

/// <summary>
/// Camera configuration — all values read from camera.json.
/// NEEDS_CONFIGURATION: All values must be set before connecting to real hardware.
/// </summary>
public sealed class CameraConfiguration
{
    /// <summary>Camera friendly name. NEEDS_CONFIGURATION.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Camera serial number — used for identification. NEEDS_CONFIGURATION.</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Camera IP address (for GigE cameras). NEEDS_CONFIGURATION.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Connection type: "GigE" or "USB3". NEEDS_CONFIGURATION.</summary>
    public string ConnectionType { get; set; } = string.Empty;

    /// <summary>Exposure time in microseconds. NEEDS_CONFIGURATION.</summary>
    public double Exposure { get; set; }

    /// <summary>Gain value. NEEDS_CONFIGURATION.</summary>
    public double Gain { get; set; }

    /// <summary>Trigger mode: e.g. "On", "Off". NEEDS_CONFIGURATION.</summary>
    public string TriggerMode { get; set; } = string.Empty;

    /// <summary>Trigger source: e.g. "Software", "Line1". NEEDS_CONFIGURATION.</summary>
    public string TriggerSource { get; set; } = string.Empty;

    /// <summary>Pixel format: e.g. "Mono8", "BayerRG8". NEEDS_CONFIGURATION.</summary>
    public string PixelFormat { get; set; } = string.Empty;

    /// <summary>Image width in pixels. 0 = use camera default. NEEDS_CONFIGURATION.</summary>
    public int Width { get; set; }

    /// <summary>Image height in pixels. 0 = use camera default. NEEDS_CONFIGURATION.</summary>
    public int Height { get; set; }

    /// <summary>Frame rate (for live preview). 0 = use camera default. NEEDS_CONFIGURATION.</summary>
    public double FrameRate { get; set; }

    /// <summary>Lines per live frame on line scan cameras. 0 keeps the capture height.</summary>
    public int LiveHeight { get; set; } = 64;

    /// <summary>Maximum live exposure in microseconds on line scan cameras. 0 keeps capture exposure.</summary>
    public double LiveExposure { get; set; } = 1000;

    /// <summary>Capture timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; }
}
