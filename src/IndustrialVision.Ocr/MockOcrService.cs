using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Core.Models;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Ocr;

/// <summary>
/// Mock OCR service for Simulation Mode.
/// Returns a simulated OCR result for testing workflow.
/// 
/// This is NOT a real OCR implementation.
/// 
/// ❌ CHƯA THỂ IMPLEMENT HARDWARE THẬT
/// Reason: OCR interface specification from anh Hiệp is UNKNOWN.
/// Required:
///   1. Interface type (DLL / EXE / HTTP API / .NET class library)
///   2. Input format specification
///   3. Output format specification
///   4. File paths or API URLs
/// </summary>
public sealed class MockOcrService : IOcrService
{
    private readonly OcrConfiguration _config;
    private readonly ILogger<MockOcrService> _logger;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _disposed;
    private int _cycleCounter;

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

    public bool IsReady => _status == ConnectionStatus.Ready;

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public MockOcrService(OcrConfiguration config, ILogger<MockOcrService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATION] OCR service initializing...");
        Status = ConnectionStatus.Connecting;
        Status = ConnectionStatus.Connected;
        Status = ConnectionStatus.Ready;
        _logger.LogInformation("[SIMULATION] OCR service ready.");
        return Task.CompletedTask;
    }

    public Task<OcrResult> ProcessAsync(ImageFrame image, CancellationToken cancellationToken = default)
    {
        if (!IsReady)
        {
            return Task.FromResult(new OcrResult
            {
                Success = false,
                ErrorMessage = "OCR service is not initialized."
            });
        }

        _cycleCounter++;
        _logger.LogInformation("[SIMULATION] OCR processing image {Width}x{Height}...",
            image.Width, image.Height);

        // Simulate OCR result
        var result = new OcrResult
        {
            Success = true,
            Text = $"SIM-{_cycleCounter:D6}",
            Confidence = 0.95,
            ProcessingTimeMs = 100,
            Timestamp = DateTime.Now
        };

        _logger.LogInformation("[SIMULATION] OCR result: Text={Text}, Confidence={Confidence:F2}",
            result.Text, result.Confidence);

        return Task.FromResult(result);
    }

    public Task<OcrResult> ProcessFromFileAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATION] OCR processing file: {Path}", imagePath);

        // Create a dummy frame for simulation
        var dummyFrame = new ImageFrame(
            new byte[640 * 480], 640, 480, 1, "Mono8", DateTime.Now);

        using (dummyFrame)
        {
            return ProcessAsync(dummyFrame, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Status = ConnectionStatus.Disconnected;
    }
}
