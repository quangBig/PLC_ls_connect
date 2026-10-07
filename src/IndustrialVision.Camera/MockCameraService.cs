using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Core.Models;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Camera;

/// <summary>
/// Mock camera service for Simulation Mode.
/// Generates synthetic images for testing workflow without real hardware.
/// 
/// This is NOT a real camera implementation.
/// Real hardware uses HikrobotCameraService.
/// </summary>
public sealed class MockCameraService : ICameraService
{
    private readonly CameraConfiguration _config;
    private readonly ILogger<MockCameraService> _logger;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _isLiveActive;
    private CancellationTokenSource? _liveCts;
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
    public bool IsLiveActive => _isLiveActive;

    public event EventHandler<ConnectionStatus>? StatusChanged;
    public event EventHandler<ImageFrame>? FrameReceived;

    public MockCameraService(CameraConfiguration config, ILogger<MockCameraService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<CameraDeviceInfo>> DiscoverCamerasAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATION] Discovering cameras...");

        var mockCameras = new List<CameraDeviceInfo>
        {
            new()
            {
                Name = "Mock Camera 1",
                SerialNumber = "SIM-001",
                ModelName = "Simulation Camera",
                InterfaceType = "Simulation",
                IpAddress = "0.0.0.0",
                Manufacturer = "Simulation"
            }
        };

        _logger.LogInformation("[SIMULATION] Found {Count} camera(s).", mockCameras.Count);
        return Task.FromResult<IReadOnlyList<CameraDeviceInfo>>(mockCameras);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATION] Camera connecting...");
        Status = ConnectionStatus.Connecting;

        // Simulate connection delay
        Status = ConnectionStatus.Connected;
        Status = ConnectionStatus.Ready;
        _logger.LogInformation("[SIMULATION] Camera connected and ready.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _logger.LogInformation("[SIMULATION] Camera disconnecting...");
        if (_isLiveActive)
        {
            _liveCts?.Cancel();
            _isLiveActive = false;
        }
        Status = ConnectionStatus.Disconnected;
        _logger.LogInformation("[SIMULATION] Camera disconnected.");
        return Task.CompletedTask;
    }

    public async Task StartLiveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("[SIMULATION] Cannot start live — camera not connected.");
            return;
        }

        _logger.LogInformation("[SIMULATION] Starting live preview...");
        _isLiveActive = true;
        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start background task to generate frames
        _ = Task.Run(async () =>
        {
            while (!_liveCts.Token.IsCancellationRequested && _isLiveActive)
            {
                try
                {
                    var frame = GenerateMockFrame();
                    FrameReceived?.Invoke(this, frame);
                    await Task.Delay(33, _liveCts.Token); // ~30 FPS
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _liveCts.Token);
    }

    public Task StopLiveAsync()
    {
        _logger.LogInformation("[SIMULATION] Stopping live preview...");
        _isLiveActive = false;
        _liveCts?.Cancel();
        _liveCts?.Dispose();
        _liveCts = null;
        return Task.CompletedTask;
    }

    public Task<ImageFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("[SIMULATION] Cannot capture — camera not connected.");
        }

        _logger.LogInformation("[SIMULATION] Capturing image...");
        var frame = GenerateMockFrame();
        _logger.LogInformation("[SIMULATION] Image captured: {Width}x{Height}", frame.Width, frame.Height);
        return Task.FromResult(frame);
    }

    public Task SetExposureAsync(double value)
    {
        _logger.LogInformation("[SIMULATION] Setting exposure to {Value}", value);
        return Task.CompletedTask;
    }

    public Task SetGainAsync(double value)
    {
        _logger.LogInformation("[SIMULATION] Setting gain to {Value}", value);
        return Task.CompletedTask;
    }

    private ImageFrame GenerateMockFrame()
    {
        // Generate a simple gray gradient test pattern
        int width = _config.Width > 0 ? _config.Width : 640;
        int height = _config.Height > 0 ? _config.Height : 480;
        var data = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                data[y * width + x] = (byte)((x + y) % 256);
            }
        }

        return new ImageFrame(data, width, height, 1, "Mono8", DateTime.Now);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _liveCts?.Cancel();
        _liveCts?.Dispose();
        _liveCts = null;
        _isLiveActive = false;
        Status = ConnectionStatus.Disconnected;
    }
}
