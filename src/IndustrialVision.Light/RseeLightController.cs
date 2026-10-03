using System.Net.Sockets;
using System.Text;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Exceptions;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Light;

/// <summary>
/// Real light controller driver for Rsee PW-D-24W20-8TE (8-channel Industrial LED Controller).
/// Connects via LAN Ethernet (TCP/IP socket client).
/// 
/// Hardware specifications:
///   - Brand / Model: Rsee PW-D-24W20-8TE (Dongguan Rsee Optoelectronics)
///   - Channels: 8 independent channels (CH1 to CH8)
///   - Interface: RJ45 LAN (TCP/IP)
///   - Default IP: 192.168.1.100 (configurable in light.json)
///   - Default Port: 5000 (configurable in light.json)
///   - Intensity range: 0 - 255 per channel
/// 
/// Supported protocols:
///   - "ASCII_WORDOP" (Default):
///       Set intensity: S<ChannelLetter><Intensity:D4># (e.g. CH1=100 -> SA0100#, CH8=255 -> SH0255#)
///       Turn ON:       SH<ChannelNumber># or set saved intensity
///       Turn OFF:      SL<ChannelNumber># or set intensity 0000
///   - "ASCII_OPT":
///       Set intensity: $3<ChannelNumber><Intensity:D4># (e.g. CH1=100 -> $310100#)
///       Turn ON:       $1<ChannelNumber>#
///       Turn OFF:      $2<ChannelNumber>#
///   - "HEX":
///       Binary frame: [0xAA, 0x01, channel, intensity, checksum, 0x55]
/// </summary>
public sealed class RseeLightController : ILightController
{
    private readonly LightConfiguration _config;
    private readonly ILogger<RseeLightController> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _disposed;

    private readonly int[] _channelIntensities = new int[9]; // 1-based indexing for CH1-CH8
    private readonly bool[] _channelStates = new bool[9];

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsConnected => _status == ConnectionStatus.Connected || _status == ConnectionStatus.Ready;
    public int ChannelCount => _config.ChannelCount > 0 ? _config.ChannelCount : 8;

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public RseeLightController(LightConfiguration config, ILogger<RseeLightController> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize channels from configuration
        int defaultIntensity = _config.DefaultIntensity > 0 ? _config.DefaultIntensity : 100;
        for (int i = 1; i <= 8; i++)
        {
            _channelIntensities[i] = defaultIntensity;
            _channelStates[i] = false;
        }

        // Apply per-channel configuration overrides
        if (_config.Channels != null)
        {
            foreach (var ch in _config.Channels)
            {
                if (ch.Channel >= 1 && ch.Channel <= 8)
                {
                    if (ch.Intensity > 0)
                        _channelIntensities[ch.Channel] = ch.Intensity;
                }
            }
        }
    }

    /// <summary>
    /// Connect to Rsee PW-D-24W20-8TE controller via TCP/IP LAN.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger.LogInformation("[Rsee PW-D-24W20-8TE] Already connected to {Ip}:{Port}.", _config.IpAddress, _config.Port);
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.IpAddress))
        {
            Status = ConnectionStatus.Error;
            throw new LightControllerException(
                "Light.IpAddress is not configured in light.json. Example: '192.168.1.100'");
        }

        int port = _config.Port > 0 ? _config.Port : 5000;
        int timeoutMs = _config.CommandTimeoutMs > 0 ? _config.CommandTimeoutMs : 3000;

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] Connecting to controller at {Ip}:{Port} (Timeout: {Timeout}ms)...",
            _config.IpAddress, port, timeoutMs);

        Status = ConnectionStatus.Connecting;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            CleanupSocket();

            _tcpClient = new TcpClient
            {
                NoDelay = true,
                SendTimeout = timeoutMs,
                ReceiveTimeout = timeoutMs
            };

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(timeoutMs);

            await _tcpClient.ConnectAsync(_config.IpAddress, port, connectCts.Token);
            _stream = _tcpClient.GetStream();

            Status = ConnectionStatus.Connected;
            Status = ConnectionStatus.Ready;

            _logger.LogInformation("[Rsee PW-D-24W20-8TE] Connected successfully to {Ip}:{Port} via LAN. Ready to control 8 channels.",
                _config.IpAddress, port);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Status = ConnectionStatus.Error;
            _logger.LogError("[Rsee PW-D-24W20-8TE] Connection timeout ({Timeout}ms) to {Ip}:{Port}. Please check LAN cable and IP address.",
                timeoutMs, _config.IpAddress, port);
            throw new LightControllerException(
                $"Connection timeout to Rsee light controller at {_config.IpAddress}:{port}. Check LAN cable, IP settings, and power.");
        }
        catch (Exception ex)
        {
            Status = ConnectionStatus.Error;
            _logger.LogError(ex, "[Rsee PW-D-24W20-8TE] Failed to connect to {Ip}:{Port}: {Message}",
                _config.IpAddress, port, ex.Message);
            throw new LightControllerException(
                $"Failed to connect to Rsee light controller at {_config.IpAddress}:{port}: {ex.Message}", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Disconnect from the light controller.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _logger.LogInformation("[Rsee PW-D-24W20-8TE] Disconnecting...");

        await _lock.WaitAsync();
        try
        {
            CleanupSocket();
            Status = ConnectionStatus.Disconnected;
            _logger.LogInformation("[Rsee PW-D-24W20-8TE] Disconnected.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Set intensity for a specific channel (CH1 to CH8), value 0 to 255.
    /// </summary>
    public async Task SetChannelAsync(int channel, int intensity, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        intensity = Math.Clamp(intensity, 0, 255);

        _channelIntensities[channel] = intensity;
        if (intensity > 0)
        {
            _channelStates[channel] = true;
        }

        await SendIntensityCommandAsync(channel, intensity, cancellationToken);
    }

    /// <summary>
    /// Turn on a specific channel at its currently set intensity (or default if 0).
    /// </summary>
    public async Task TurnOnAsync(int channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        int intensity = _channelIntensities[channel];
        if (intensity <= 0)
        {
            intensity = _config.DefaultIntensity > 0 ? _config.DefaultIntensity : 100;
            _channelIntensities[channel] = intensity;
        }

        _channelStates[channel] = true;
        await SendTurnOnCommandAsync(channel, intensity, cancellationToken);
    }

    /// <summary>
    /// Turn off a specific channel (sets intensity to 0 or sends OFF command).
    /// </summary>
    public async Task TurnOffAsync(int channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        _channelStates[channel] = false;

        await SendTurnOffCommandAsync(channel, cancellationToken);
    }

    /// <summary>
    /// Turn off all 8 channels simultaneously.
    /// </summary>
    public async Task TurnOffAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Rsee PW-D-24W20-8TE] Turning off all channels (CH1-CH8)...");

        for (int ch = 1; ch <= ChannelCount; ch++)
        {
            _channelStates[ch] = false;
        }

        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();
        if (protocol.Contains("WORDOP"))
        {
            // Batch command to set all A-H to 0000
            var sb = new StringBuilder();
            for (int ch = 1; ch <= ChannelCount; ch++)
            {
                char chLetter = (char)('A' + ch - 1);
                sb.Append($"S{chLetter}0000#");
            }
            await SendRawAsciiInternalAsync(sb.ToString(), cancellationToken);
        }
        else
        {
            for (int ch = 1; ch <= ChannelCount; ch++)
            {
                await TurnOffAsync(ch, cancellationToken);
            }
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] All channels turned OFF.");
    }

    /// <summary>
    /// Get current intensity stored for a channel (1 to 8).
    /// </summary>
    public int GetChannelIntensity(int channel)
    {
        ValidateChannel(channel);
        return _channelIntensities[channel];
    }

    /// <summary>
    /// Get current ON/OFF state for a channel (1 to 8).
    /// </summary>
    public bool IsChannelOn(int channel)
    {
        ValidateChannel(channel);
        return _channelStates[channel];
    }

    // ── Command formatting and sending ──────────────────────────────────

    private async Task SendIntensityCommandAsync(int channel, int intensity, CancellationToken cancellationToken)
    {
        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();

        if (protocol.Contains("OPT"))
        {
            // OPT protocol format: $3<channel><intensity:D4># (e.g. $310255#)
            string command = $"$3{channel}{intensity:D4}#";
            await SendRawAsciiInternalAsync(command, cancellationToken);
        }
        else if (protocol.Contains("HEX"))
        {
            // Binary frame format: [0xAA, 0x01, channel, intensity, checksum, 0x55]
            byte checksum = (byte)((0xAA + 0x01 + channel + intensity) & 0xFF);
            byte[] packet = [0xAA, 0x01, (byte)channel, (byte)intensity, checksum, 0x55];
            await SendRawBytesInternalAsync(packet, cancellationToken);
        }
        else
        {
            // Standard Rsee / Wordop / CST format: S<ChannelLetter><Intensity:D4># (e.g. SA0100#)
            char chLetter = (char)('A' + channel - 1);
            string command = $"S{chLetter}{intensity:D4}#";
            await SendRawAsciiInternalAsync(command, cancellationToken);
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] CH{Channel} set to intensity {Intensity}/255", channel, intensity);
    }

    private async Task SendTurnOnCommandAsync(int channel, int intensity, CancellationToken cancellationToken)
    {
        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();

        if (protocol.Contains("OPT"))
        {
            // OPT: First ensure intensity is set, then turn ON ($1<channel>#)
            string setCmd = $"$3{channel}{intensity:D4}#";
            string onCmd = $"$1{channel}#";
            await SendRawAsciiInternalAsync(setCmd + onCmd, cancellationToken);
        }
        else if (protocol.Contains("HEX"))
        {
            byte checksum = (byte)((0xAA + 0x01 + channel + intensity) & 0xFF);
            byte[] packet = [0xAA, 0x01, (byte)channel, (byte)intensity, checksum, 0x55];
            await SendRawBytesInternalAsync(packet, cancellationToken);
        }
        else
        {
            // Wordop / Rsee: Set intensity > 0 turns on, plus optionally SH<channel>#
            char chLetter = (char)('A' + channel - 1);
            string setCmd = $"S{chLetter}{intensity:D4}#";
            string onCmd = $"SH{channel}#";
            await SendRawAsciiInternalAsync(setCmd + onCmd, cancellationToken);
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] CH{Channel} turned ON (Intensity: {Intensity})", channel, intensity);
    }

    private async Task SendTurnOffCommandAsync(int channel, CancellationToken cancellationToken)
    {
        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();

        if (protocol.Contains("OPT"))
        {
            string offCmd = $"$2{channel}#";
            await SendRawAsciiInternalAsync(offCmd, cancellationToken);
        }
        else if (protocol.Contains("HEX"))
        {
            byte checksum = (byte)((0xAA + 0x02 + channel + 0) & 0xFF);
            byte[] packet = [0xAA, 0x02, (byte)channel, 0x00, checksum, 0x55];
            await SendRawBytesInternalAsync(packet, cancellationToken);
        }
        else
        {
            char chLetter = (char)('A' + channel - 1);
            string offCmd = $"S{chLetter}0000#SL{channel}#";
            await SendRawAsciiInternalAsync(offCmd, cancellationToken);
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] CH{Channel} turned OFF", channel);
    }

    /// <summary>
    /// Send raw ASCII command over the network stream.
    /// Thread-safe and logs bytes sent and received.
    /// </summary>
    public async Task<string> SendRawAsciiAsync(string command, CancellationToken cancellationToken = default)
    {
        return await SendRawAsciiInternalAsync(command, cancellationToken);
    }

    private async Task<string> SendRawAsciiInternalAsync(string command, CancellationToken cancellationToken)
    {
        EnsureConnected();

        byte[] bytes = Encoding.ASCII.GetBytes(command);
        byte[] responseBytes = await SendAndReceiveBytesAsync(bytes, cancellationToken);
        string response = responseBytes.Length > 0 ? Encoding.ASCII.GetString(responseBytes) : string.Empty;

        _logger.LogDebug("[Rsee PW-D-24W20-8TE] TX: '{Command}' | RX: '{Response}'", command, response);
        return response;
    }

    private async Task<byte[]> SendRawBytesInternalAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        EnsureConnected();
        byte[] response = await SendAndReceiveBytesAsync(bytes, cancellationToken);
        _logger.LogDebug("[Rsee PW-D-24W20-8TE] TX Hex: {TxHex} | RX Hex: {RxHex}",
            Convert.ToHexString(bytes), Convert.ToHexString(response));
        return response;
    }

    private async Task<byte[]> SendAndReceiveBytesAsync(byte[] data, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_stream == null || _tcpClient == null || !_tcpClient.Connected)
            {
                Status = ConnectionStatus.Error;
                throw new LightControllerException("Light controller is not connected to LAN socket.");
            }

            // Send command
            await _stream.WriteAsync(data, cancellationToken);
            await _stream.FlushAsync(cancellationToken);

            // Read response if available (non-blocking with short timeout)
            var responseList = new List<byte>();
            if (_stream.DataAvailable)
            {
                byte[] buffer = new byte[256];
                int read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read > 0)
                {
                    responseList.AddRange(buffer.AsSpan(0, read).ToArray());
                }
            }

            return responseList.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = ConnectionStatus.Error;
            _logger.LogError(ex, "[Rsee PW-D-24W20-8TE] Socket error during communication: {Message}", ex.Message);
            throw new LightControllerException($"Light controller communication error: {ex.Message}", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected || _stream == null || _tcpClient == null || !_tcpClient.Connected)
        {
            Status = ConnectionStatus.Error;
            throw new LightControllerException(
                $"Rsee PW-D-24W20-8TE is not connected. Call ConnectAsync first. Target: {_config.IpAddress}:{_config.Port}");
        }
    }

    private void ValidateChannel(int channel)
    {
        if (channel < 1 || channel > ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel),
                $"Channel must be between 1 and {ChannelCount}. Received: {channel}");
        }
    }

    private void CleanupSocket()
    {
        try { _stream?.Close(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _stream = null;

        try { _tcpClient?.Close(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _tcpClient = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CleanupSocket();
        _lock.Dispose();
        Status = ConnectionStatus.Disconnected;
    }
}
