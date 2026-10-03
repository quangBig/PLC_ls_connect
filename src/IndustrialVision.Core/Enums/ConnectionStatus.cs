namespace IndustrialVision.Core.Enums;

/// <summary>
/// Trạng thái kết nối của từng thiết bị riêng lẻ.
/// Mỗi thiết bị (PLC, Camera, Light, OCR) có trạng thái kết nối độc lập.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Chưa kết nối.</summary>
    Disconnected,

    /// <summary>Đang kết nối.</summary>
    Connecting,

    /// <summary>Đã kết nối.</summary>
    Connected,

    /// <summary>Đã kết nối và sẵn sàng hoạt động.</summary>
    Ready,

    /// <summary>Lỗi kết nối.</summary>
    Error
}
