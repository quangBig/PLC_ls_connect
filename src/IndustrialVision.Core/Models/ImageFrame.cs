namespace IndustrialVision.Core.Models;

/// <summary>
/// Represents a captured image frame from the camera.
/// This is a camera-agnostic wrapper — services should produce and consume this model,
/// not vendor-specific types like IGrabResult or Bitmap.
/// </summary>
public sealed class ImageFrame : IDisposable
{
    /// <summary>Raw pixel data of the image.</summary>
    public byte[] PixelData { get; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Number of channels (1 = Mono, 3 = RGB).</summary>
    public int Channels { get; }

    /// <summary>Pixel format name (e.g. "Mono8", "BayerRG8", "RGB8").</summary>
    public string PixelFormat { get; }

    /// <summary>Timestamp when the image was captured.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Stride (bytes per row).</summary>
    public int Stride { get; }

    private bool _disposed;

    public ImageFrame(
        byte[] pixelData,
        int width,
        int height,
        int channels,
        string pixelFormat,
        DateTime timestamp)
    {
        PixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
        Width = width;
        Height = height;
        Channels = channels;
        PixelFormat = pixelFormat ?? string.Empty;
        Timestamp = timestamp;
        Stride = width * channels;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // PixelData is a managed byte array — GC handles it.
        // This IDisposable is here for future extensibility if we add unmanaged resources.
    }
}
