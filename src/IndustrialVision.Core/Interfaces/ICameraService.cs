using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Models;

namespace IndustrialVision.Core.Interfaces;

/// <summary>
/// Camera service abstraction.
/// Only the implementation (e.g. HikrobotCameraService) may depend on vendor SDK.
/// UI and workflow must only use this interface.
/// </summary>
public interface ICameraService : IDisposable
{
    /// <summary>Current connection status of the camera.</summary>
    ConnectionStatus Status { get; }

    /// <summary>Whether the camera is connected and operational.</summary>
    bool IsConnected { get; }

    /// <summary>Whether live preview is active.</summary>
    bool IsLiveActive { get; }

    /// <summary>Raised when connection status changes.</summary>
    event EventHandler<ConnectionStatus> StatusChanged;

    /// <summary>Raised when a new frame is available during live preview.</summary>
    event EventHandler<ImageFrame> FrameReceived;

    /// <summary>Discover all available cameras on the network/bus.</summary>
    Task<IReadOnlyList<CameraDeviceInfo>> DiscoverCamerasAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Connect to the camera using current configuration.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnect from the camera and release resources.</summary>
    Task DisconnectAsync();

    /// <summary>Start live preview (continuous grabbing).</summary>
    Task StartLiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop live preview.</summary>
    Task StopLiveAsync();

    /// <summary>Capture a single image frame. Real hardware stops live preview before capture.</summary>
    Task<ImageFrame> CaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Set camera exposure time.</summary>
    Task SetExposureAsync(double value);

    /// <summary>Set camera gain.</summary>
    Task SetGainAsync(double value);
}
