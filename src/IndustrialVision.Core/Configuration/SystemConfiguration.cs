namespace IndustrialVision.Core.Configuration;

/// <summary>
/// System-wide configuration — all values read from appsettings.json.
/// </summary>
public sealed class SystemConfiguration
{
    /// <summary>
    /// When true, all hardware services use mock/simulation implementations.
    /// When false, real hardware services are used.
    /// </summary>
    public bool SimulationMode { get; set; }

    /// <summary>Base path for saving captured images. NEEDS_CONFIGURATION.</summary>
    public string ImageSavePath { get; set; } = string.Empty;

    /// <summary>Base path for log files. NEEDS_CONFIGURATION.</summary>
    public string LogPath { get; set; } = string.Empty;

    /// <summary>Application title displayed on HMI.</summary>
    public string ApplicationTitle { get; set; } = "OCR INSPECTION SYSTEM";

    /// <summary>Timeout configuration.</summary>
    public TimeoutConfiguration Timeouts { get; set; } = new();

    /// <summary>Retry configuration.</summary>
    public RetryConfiguration Retry { get; set; } = new();
}

/// <summary>
/// Timeout configuration — all values in milliseconds.
/// These are placeholder values. NEEDS_CONFIGURATION for production.
/// </summary>
public sealed class TimeoutConfiguration
{
    /// <summary>PLC connection timeout (ms).</summary>
    public int PlcConnectMs { get; set; }

    /// <summary>Camera connection timeout (ms).</summary>
    public int CameraConnectMs { get; set; }

    /// <summary>Camera capture timeout (ms).</summary>
    public int CameraCaptureMs { get; set; }

    /// <summary>Light controller command timeout (ms).</summary>
    public int LightCommandMs { get; set; }

    /// <summary>OCR processing timeout (ms).</summary>
    public int OcrMs { get; set; }

    /// <summary>Complete cycle timeout (ms).</summary>
    public int CycleMs { get; set; }
}

/// <summary>
/// Retry configuration for communication failures.
/// </summary>
public sealed class RetryConfiguration
{
    /// <summary>Whether retry is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Number of retry attempts.</summary>
    public int Count { get; set; }

    /// <summary>Delay between retries in milliseconds.</summary>
    public int DelayMs { get; set; }
}
