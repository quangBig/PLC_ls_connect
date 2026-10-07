using System.Net.Sockets;
using System.Text;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Exceptions;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Light;

/// <summary>
/// Driver điều khiển đèn Rsee PW-D-24W20-8TE qua mạng LAN bằng kết nối TCP.
/// C# là client, kết nối tới IP/port của controller lấy từ LightConfiguration (light.json).
/// Driver lưu cường độ và cờ bật/tắt cho 8 kênh CH1..CH8 trong bộ nhớ của ứng dụng.
/// Luồng lệnh: ViewModel gọi hàm -> tạo chuỗi/gói byte -> gửi qua NetworkStream -> đọc dữ liệu có sẵn.
/// Class này không chụp ảnh hoặc đọc PLC; ViewModel quyết định lúc bật đèn và lúc chụp.
///
/// Các định dạng lệnh được triển khai trong code, chọn bằng Light.Protocol:
/// ASCII_WORDOP (mặc định): CH1=100 gửi SA0100#; bật thêm SH1#, tắt bằng SA0000#SL1#.
/// ASCII_OPT: CH1=100 gửi $310100#; bật bằng $11#, tắt bằng $21#.
/// HEX: gói nhị phân gồm byte đầu, mã lệnh, kênh, cường độ, checksum và byte cuối.
/// Chọn định dạng phù hợp với controller đang sử dụng; kết nối TCP thành công chưa xác nhận lệnh hợp lệ.
/// </summary>
public sealed class RseeLightController : ILightController
{
    // Cấu hình dùng chung với ViewModel; logger ghi kết nối, thao tác và lỗi vào log ứng dụng.
    private readonly LightConfiguration _config;
    private readonly ILogger<RseeLightController> _logger;
    // Khóa cho kết nối/đóng socket và từng lượt gửi-nhận, tránh hai lệnh dùng stream cùng lúc.
    private readonly SemaphoreSlim _lock = new(1, 1);

    // TcpClient quản lý kết nối; NetworkStream dùng để ghi/đọc byte trên kết nối đó.
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _disposed;

    // Mảng 9 phần tử để dùng trực tiếp chỉ số 1..8; phần tử 0 không dùng.
    // Đây là giá trị lưu trong C#, không phải dữ liệu đọc lại từ controller.
    private readonly int[] _channelIntensities = new int[9];
    private readonly bool[] _channelStates = new bool[9];

    /// <summary>Trạng thái service; khi đổi trạng thái thì thông báo cho giao diện qua event.</summary>
    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                // ViewModel nhận sự kiện để cập nhật trạng thái và nút kết nối đèn.
                StatusChanged?.Invoke(this, value);
            }
        }
    }

    // Cờ trạng thái do code quản lý; không phải phép kiểm tra đèn có đang sáng ngoài đời hay không.
    public bool IsConnected => _status == ConnectionStatus.Connected || _status == ConnectionStatus.Ready;
    // Không cấu hình số kênh thì dùng 8; số kênh phải phù hợp bộ nhớ CH1..CH8 của driver.
    public int ChannelCount => _config.ChannelCount > 0 ? _config.ChannelCount : 8;

    public event EventHandler<ConnectionStatus>? StatusChanged;

    /// <summary>Nhận cấu hình và khởi tạo giá trị các kênh; chưa kết nối hay gửi lệnh đèn.</summary>
    public RseeLightController(LightConfiguration config, ILogger<RseeLightController> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Cường độ mặc định phải >0, nếu không dùng 100; cờ ban đầu của các kênh là OFF.
        int defaultIntensity = _config.DefaultIntensity > 0 ? _config.DefaultIntensity : 100;
        for (int i = 1; i <= 8; i++)
        {
            _channelIntensities[i] = defaultIntensity;
            _channelStates[i] = false;
        }

        // Cấu hình riêng từng kênh ghi đè giá trị mặc định nếu kênh hợp lệ và Intensity>0.
        // Giá trị 0 trong cấu hình không ghi đè ở đây; trạng thái Enabled được xử lý bên ViewModel.
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
    /// CONNECT: mở socket TCP tới controller theo IP/port cấu hình, rồi báo Ready.
    /// Ready ở đây nghĩa là kết nối đã mở; hàm chưa gửi lệnh kiểm tra hoặc đọc trạng thái đèn.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Nếu service đã ghi nhận Connected/Ready thì không mở lại kết nối.
        if (IsConnected)
        {
            _logger.LogInformation("[Rsee PW-D-24W20-8TE] Already connected to {Ip}:{Port}.", _config.IpAddress, _config.Port);
            return;
        }

        // IP rỗng là lỗi cấu hình trước khi thử mở socket.
        if (string.IsNullOrWhiteSpace(_config.IpAddress))
        {
            Status = ConnectionStatus.Error;
            throw new LightControllerException(
                "Light.IpAddress is not configured in light.json. Example: '192.168.1.100'");
        }

        // Port/timeout dùng cấu hình nếu >0; 5000 và 3000ms chỉ là giá trị dự phòng trong code.
        // IP/port thật phải là của controller hiện tại, ví dụ thiết bị người dùng: 192.168.110.10:9000.
        int port = _config.Port > 0 ? _config.Port : 5000;
        int timeoutMs = _config.CommandTimeoutMs > 0 ? _config.CommandTimeoutMs : 3000;

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] Connecting to controller at {Ip}:{Port} (Timeout: {Timeout}ms)...",
            _config.IpAddress, port, timeoutMs);

        Status = ConnectionStatus.Connecting;

        // Chờ lượt dùng socket; nếu người gọi yêu cầu hủy trong lúc chờ thì dừng tại đây.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Dọn socket cũ còn sót lại từ phiên trước hoặc lần kết nối lỗi.
            CleanupSocket();

            // NoDelay=true tắt cơ chế gom gói nhỏ của TCP, giúp gửi lệnh điều khiển sớm hơn.
            // SendTimeout/ReceiveTimeout chủ yếu áp dụng I/O đồng bộ; lệnh async còn dùng token.
            _tcpClient = new TcpClient
            {
                NoDelay = true,
                SendTimeout = timeoutMs,
                ReceiveTimeout = timeoutMs
            };

            // Liên kết hủy từ người gọi với hạn chờ kết nối riêng của driver.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(timeoutMs);

            // Mở kết nối rồi lấy stream để các lệnh sau ghi/đọc trên cùng socket.
            await _tcpClient.ConnectAsync(_config.IpAddress, port, connectCts.Token);
            _stream = _tcpClient.GetStream();

            Status = ConnectionStatus.Connected;
            Status = ConnectionStatus.Ready;

            _logger.LogInformation("[Rsee PW-D-24W20-8TE] Connected successfully to {Ip}:{Port} via LAN. Ready to control 8 channels.",
                _config.IpAddress, port);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Token của người gọi chưa hủy: lần hủy này đến từ CancelAfter, tức hết hạn kết nối.
            Status = ConnectionStatus.Error;
            _logger.LogError("[Rsee PW-D-24W20-8TE] Connection timeout ({Timeout}ms) to {Ip}:{Port}. Please check LAN cable and IP address.",
                timeoutMs, _config.IpAddress, port);
            throw new LightControllerException(
                $"Connection timeout to Rsee light controller at {_config.IpAddress}:{port}. Check LAN cable, IP settings, and power.");
        }
        catch (Exception ex)
        {
            // Lỗi kết nối còn lại được ghi log và bọc thành LightControllerException cho UI.
            Status = ConnectionStatus.Error;
            _logger.LogError(ex, "[Rsee PW-D-24W20-8TE] Failed to connect to {Ip}:{Port}: {Message}",
                _config.IpAddress, port, ex.Message);
            throw new LightControllerException(
                $"Failed to connect to Rsee light controller at {_config.IpAddress}:{port}: {ex.Message}", ex);
        }
        finally
        {
            // Luôn trả khóa kể cả lúc lỗi để các thao tác sau không bị chờ mãi.
            _lock.Release();
        }
    }

    /// <summary>
    /// DISCONNECT: đóng kết nối mạng và cập nhật trạng thái; không gửi lệnh tắt đèn.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _logger.LogInformation("[Rsee PW-D-24W20-8TE] Disconnecting...");

        // Đợi lượt gửi-nhận đang dùng socket hoàn tất trước khi đóng.
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
    /// Đặt cường độ một kênh: kiểm tra số kênh, giới hạn giá trị 0..255 rồi gửi lệnh.
    /// </summary>
    public async Task SetChannelAsync(int channel, int intensity, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        // Giá trị âm thành 0, lớn hơn 255 thành 255.
        intensity = Math.Clamp(intensity, 0, 255);

        // Cache được cập nhật trước khi gửi; nếu gửi lỗi thì giá trị này vẫn đã đổi.
        _channelIntensities[channel] = intensity;
        // Code chỉ đặt cờ ON khi >0; nếu đặt 0 tại hàm này thì cờ ON/OFF cũ được giữ nguyên.
        if (intensity > 0)
        {
            _channelStates[channel] = true;
        }

        // Chuyển yêu cầu thành định dạng lệnh theo Protocol rồi gửi tới controller.
        await SendIntensityCommandAsync(channel, intensity, cancellationToken);
    }

    /// <summary>
    /// Bật kênh bằng cường độ đang lưu; nếu đang lưu 0 thì dùng DefaultIntensity hoặc 100.
    /// </summary>
    public async Task TurnOnAsync(int channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        // Lấy độ sáng đã lưu, không đọc độ sáng thực từ controller.
        int intensity = _channelIntensities[channel];
        if (intensity <= 0)
        {
            intensity = _config.DefaultIntensity > 0 ? _config.DefaultIntensity : 100;
            _channelIntensities[channel] = intensity;
        }

        // Đây là cờ yêu cầu trong C#; dữ liệu phản hồi chưa được phân tích để xác nhận bật thành công.
        _channelStates[channel] = true;
        await SendTurnOnCommandAsync(channel, intensity, cancellationToken);
    }

    /// <summary>
    /// Tắt một kênh qua lệnh OFF/đặt cường độ 0; giữ cường độ trong cache cho lần bật sau.
    /// </summary>
    public async Task TurnOffAsync(int channel, CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        _channelStates[channel] = false;

        // Không xóa _channelIntensities[channel], để TurnOnAsync dùng lại mức sáng trước đó.
        await SendTurnOffCommandAsync(channel, cancellationToken);
    }

    /// <summary>
    /// Gửi lệnh tắt các kênh: WORDOP ghép một chuỗi; các định dạng khác gửi lần lượt từng kênh.
    /// Việc ghép lệnh trong một lần gửi không bảo đảm thiết bị tắt mọi kênh đúng cùng thời điểm.
    /// </summary>
    public async Task TurnOffAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Rsee PW-D-24W20-8TE] Turning off all channels (CH1-CH8)...");

        // Cập nhật cờ trong bộ nhớ trước khi gửi; vẫn giữ cường độ đã lưu của từng kênh.
        for (int ch = 1; ch <= ChannelCount; ch++)
        {
            _channelStates[ch] = false;
        }

        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();
        if (protocol.Contains("WORDOP"))
        {
            // CH1..CH8 tương ứng A..H: ghép SA0000#SB0000#...SH0000# trong cùng một lần gửi.
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
            // OPT/HEX và các nhánh khác dùng lệnh OFF của từng kênh, chờ gửi xong rồi chuyển kênh.
            for (int ch = 1; ch <= ChannelCount; ch++)
            {
                await TurnOffAsync(ch, cancellationToken);
            }
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] All channels turned OFF.");
    }

    /// <summary>
    /// Trả cường độ lưu trong ứng dụng, không gửi truy vấn tới controller.
    /// </summary>
    public int GetChannelIntensity(int channel)
    {
        ValidateChannel(channel);
        return _channelIntensities[channel];
    }

    /// <summary>
    /// Trả cờ ON/OFF trong cache C#, không xác nhận trạng thái đèn ngoài đời.
    /// </summary>
    public bool IsChannelOn(int channel)
    {
        ValidateChannel(channel);
        return _channelStates[channel];
    }

    // TẠO LỆNH: hàm công khai phía trên chỉ yêu cầu bật/tắt/đặt sáng.
    // Các hàm phía dưới đổi yêu cầu đó thành đúng chuỗi ASCII hoặc gói byte mà code triển khai.

    /// <summary>Tạo lệnh đặt cường độ; Protocol rỗng hoặc không khớp OPT/HEX đi vào nhánh WORDOP.</summary>
    private async Task SendIntensityCommandAsync(int channel, int intensity, CancellationToken cancellationToken)
    {
        // Viết hoa để so sánh không phụ thuộc người dùng nhập ascii_wordop hay ASCII_WORDOP.
        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();

        if (protocol.Contains("OPT"))
        {
            // OPT: $3 + số kênh + cường độ 4 chữ số + #. CH1=255 -> $310255#.
            // D4 thêm số 0 ở đầu, ví dụ 100 -> 0100.
            string command = $"$3{channel}{intensity:D4}#";
            await SendRawAsciiInternalAsync(command, cancellationToken);
        }
        else if (protocol.Contains("HEX"))
        {
            // HEX là byte nhị phân, không gửi chuỗi ký tự "AA01...".
            // 0xAA mở gói, 0x01 là mã đặt sáng, 0x55 kết thúc gói theo định dạng của code.
            // Checksum là tổng bốn byte đầu, giữ 8 bit thấp bằng &0xFF.
            byte checksum = (byte)((0xAA + 0x01 + channel + intensity) & 0xFF);
            byte[] packet = [0xAA, 0x01, (byte)channel, (byte)intensity, checksum, 0x55];
            await SendRawBytesInternalAsync(packet, cancellationToken);
        }
        else
        {
            // WORDOP: CH1 -> A, CH2 -> B, ..., CH8 -> H; CH1=100 tạo SA0100#.
            char chLetter = (char)('A' + channel - 1);
            string command = $"S{chLetter}{intensity:D4}#";
            await SendRawAsciiInternalAsync(command, cancellationToken);
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] CH{Channel} set to intensity {Intensity}/255", channel, intensity);
    }

    /// <summary>Tạo lệnh bật kênh, kèm cường độ được lưu trong ứng dụng.</summary>
    private async Task SendTurnOnCommandAsync(int channel, int intensity, CancellationToken cancellationToken)
    {
        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();

        if (protocol.Contains("OPT"))
        {
            // OPT: gửi lệnh đặt sáng trước rồi lệnh ON, ví dụ CH1=100 -> $310100#$11#.
            string setCmd = $"$3{channel}{intensity:D4}#";
            string onCmd = $"$1{channel}#";
            await SendRawAsciiInternalAsync(setCmd + onCmd, cancellationToken);
        }
        else if (protocol.Contains("HEX"))
        {
            // HEX: bật bằng cùng gói đặt cường độ với mã 0x01, không thêm chuỗi ON.
            byte checksum = (byte)((0xAA + 0x01 + channel + intensity) & 0xFF);
            byte[] packet = [0xAA, 0x01, (byte)channel, (byte)intensity, checksum, 0x55];
            await SendRawBytesInternalAsync(packet, cancellationToken);
        }
        else
        {
            // WORDOP: code luôn ghép đặt sáng và ON, ví dụ CH1=100 -> SA0100#SH1#.
            char chLetter = (char)('A' + channel - 1);
            string setCmd = $"S{chLetter}{intensity:D4}#";
            string onCmd = $"SH{channel}#";
            await SendRawAsciiInternalAsync(setCmd + onCmd, cancellationToken);
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] CH{Channel} turned ON (Intensity: {Intensity})", channel, intensity);
    }

    /// <summary>Tạo lệnh tắt: OPT dùng $2, HEX dùng mã 0x02, WORDOP ghép cường độ 0 và SL.</summary>
    private async Task SendTurnOffCommandAsync(int channel, CancellationToken cancellationToken)
    {
        string protocol = string.IsNullOrWhiteSpace(_config.Protocol) ? "ASCII_WORDOP" : _config.Protocol.ToUpperInvariant();

        if (protocol.Contains("OPT"))
        {
            // CH1 -> $21#, CH2 -> $22#, ...
            string offCmd = $"$2{channel}#";
            await SendRawAsciiInternalAsync(offCmd, cancellationToken);
        }
        else if (protocol.Contains("HEX"))
        {
            // HEX: mã OFF 0x02, cường độ byte 0x00; checksum đổi theo mã lệnh này.
            byte checksum = (byte)((0xAA + 0x02 + channel + 0) & 0xFF);
            byte[] packet = [0xAA, 0x02, (byte)channel, 0x00, checksum, 0x55];
            await SendRawBytesInternalAsync(packet, cancellationToken);
        }
        else
        {
            // WORDOP: CH1 -> SA0000#SL1#, tức gửi cả lệnh mức sáng 0 và lệnh OFF.
            char chLetter = (char)('A' + channel - 1);
            string offCmd = $"S{chLetter}0000#SL{channel}#";
            await SendRawAsciiInternalAsync(offCmd, cancellationToken);
        }

        _logger.LogInformation("[Rsee PW-D-24W20-8TE] CH{Channel} turned OFF", channel);
    }

    /// <summary>
    /// Gửi chuỗi ASCII nhập tay để kiểm tra giao tiếp, trả chuỗi phản hồi đọc được nếu có.
    /// Khóa gửi-nhận nằm ở SendAndReceiveBytesAsync; hàm này không kiểm tra cú pháp lệnh.
    /// </summary>
    public async Task<string> SendRawAsciiAsync(string command, CancellationToken cancellationToken = default)
    {
        return await SendRawAsciiInternalAsync(command, cancellationToken);
    }

    // Chuỗi lệnh -> byte ASCII -> gửi TCP -> byte phản hồi -> chuỗi ASCII cho UI/log.
    private async Task<string> SendRawAsciiInternalAsync(string command, CancellationToken cancellationToken)
    {
        EnsureConnected();

        // Không tự thêm ký tự kết thúc: người tạo/nhập lệnh phải cung cấp # nếu định dạng cần nó.
        byte[] bytes = Encoding.ASCII.GetBytes(command);
        byte[] responseBytes = await SendAndReceiveBytesAsync(bytes, cancellationToken);
        string response = responseBytes.Length > 0 ? Encoding.ASCII.GetString(responseBytes) : string.Empty;

        // TX là dữ liệu gửi, RX là dữ liệu đọc được. RX rỗng nếu chưa có byte sẵn khi kiểm tra.
        _logger.LogDebug("[Rsee PW-D-24W20-8TE] TX: '{Command}' | RX: '{Response}'", command, response);
        return response;
    }

    // Dùng cho gói nhị phân; log đổi byte thành chuỗi HEX để người đọc xem được.
    private async Task<byte[]> SendRawBytesInternalAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        EnsureConnected();
        byte[] response = await SendAndReceiveBytesAsync(bytes, cancellationToken);
        _logger.LogDebug("[Rsee PW-D-24W20-8TE] TX Hex: {TxHex} | RX Hex: {RxHex}",
            Convert.ToHexString(bytes), Convert.ToHexString(response));
        return response;
    }

    /// <summary>
    /// Một lượt gửi TCP và đọc phần dữ liệu đang có sẵn; chưa ghép/kiểm tra một phản hồi protocol đầy đủ.
    /// Hàm không chờ ACK với CommandTimeoutMs và không xác nhận thiết bị đã bật/tắt đèn.
    /// </summary>
    private async Task<byte[]> SendAndReceiveBytesAsync(byte[] data, CancellationToken cancellationToken)
    {
        // Giữ khóa để các lượt gửi-nhận không chạy đan xen trên cùng stream.
        // Phản hồi đến muộn vẫn có thể được lượt sau đọc; code chưa đối chiếu RX với lệnh đã gửi.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Kiểm tra lại trong khóa, vì socket có thể đã bị đóng sau lần EnsureConnected bên ngoài.
            if (_stream == null || _tcpClient == null || !_tcpClient.Connected)
            {
                Status = ConnectionStatus.Error;
                throw new LightControllerException("Light controller is not connected to LAN socket.");
            }

            // Ghi dữ liệu ra stream; Write/Flush hoàn tất chưa có nghĩa controller đã thực thi lệnh.
            await _stream.WriteAsync(data, cancellationToken);
            await _stream.FlushAsync(cancellationToken);

            // DataAvailable chỉ xem đã có byte sẵn hay chưa; không đợi phản hồi đến muộn.
            // Nếu có, đọc một lần tối đa 256 byte; TCP có thể chia/ghép dữ liệu nhiều lệnh.
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

            // Không có dữ liệu sẵn thì trả mảng rỗng, không coi đó là lỗi timeout hay ACK thất bại.
            return responseList.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Lỗi I/O được đổi thành lỗi controller; yêu cầu hủy từ token được truyền lên người gọi.
            Status = ConnectionStatus.Error;
            _logger.LogError(ex, "[Rsee PW-D-24W20-8TE] Socket error during communication: {Message}", ex.Message);
            throw new LightControllerException($"Light controller communication error: {ex.Message}", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Kiểm tra điều kiện trước khi gửi; TcpClient.Connected không phải phép kiểm tra sống tức thời.
    private void EnsureConnected()
    {
        if (!IsConnected || _stream == null || _tcpClient == null || !_tcpClient.Connected)
        {
            Status = ConnectionStatus.Error;
            throw new LightControllerException(
                $"Rsee PW-D-24W20-8TE is not connected. Call ConnectAsync first. Target: {_config.IpAddress}:{_config.Port}");
        }
    }

    // Chặn số kênh ngoài 1..ChannelCount; cấu hình ChannelCount không nên vượt 8 của driver này.
    private void ValidateChannel(int channel)
    {
        if (channel < 1 || channel > ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel),
                $"Channel must be between 1 and {ChannelCount}. Received: {channel}");
        }
    }

    /// <summary>Đóng/giải phóng stream và client, đặt về null; không gửi lệnh OFF cho đèn.</summary>
    private void CleanupSocket()
    {
        // Tiếp tục dọn phần còn lại dù Close/Dispose của một tài nguyên phát sinh lỗi.
        try { _stream?.Close(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _stream = null;

        try { _tcpClient?.Close(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _tcpClient = null;
    }

    /// <summary>Giải phóng service khi ứng dụng đóng: dọn socket, khóa và trạng thái kết nối.</summary>
    public void Dispose()
    {
        // Đảm bảo việc giải phóng chỉ chạy một lần.
        if (_disposed) return;
        _disposed = true;

        CleanupSocket();
        _lock.Dispose();
        Status = ConnectionStatus.Disconnected;
    }
}
