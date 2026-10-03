using IndustrialVision.Core.Enums;

namespace IndustrialVision.Core.Interfaces;

/// <summary>
/// PLC communication service abstraction.
/// Independent of PLC brand (Siemens, Mitsubishi, Omron, etc.).
/// All PLC addresses come from configuration — never hardcoded.
/// </summary>
public interface IPlcService : IDisposable
{
    /// <summary>Current connection status.</summary>
    ConnectionStatus Status { get; }

    /// <summary>Whether PLC is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when connection status changes.</summary>
    event EventHandler<ConnectionStatus> StatusChanged;

    /// <summary>Connect to PLC using current configuration.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnect from PLC.</summary>
    Task DisconnectAsync();

    /// <summary>Check if PLC connection is alive asynchronously.</summary>
    Task<bool> IsConnectedAsync();

    /// <summary>Read a boolean (bit) value from PLC.</summary>
    Task<bool> ReadBoolAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Alias for ReadBoolAsync (bit).</summary>
    Task<bool> ReadBitAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Write a boolean (bit) value to PLC.</summary>
    Task WriteBoolAsync(string address, bool value, CancellationToken cancellationToken = default);

    /// <summary>Alias for WriteBoolAsync (bit).</summary>
    Task WriteBitAsync(string address, bool value, CancellationToken cancellationToken = default);

    /// <summary>Read a 16-bit word/register (ushort) from PLC.</summary>
    Task<ushort> ReadWordAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Write a 16-bit word/register (ushort) to PLC.</summary>
    Task WriteWordAsync(string address, ushort value, CancellationToken cancellationToken = default);

    /// <summary>Read a 32-bit integer value from PLC.</summary>
    Task<int> ReadIntAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Write a 32-bit integer value to PLC.</summary>
    Task WriteIntAsync(string address, int value, CancellationToken cancellationToken = default);

    /// <summary>Read a string value from PLC.</summary>
    Task<string> ReadStringAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Read a string value with specified character length from PLC.</summary>
    Task<string> ReadStringAsync(string address, int length, CancellationToken cancellationToken = default);

    /// <summary>Write a string value to PLC.</summary>
    Task WriteStringAsync(string address, string value, CancellationToken cancellationToken = default);
}
