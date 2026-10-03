using IndustrialVision.Core.Enums;

namespace IndustrialVision.Core.Models;

/// <summary>
/// Represents one complete inspection cycle result.
/// </summary>
public sealed class InspectionCycleResult
{
    /// <summary>Unique identifier for this cycle.</summary>
    public string CycleId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>When the cycle started.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>When the cycle completed.</summary>
    public DateTime EndTime { get; set; }

    /// <summary>Total cycle time in milliseconds.</summary>
    public long CycleTimeMs => (long)(EndTime - StartTime).TotalMilliseconds;

    /// <summary>The inspection result (OK/NG/Unknown).</summary>
    public InspectionResult Result { get; set; } = InspectionResult.Unknown;

    /// <summary>OCR result from this cycle.</summary>
    public OcrResult? OcrResult { get; set; }

    /// <summary>Path to saved image file, if applicable.</summary>
    public string ImageSavePath { get; set; } = string.Empty;

    /// <summary>Error message if cycle failed.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Whether the cycle completed without error.</summary>
    public bool IsSuccess => Result != InspectionResult.Unknown && string.IsNullOrEmpty(ErrorMessage);
}
