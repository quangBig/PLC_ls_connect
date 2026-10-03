using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Models;

namespace IndustrialVision.Core.Interfaces;

/// <summary>
/// OCR/AI service abstraction.
/// The actual OCR algorithm is developed by another developer (anh Hiệp).
/// This interface is the integration layer only.
/// 
/// ❌ CHƯA THỂ IMPLEMENT HARDWARE THẬT
/// Reason: Missing OCR interface specification from anh Hiệp.
/// Required:
///   1. Interface type (DLL / EXE / HTTP API / .NET class library)
///   2. Input format (file path / byte array / stream)
///   3. Output format (string / JSON / custom object)
///   4. DLL/EXE/API location
/// </summary>
public interface IOcrService : IDisposable
{
    /// <summary>Current status of OCR service.</summary>
    ConnectionStatus Status { get; }

    /// <summary>Whether OCR service is initialized and ready.</summary>
    bool IsReady { get; }

    /// <summary>Raised when status changes.</summary>
    event EventHandler<ConnectionStatus> StatusChanged;

    /// <summary>Initialize the OCR service.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Process an image and return OCR result.</summary>
    Task<OcrResult> ProcessAsync(ImageFrame image, CancellationToken cancellationToken = default);

    /// <summary>Process an image from file path and return OCR result.</summary>
    Task<OcrResult> ProcessFromFileAsync(string imagePath, CancellationToken cancellationToken = default);
}
