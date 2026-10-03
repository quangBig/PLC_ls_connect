using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Plc.Drivers;

/// <summary>
/// LS Electric XGT Dedicated Protocol (FEnet, TCP) driver, used for XGB XBM-DN32HP (built-in Ethernet).
///
/// NO hardcoded IP / port / device address: everything comes from <see cref="PlcConfiguration"/>
/// (IP/Port are typed in the UI) and device addresses are passed through exactly as the user typed
/// them (XGT variable names such as "%MW100", "%MX10", "%DB200"). Short forms ("M100") are rejected,
/// never guessed.
///
/// Frame (all multi-byte values little-endian):
///   Header (20 bytes) : "LSIS-XGT\0\0"(10) | PLC info(2) | CPU info(1) | source(1)
///                       | invokeId(2) | payloadLength(2) | FEnet position(1) | BCC(1)
///   Read  request  : cmd 0x0054, type, reserved, blockCount=1, varLen, var, [byteCount for continuous]
///   Write request  : cmd 0x0058, type, reserved, blockCount=1, varLen, var, dataLen, data
///   Response       : cmd (0x0055/0x0059), type, reserved, status(2)
///                    status != 0 -> NAK, followed by error code bytes
///                    status == 0 -> blockCount(2), size(2), data (read only)
///   type: 0x0000 bit, 0x0002 word, 0x0014 continuous (bytes)
///
/// NOTE: Written from the XGT protocol specification; validate on the real PLC.
/// Any NAK is surfaced as <see cref="PlcRequestException"/> with the raw hex code (no invented meanings).
/// </summary>
public sealed class LsXgtDedicatedDriver : ILsPlcDriver
{
    private static readonly byte[] CompanyId = Encoding.ASCII.GetBytes("LSIS-XGT\0\0");

    private const int HeaderLength = 20;
    private const byte CpuInfo = 0xA0;        // 0xA0 = XGK/XGB-type (client -> PLC)
    private const byte SourceOfFrame = 0x33;  // 0x33 = client (PC) -> PLC
    private const byte FEnetPosition = 0x00;  // 0 = built-in Ethernet port
    private const ushort CmdReadRequest = 0x0054;
    private const ushort CmdReadResponse = 0x0055;
    private const ushort CmdWriteRequest = 0x0058;
    private const ushort CmdWriteResponse = 0x0059;
    private const ushort TypeBit = 0x0000;
    private const ushort TypeWord = 0x0002;
    private const ushort TypeContinuous = 0x0014;
    private const int MaxContinuousBytes = 1400;

    // %<device letters><size letter><number>, e.g. %MW100, %DB200
    private static readonly Regex StringAddressRegex =
        new(@"^%(?<dev>[A-Z]{1,2})(?<size>[XBWDL])(?<num>\d+)$", RegexOptions.Compiled);

    private static readonly string[] SupportedProtocols = ["XGT_DEDICATED", "XGT_FENET", "FENET", "XGT"];

    private readonly PlcConfiguration _config;
    private readonly ILogger<LsXgtDedicatedDriver> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ushort _invokeId;
    private bool _disposed;

    public string ProtocolName => "LS_XGT_Dedicated";

    /// <summary>True if the protocol string from plc.json selects this driver.</summary>
    public static bool Supports(string? protocol) =>
        !string.IsNullOrWhiteSpace(protocol) &&
        SupportedProtocols.Contains(protocol.Trim(), StringComparer.OrdinalIgnoreCase);

    public LsXgtDedicatedDriver(PlcConfiguration config, ILogger<LsXgtDedicatedDriver> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── CONNECTION ────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await ConnectCoreAsync(cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _ioLock.WaitAsync();
        try
        {
            CloseSocket();
            _logger.LogInformation("[LS_XGT] Disconnected from PLC.");
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public Task<bool> IsConnectedAsync() =>
        Task.FromResult(_tcpClient != null && _tcpClient.Connected && _stream != null);

    /// <summary>Must be called while holding <see cref="_ioLock"/>.</summary>
    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        if (_tcpClient?.Connected == true && _stream != null)
            return;

        CloseSocket();

        string ip = _config.IpAddress?.Trim() ?? string.Empty;
        int port = _config.Port;

        if (string.IsNullOrWhiteSpace(ip) || ip.Contains("NEEDS_", StringComparison.OrdinalIgnoreCase))
            throw new PlcException("[PLC_CONFIG_ERROR] PLC IP address is not set. Enter the PLC IP in the PLC tab.");
        if (port <= 0 || port > 65535)
            throw new PlcException("[PLC_CONFIG_ERROR] PLC port is not set (XGT Dedicated default is 2004).");

        int connectTimeout = _config.ConnectionTimeoutMs > 0 ? _config.ConnectionTimeoutMs : 3000;
        _logger.LogInformation("[LS_XGT] Connecting to {Ip}:{Port} (timeout {Timeout} ms)...", ip, port, connectTimeout);

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeoutCts = new CancellationTokenSource(connectTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await client.ConnectAsync(ip, port, linked.Token);

            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            if (_config.ReadTimeoutMs > 0) client.ReceiveTimeout = _config.ReadTimeoutMs;
            if (_config.WriteTimeoutMs > 0) client.SendTimeout = _config.WriteTimeoutMs;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new PlcException($"[PLC_CONNECT_TIMEOUT] No response from {ip}:{port} within {connectTimeout} ms.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            throw new PlcException($"[PLC_CONNECT_FAILED] Cannot connect to {ip}:{port}: {ex.Message}", ex);
        }

        _tcpClient = client;
        _stream = client.GetStream();
        _logger.LogInformation("[LS_XGT] Connected to {Ip}:{Port}.", ip, port);
    }

    private void CloseSocket()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
    }

    // ── READ / WRITE BIT ──────────────────────────────────────────────

    public async Task<bool> ReadBitAsync(string address, CancellationToken cancellationToken = default)
    {
        string variable = ValidateVariable(address);
        byte[] data = await ExecuteReadAsync(BuildRequest(CmdReadRequest, TypeBit, variable), variable, cancellationToken);
        if (data.Length < 1)
            throw new PlcRequestException($"[LS_XGT] Empty bit data returned for {variable}.");
        return data[0] != 0;
    }

    public async Task WriteBitAsync(string address, bool value, CancellationToken cancellationToken = default)
    {
        string variable = ValidateVariable(address);
        byte[] request = BuildRequest(CmdWriteRequest, TypeBit, variable, writeData: [(byte)(value ? 1 : 0)]);
        await ExecuteWriteAsync(request, variable, cancellationToken);
    }

    // ── READ / WRITE WORD ─────────────────────────────────────────────

    public async Task<ushort> ReadWordAsync(string address, CancellationToken cancellationToken = default)
    {
        string variable = ValidateVariable(address);
        byte[] data = await ExecuteReadAsync(BuildRequest(CmdReadRequest, TypeWord, variable), variable, cancellationToken);
        if (data.Length < 2)
            throw new PlcRequestException($"[LS_XGT] Expected 2 bytes for word {variable}, got {data.Length}.");
        return BitConverter.ToUInt16(data, 0); // little-endian
    }

    public async Task WriteWordAsync(string address, ushort value, CancellationToken cancellationToken = default)
    {
        string variable = ValidateVariable(address);
        byte[] request = BuildRequest(CmdWriteRequest, TypeWord, variable, writeData: BitConverter.GetBytes(value));
        await ExecuteWriteAsync(request, variable, cancellationToken);
    }

    // ── READ / WRITE STRING ───────────────────────────────────────────

    public async Task<string> ReadStringAsync(string address, int length, CancellationToken cancellationToken = default)
    {
        if (length <= 0) return string.Empty;
        if (length > MaxContinuousBytes)
            throw new PlcRequestException($"[LS_XGT] String length {length} exceeds max {MaxContinuousBytes} bytes.");

        string variable = ToByteVariable(address);
        byte[] request = BuildRequest(CmdReadRequest, TypeContinuous, variable, readCount: (ushort)length);
        byte[] data = await ExecuteReadAsync(request, variable, cancellationToken);

        int nul = Array.IndexOf(data, (byte)0);
        int len = nul >= 0 ? nul : data.Length;
        return Encoding.ASCII.GetString(data, 0, len).TrimEnd(' ');
    }

    /// <summary>
    /// ASSUMPTION (confirm with PLC programmer): ASCII, NUL-terminated, padded with NUL to an even
    /// number of bytes so whole words are written.
    /// </summary>
    public async Task WriteStringAsync(string address, string value, CancellationToken cancellationToken = default)
    {
        string variable = ToByteVariable(address);
        byte[] ascii = Encoding.ASCII.GetBytes(value ?? string.Empty);

        int total = ascii.Length + 1;     // + NUL terminator
        if (total % 2 != 0) total++;      // pad to whole word
        if (total > MaxContinuousBytes)
            throw new PlcRequestException($"[LS_XGT] String too long ({total} bytes > {MaxContinuousBytes}).");

        byte[] data = new byte[total];
        Buffer.BlockCopy(ascii, 0, data, 0, ascii.Length);

        byte[] request = BuildRequest(CmdWriteRequest, TypeContinuous, variable, writeData: data);
        await ExecuteWriteAsync(request, variable, cancellationToken);
    }

    // ── ADDRESS VALIDATION (no guessing) ──────────────────────────────

    private static string ValidateVariable(string address)
    {
        string a = (address ?? string.Empty).Trim().ToUpperInvariant();
        if (a.Length == 0)
            throw new PlcRequestException("[PLC_ADDRESS_ERROR] Address is empty.");
        if (!a.StartsWith('%'))
            throw new PlcRequestException(
                $"[PLC_ADDRESS_ERROR] '{address}' is not an XGT variable name. Use the full form, e.g. %MX100 (bit), %MW100 (word), %DW100.");
        if (a.Length < 4 || a.Length > 16 || a.Any(c => c > 127 || char.IsWhiteSpace(c)))
            throw new PlcRequestException($"[PLC_ADDRESS_ERROR] '{address}' is not a valid XGT variable name.");
        return a;
    }

    /// <summary>String access is byte-based: %xWn (word n) -> %xB(2n), %xBn stays as is.</summary>
    private static string ToByteVariable(string address)
    {
        string a = ValidateVariable(address);
        var m = StringAddressRegex.Match(a);
        if (!m.Success)
            throw new PlcRequestException(
                $"[PLC_ADDRESS_ERROR] '{address}' invalid for string. Use %<dev>B<n> (byte) or %<dev>W<n> (word), e.g. %DB200 or %DW100.");

        string dev = m.Groups["dev"].Value;
        string size = m.Groups["size"].Value;
        long num = long.Parse(m.Groups["num"].Value);

        return size switch
        {
            "B" => a,
            "W" => $"%{dev}B{num * 2}",
            _ => throw new PlcRequestException(
                $"[PLC_ADDRESS_ERROR] '{address}': string address must be byte (B) or word (W) based.")
        };
    }

    // ── FRAME BUILD / PARSE ───────────────────────────────────────────

    /// <summary>Builds the application payload (without header).</summary>
    private static byte[] BuildRequest(ushort command, ushort dataType, string variable,
        ushort? readCount = null, byte[]? writeData = null)
    {
        byte[] varBytes = Encoding.ASCII.GetBytes(variable);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(command);
        w.Write(dataType);
        w.Write((ushort)0);                 // reserved
        w.Write((ushort)1);                 // block count
        w.Write((ushort)varBytes.Length);
        w.Write(varBytes);
        if (readCount.HasValue)
        {
            w.Write(readCount.Value);       // byte count (continuous read)
        }
        if (writeData != null)
        {
            w.Write((ushort)writeData.Length);
            w.Write(writeData);
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Wraps payload with the 20-byte header. Must run under <see cref="_ioLock"/>.</summary>
    private byte[] BuildFrame(byte[] payload)
    {
        byte[] frame = new byte[HeaderLength + payload.Length];
        Buffer.BlockCopy(CompanyId, 0, frame, 0, CompanyId.Length);
        // [10..11] PLC info = 0
        frame[12] = CpuInfo;
        frame[13] = SourceOfFrame;
        _invokeId++;
        frame[14] = (byte)(_invokeId & 0xFF);
        frame[15] = (byte)(_invokeId >> 8);
        frame[16] = (byte)(payload.Length & 0xFF);
        frame[17] = (byte)(payload.Length >> 8);
        frame[18] = FEnetPosition;

        int sum = 0;
        for (int i = 0; i < 19; i++) sum += frame[i];
        frame[19] = (byte)(sum & 0xFF);     // BCC = byte sum of header bytes 0..18

        Buffer.BlockCopy(payload, 0, frame, HeaderLength, payload.Length);
        return frame;
    }

    private async Task<byte[]> ExecuteReadAsync(byte[] payload, string variable, CancellationToken ct)
    {
        byte[] response = await ExchangeAsync(payload, ct);
        return ParseResponse(response, CmdReadResponse, variable, expectData: true);
    }

    private async Task ExecuteWriteAsync(byte[] payload, string variable, CancellationToken ct)
    {
        byte[] response = await ExchangeAsync(payload, ct);
        ParseResponse(response, CmdWriteResponse, variable, expectData: false);
    }

    /// <summary>Returns the response payload (header stripped).</summary>
    private async Task<byte[]> ExchangeAsync(byte[] payload, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct);
        try
        {
            await ConnectCoreAsync(ct);

            try
            {
                NetworkStream stream = _stream!;

                // Drop stale bytes from a previous (timed-out) exchange
                while (stream.DataAvailable) stream.ReadByte();

                byte[] frame = BuildFrame(payload);
                ushort sentInvokeId = _invokeId;

                await stream.WriteAsync(frame, ct);
                await stream.FlushAsync(ct);

                byte[] header = new byte[HeaderLength];
                await ReadExactAsync(stream, header, HeaderLength, ct);

                for (int i = 0; i < CompanyId.Length; i++)
                {
                    if (header[i] != CompanyId[i])
                        throw new IOException("[LS_XGT] Response is not an XGT frame (Company ID mismatch).");
                }

                ushort rxInvoke = (ushort)(header[14] | (header[15] << 8));
                if (rxInvoke != sentInvokeId)
                    _logger.LogWarning("[LS_XGT] Invoke ID mismatch (sent {Sent}, received {Rx}).", sentInvokeId, rxInvoke);

                int payloadLength = header[16] | (header[17] << 8);
                byte[] body = new byte[payloadLength];
                if (payloadLength > 0)
                    await ReadExactAsync(stream, body, payloadLength, ct);

                return body;
            }
            catch (Exception ex) when (ex is not PlcException)
            {
                // Transport problem: drop the socket; next call reconnects lazily.
                _logger.LogWarning(ex, "[LS_XGT] Transport error. Closing socket.");
                CloseSocket();
                if (ex is OperationCanceledException) throw;
                throw new PlcException($"[PLC_COMM_ERROR] {ex.Message}", ex);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct);
            if (read == 0)
                throw new IOException("PLC closed the TCP connection.");
            total += read;
        }
    }

    /// <summary>Parses a response payload. Returns data bytes for reads, empty for writes.</summary>
    private static byte[] ParseResponse(byte[] p, ushort expectedCmd, string variable, bool expectData)
    {
        if (p.Length < 8)
            throw new PlcRequestException($"[LS_XGT] Response too short ({p.Length} bytes) for {variable}.");

        ushort cmd = (ushort)(p[0] | (p[1] << 8));
        ushort status = (ushort)(p[6] | (p[7] << 8));

        if (status != 0)
        {
            // NAK: error code follows; size not assumed -> report whatever remains in hex.
            string codeHex = p.Length > 8 ? Convert.ToHexString(p, 8, p.Length - 8) : "(none)";
            throw new PlcRequestException(
                $"[PLC_NAK] PLC rejected request for {variable}: status=0x{status:X4}, errorCode=0x{codeHex}.");
        }

        if (cmd != expectedCmd)
            throw new PlcRequestException(
                $"[LS_XGT] Unexpected response command 0x{cmd:X4} (expected 0x{expectedCmd:X4}) for {variable}.");

        if (!expectData)
            return [];

        if (p.Length < 12)
            throw new PlcRequestException($"[LS_XGT] Read response for {variable} has no data block.");

        int size = p[10] | (p[11] << 8);
        if (p.Length < 12 + size)
            throw new PlcRequestException(
                $"[LS_XGT] Read response truncated for {variable} (size {size}, available {p.Length - 12}).");

        byte[] data = new byte[size];
        Buffer.BlockCopy(p, 12, data, 0, size);
        return data;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseSocket();
        _ioLock.Dispose();
    }
}
