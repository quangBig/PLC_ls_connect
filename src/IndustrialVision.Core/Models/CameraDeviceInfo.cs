namespace IndustrialVision.Core.Models;

/// <summary>
/// Information about a discovered camera device.
/// Used by camera discovery to list available cameras for the user to select.
/// </summary>
public sealed class CameraDeviceInfo
{
    /// <summary>Camera friendly name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Camera serial number — primary key for identification.</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Camera model name.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Interface type (e.g. "GigE", "USB3").</summary>
    public string InterfaceType { get; set; } = string.Empty;

    /// <summary>IP address if GigE camera.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>MAC address if available.</summary>
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>Manufacturer name.</summary>
    public string Manufacturer { get; set; } = string.Empty;

    public override string ToString()
        => $"{ModelName} (SN: {SerialNumber}) [{InterfaceType}] {IpAddress}";
}
