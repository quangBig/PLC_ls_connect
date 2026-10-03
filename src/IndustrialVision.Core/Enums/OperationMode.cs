namespace IndustrialVision.Core.Enums;

/// <summary>
/// Chế độ hoạt động của ứng dụng.
/// </summary>
public enum OperationMode
{
    /// <summary>Chế độ tự động — chạy theo trigger PLC.</summary>
    Auto,

    /// <summary>Chế độ thủ công — kỹ thuật viên test từng thiết bị.</summary>
    Manual
}
