namespace IndustrialVision.Core.Configuration;

/// <summary>
/// OCR service configuration — all values read from ocr.json.
/// 
/// ❌ CHƯA THỂ IMPLEMENT HARDWARE THẬT
/// Reason: OCR interface specification from anh Hiệp is UNKNOWN.
/// Required:
///   1. Interface type (DLL / EXE / HTTP API / .NET class library)
///   2. Input format specification
///   3. Output format specification
///   4. File paths or API URLs
/// </summary>
public sealed class OcrConfiguration
{
    /// <summary>
    /// OCR integration type: "DLL", "EXE", "HTTP", "ClassLibrary".
    /// NEEDS_PROTOCOL_INFORMATION — depends on anh Hiệp's delivery format.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Path to OCR DLL. NEEDS_CONFIGURATION.</summary>
    public string DllPath { get; set; } = string.Empty;

    /// <summary>Path to OCR executable. NEEDS_CONFIGURATION.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>OCR HTTP API URL. NEEDS_CONFIGURATION.</summary>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>Path to OCR model files. NEEDS_CONFIGURATION.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>OCR processing timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; }
}
