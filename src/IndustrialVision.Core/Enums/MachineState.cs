namespace IndustrialVision.Core.Enums;

/// <summary>
/// Trạng thái tổng thể của máy trong inspection cycle.
/// </summary>
public enum MachineState
{
    /// <summary>Ứng dụng đang khởi động.</summary>
    Starting,

    /// <summary>Đang khởi tạo các service.</summary>
    Initializing,

    /// <summary>Đang kết nối đến các thiết bị (PLC, Camera, Light, OCR).</summary>
    Connecting,

    /// <summary>Tất cả thiết bị đã sẵn sàng.</summary>
    Ready,

    /// <summary>Đang chờ tín hiệu trigger từ PLC.</summary>
    WaitingTrigger,

    /// <summary>Đang bật đèn.</summary>
    Lighting,

    /// <summary>Đang chụp ảnh từ camera.</summary>
    Capturing,

    /// <summary>Đang xử lý OCR/AI.</summary>
    Processing,

    /// <summary>Đang gửi kết quả về PLC.</summary>
    SendingResult,

    /// <summary>Cycle hoàn thành.</summary>
    Completed,

    /// <summary>Lỗi — hệ thống cần kiểm tra.</summary>
    Error,

    /// <summary>Hệ thống đã dừng.</summary>
    Stopped
}
