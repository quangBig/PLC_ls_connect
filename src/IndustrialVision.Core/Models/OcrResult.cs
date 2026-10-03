namespace IndustrialVision.Core.Models;

/// <summary>
/// Result from OCR/AI processing.
/// This model is used by the integration layer — not by the OCR algorithm itself.
/// </summary>
public sealed class OcrResult
{
    /// <summary>Whether OCR processing completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>The recognized text from the image.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Error message if processing failed.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Processing time in milliseconds.</summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>Timestamp when OCR processing completed.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
