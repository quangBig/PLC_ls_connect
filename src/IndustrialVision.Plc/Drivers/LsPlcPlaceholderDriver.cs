using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Plc.Drivers;

/// <summary>
/// Placeholder driver for LS PLC pending official protocol specification.
/// 
/// ❌ NEEDS_PLC_PROTOCOL_INFORMATION
/// Required information:
///   1. Exact LS PLC model (XGB, XGK, XGI, XEC)
///   2. Communication card / protocol (FEnet XGT Dedicated, Modbus TCP, Cnet Serial)
///   3. Port and station/slot number
///   4. Memory address syntax (e.g. %MX, %MW, P, M, D, R)
/// </summary>
public sealed class LsPlcPlaceholderDriver : ILsPlcDriver
{
    private readonly PlcConfiguration _config;
    private readonly ILogger<LsPlcPlaceholderDriver> _logger;
    private bool _isConnected;
    private bool _disposed;

    public string ProtocolName => _config.Protocol;

    public LsPlcPlaceholderDriver(PlcConfiguration config, ILogger<LsPlcPlaceholderDriver> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[PLC LS DRIVER] Connect requested to {Ip}:{Port} (Model: {Model}, Protocol: {Protocol}). " +
            "⚠ Driver is in placeholder mode — NEEDS_PLC_PROTOCOL_INFORMATION.",
            _config.IpAddress, _config.Port, _config.Model, _config.Protocol);

        if (string.IsNullOrWhiteSpace(_config.IpAddress) || _config.Port <= 0)
        {
            throw new PlcException(
                "PLC IP address or port is not configured in plc.json. " +
                "Please configure 'PLC:IpAddress' and 'PLC:Port'.");
        }

        if (string.IsNullOrWhiteSpace(_config.Protocol) || _config.Protocol.Contains("NEEDS_"))
        {
            throw new PlcException(
                $"LS PLC protocol is not defined. Current value: '{_config.Protocol}'. " +
                "NEEDS_PLC_PROTOCOL_INFORMATION: Specify LS protocol (e.g. 'XGT_FENET', 'MODBUS_TCP', 'CNET').");
        }

        // Placeholder: When protocol is specified, real socket/serial connection will be opened here
        _isConnected = true;
        _logger.LogInformation("[PLC LS DRIVER] Link established to {Ip}:{Port}.", _config.IpAddress, _config.Port);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _isConnected = false;
        _logger.LogInformation("[PLC LS DRIVER] Disconnected.");
        return Task.CompletedTask;
    }

    public Task<bool> IsConnectedAsync()
    {
        return Task.FromResult(_isConnected);
    }

    public Task<bool> ReadBitAsync(string address, CancellationToken cancellationToken = default)
    {
        ValidateAddress(address, "ReadBit");
        throw new PlcException($"LS PLC ReadBit({address}) not implemented — NEEDS_PLC_PROTOCOL_INFORMATION.");
    }

    public Task WriteBitAsync(string address, bool value, CancellationToken cancellationToken = default)
    {
        ValidateAddress(address, "WriteBit");
        throw new PlcException($"LS PLC WriteBit({address}, {value}) not implemented — NEEDS_PLC_PROTOCOL_INFORMATION.");
    }

    public Task<ushort> ReadWordAsync(string address, CancellationToken cancellationToken = default)
    {
        ValidateAddress(address, "ReadWord");
        throw new PlcException($"LS PLC ReadWord({address}) not implemented — NEEDS_PLC_PROTOCOL_INFORMATION.");
    }

    public Task WriteWordAsync(string address, ushort value, CancellationToken cancellationToken = default)
    {
        ValidateAddress(address, "WriteWord");
        throw new PlcException($"LS PLC WriteWord({address}, {value}) not implemented — NEEDS_PLC_PROTOCOL_INFORMATION.");
    }

    public Task<string> ReadStringAsync(string address, int length, CancellationToken cancellationToken = default)
    {
        ValidateAddress(address, "ReadString");
        throw new PlcException($"LS PLC ReadString({address}, len:{length}) not implemented — NEEDS_PLC_PROTOCOL_INFORMATION.");
    }

    public Task WriteStringAsync(string address, string value, CancellationToken cancellationToken = default)
    {
        ValidateAddress(address, "WriteString");
        throw new PlcException($"LS PLC WriteString({address}, '{value}') not implemented — NEEDS_PLC_PROTOCOL_INFORMATION.");
    }

    private void ValidateAddress(string address, string op)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Contains("NEEDS_"))
        {
            throw new PlcException(
                $"PLC address for {op} is not configured: '{address}'. NEEDS_PLC_ADDRESS_INFORMATION.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isConnected = false;
    }
}
