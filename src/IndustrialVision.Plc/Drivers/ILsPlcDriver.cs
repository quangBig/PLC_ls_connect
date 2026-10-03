namespace IndustrialVision.Plc.Drivers;

/// <summary>
/// Abstraction for low-level LS PLC communication driver.
/// 
/// ❌ NEEDS_PLC_PROTOCOL_INFORMATION
/// LS PLC models (XGB, XGK, XGI, XEC) support various protocols:
///   - XGT Dedicated FEnet (TCP/IP Ethernet)
///   - Modbus TCP / RTU
///   - Cnet (RS232/RS485 Serial)
/// 
/// Implementations of this interface will handle wire-level frame encoding/decoding
/// without affecting the high-level PlcService, workflow, or UI.
/// </summary>
public interface ILsPlcDriver : IDisposable
{
    /// <summary>Driver protocol name.</summary>
    string ProtocolName { get; }

    /// <summary>Open communication link to the LS PLC.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Close communication link.</summary>
    Task DisconnectAsync();

    /// <summary>Check if low-level link is alive.</summary>
    Task<bool> IsConnectedAsync();

    /// <summary>Read single bit from address.</summary>
    Task<bool> ReadBitAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Write single bit to address.</summary>
    Task WriteBitAsync(string address, bool value, CancellationToken cancellationToken = default);

    /// <summary>Read 16-bit word/register from address.</summary>
    Task<ushort> ReadWordAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Write 16-bit word/register to address.</summary>
    Task WriteWordAsync(string address, ushort value, CancellationToken cancellationToken = default);

    /// <summary>Read string from address with specified character length.</summary>
    Task<string> ReadStringAsync(string address, int length, CancellationToken cancellationToken = default);

    /// <summary>Write string to address.</summary>
    Task WriteStringAsync(string address, string value, CancellationToken cancellationToken = default);
}
