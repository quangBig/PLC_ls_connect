namespace IndustrialVision.Core.Enums;

/// <summary>
/// Kết quả inspection: OK hoặc NG.
/// </summary>
public enum InspectionResult
{
    /// <summary>Chưa xác định — chưa có kết quả.</summary>
    Unknown,

    /// <summary>Sản phẩm đạt.</summary>
    OK,

    /// <summary>Sản phẩm không đạt.</summary>
    NG
}
