using System.Diagnostics;
using Basler.Pylon;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Exceptions;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Core.Models;
using Microsoft.Extensions.Logging;
using PylonCamera = Basler.Pylon.Camera; // Đặt tên khác để tránh trùng namespace IndustrialVision.Camera.

namespace IndustrialVision.Camera;

/// <summary>
/// Điều khiển camera Basler thật qua SDK pylon, hỗ trợ kết nối GigE và USB3.
/// DISCOVER: tìm camera. CONNECT: mở camera và áp dụng cấu hình.
/// LIVE: nhận ảnh liên tục rồi phát FrameReceived để ViewModel hiển thị ảnh mới nhất.
/// CAPTURE: lấy một ảnh để ViewModel chạy chu trình kiểm tra sản phẩm.
/// PLC, đèn và việc đánh giá OK/NG được điều phối bên ngoài class này.
/// </summary>
public sealed class BaslerCameraService : ICameraService
{
    // Cấu hình được truyền từ Config/camera.json; logger ghi thông tin/lỗi vào log ứng dụng.
    private readonly CameraConfiguration _config;
    private readonly ILogger<BaslerCameraService> _logger;

    // Chỉ cho một thao tác điều khiển chạy mỗi lần, ví dụ CONNECT không chạy cùng CAPTURE.
    private readonly SemaphoreSlim _operations = new(1, 1);
    // Worker LIVE chạy riêng: khóa này tránh đọc ảnh cùng lúc với thay đổi tham số camera.
    private readonly object _sdkLock = new();

    // Đối tượng SDK giữ kết nối camera và bộ đổi ảnh thô sang Mono8/RGB8 cho ứng dụng.
    private PylonCamera? _camera;
    private PixelDataConverter? _converter;

    // CTS dùng để yêu cầu dừng LIVE; Task là công việc nhận ảnh ở luồng nền.
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;
    // Chỉ dùng khi preview camera line scan: lưu tham số gốc để phục hồi trước khi chụp.
    private long? _liveHeightRestore;
    private double? _liveExposureRestore;

    // disposed: service đã được giải phóng; volatile: cờ LIVE được đọc từ nhiều luồng.
    private bool _disposed;
    private volatile bool _isLiveActive;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;

    /// <summary>Trạng thái kết nối; phát sự kiện khi thay đổi để giao diện cập nhật.</summary>
    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            // ViewModel nhận sự kiện này để cập nhật trạng thái/nút điều khiển.
            StatusChanged?.Invoke(this, value);
        }
    }

    // IsConnected là trạng thái service; RequireCamera còn kiểm tra đối tượng SDK đã mở.
    public bool IsConnected => Status is ConnectionStatus.Connected or ConnectionStatus.Ready;
    public bool IsLiveActive => _isLiveActive;
    public event EventHandler<ConnectionStatus>? StatusChanged;
    // Gửi ImageFrame cho bên đăng ký sự kiện; driver không tự vẽ lên màn hình WPF.
    public event EventHandler<ImageFrame>? FrameReceived;

    /// <summary>Nhận cấu hình và logger; chưa mở thiết bị ở thời điểm tạo service.</summary>
    public BaslerCameraService(CameraConfiguration config, ILogger<BaslerCameraService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Chỉ gọi SDK khi quét/kết nối: giao diện vẫn mở được nếu runtime pylon có vấn đề.
    }

    /// <summary>
    /// Lớp bọc chung cho các thao tác camera: xếp lần lượt, chạy nền, chuẩn hóa thông báo lỗi.
    /// T là kiểu kết quả trả về, ví dụ danh sách camera hoặc ImageFrame.
    /// </summary>
    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken token = default)
    {
        // Chờ thao tác trước xong; token cho phép người gọi hủy lúc đang chờ.
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Các lệnh SDK có thể chờ thiết bị; chạy ở luồng nền để không giữ luồng giao diện.
            return await Task.Run(action, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsSdkLoadFailure(ex))
        {
            // Đây là lỗi nạp DLL/runtime, khác với lỗi IP hoặc camera đang bị ứng dụng khác mở.
            Status = ConnectionStatus.Error;
            throw new CameraException("Cannot load Basler pylon runtime. Install matching pylon 11 x64 runtime and GigE/USB3 drivers; check its Runtime/x64 DLLs and PATH.", ex);
        }
        catch (Exception ex) when (ex is not (CameraException or OperationCanceledException or ObjectDisposedException))
        {
            // Đưa lỗi SDK về CameraException để phần giao diện xử lý thống nhất.
            throw new CameraException($"Basler pylon: {ex.Message}", ex);
        }
        // Dù thành công, lỗi hay hủy cũng phải trả khóa cho thao tác tiếp theo.
        finally { _operations.Release(); }
    }

    // Dùng cùng lớp bọc cho thao tác không có kết quả như CONNECT hoặc STOP LIVE.
    private Task RunAsync(Action action, CancellationToken token = default)
        => RunAsync(() => { action(); return true; }, token);

    // Một lỗi nạp DLL có thể nằm bên trong InnerException; kiểm tra cả chuỗi lỗi.
    private static bool IsSdkLoadFailure(Exception ex)
        => ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException
            || (ex.InnerException != null && IsSdkLoadFailure(ex.InnerException));

    // Các model/transport có thể thiếu IP hoặc MAC; thiếu key thì trả chuỗi rỗng.
    private static string InfoValue(ICameraInfo info, string key)
        => info.ContainsKey(key) ? info[key] ?? string.Empty : string.Empty;

    /// <summary>Quét qua pylon; giữ thông tin cho UI và thông tin SDK để mở đúng thiết bị.</summary>
    private static List<(CameraDeviceInfo Info, ICameraInfo Device)> Enumerate()
    {
        var devices = new List<(CameraDeviceInfo, ICameraInfo)>();
        foreach (var device in CameraFinder.Enumerate())
        {
            string deviceType = InfoValue(device, CameraInfoKey.DeviceType);
            string connection = deviceType switch
            {
                "BaslerGigE" => "GigE",
                "BaslerUsb" => "USB3",
                _ => deviceType
            };
            // Chỉ đưa camera GigE/USB3 thật lên UI; bỏ camera emulation và transport khác.
            if (connection is not ("GigE" or "USB3")) continue;
            // CameraDeviceInfo là model chung của app, còn device là thông tin riêng của SDK.
            devices.Add((new CameraDeviceInfo
            {
                Name = InfoValue(device, CameraInfoKey.FriendlyName),
                SerialNumber = InfoValue(device, CameraInfoKey.SerialNumber),
                ModelName = InfoValue(device, CameraInfoKey.ModelName),
                Manufacturer = InfoValue(device, CameraInfoKey.VendorName),
                InterfaceType = connection,
                IpAddress = InfoValue(device, CameraInfoKey.DeviceIpAddress),
                MacAddress = InfoValue(device, CameraInfoKey.DeviceMacAddress)
            }, device));
        }
        return devices;
    }

    /// <summary>Nút DISCOVER: trả danh sách camera, chưa Open và chưa lấy ảnh.</summary>
    public Task<IReadOnlyList<CameraDeviceInfo>> DiscoverCamerasAsync(CancellationToken cancellationToken = default)
        => RunAsync<IReadOnlyList<CameraDeviceInfo>>(() =>
        {
            var cameras = Enumerate().Select(d => d.Info).ToArray();
            _logger.LogInformation("Discovered {Count} Basler camera(s).", cameras.Length);
            return cameras;
        }, cancellationToken);

    /// <summary>Nút CONNECT: tìm đúng camera, mở quyền điều khiển và áp dụng tham số.</summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        // 1. Dọn phiên cũ trước khi tạo kết nối mới.
        StopLive();
        CloseCamera();
        Status = ConnectionStatus.Connecting;
        try
        {
            // 2. Quét lại và ưu tiên serial; nếu không có serial thì lọc theo IP.
            // Không dùng vị trí trong danh sách vì thứ tự thiết bị có thể thay đổi sau lần quét.
            var matches = Enumerate().Where(d =>
                (!string.IsNullOrWhiteSpace(_config.SerialNumber)
                    ? d.Info.SerialNumber == _config.SerialNumber.Trim()
                    : string.IsNullOrWhiteSpace(_config.IpAddress) || d.Info.IpAddress == _config.IpAddress.Trim()) &&
                (string.IsNullOrWhiteSpace(_config.ConnectionType) ||
                    d.Info.InterfaceType.Equals(_config.ConnectionType, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count != 1)
                throw new CameraException(matches.Count == 0
                    ? "Selected Basler camera was not found. Check pylon Viewer, cable and IP/subnet."
                    : "Multiple Basler cameras found. Discover and select a camera before connecting.");

            cancellationToken.ThrowIfCancellationRequested();
            var selected = matches[0];
            // 3. Tạo camera SDK. Khi mở, đặt AcquisitionMode liên tục làm nền cho LIVE/capture.
            _camera = new PylonCamera(selected.Device);
            _camera.CameraOpened += (sender, args) => Configuration.AcquireContinuous(sender!, args);
            _camera.ConnectionLost += OnConnectionLost;
            // Open lấy quyền điều khiển: có thể lỗi nếu pylon Viewer/Cognex/app khác đang giữ camera.
            _camera.Open();

            // 4. Chuẩn bị bộ chuyển đổi pixel; padding=0 để mỗi dòng ảnh không có byte đệm.
            _converter = new PixelDataConverter();
            _converter.Parameters[PLPixelDataConverter.OutputPaddingX].SetValue(0);
            // Width/Height=0 hoặc PixelFormat rỗng: giữ giá trị camera đang dùng.
            if (_config.Width > 0) SetNumber(_config.Width, "Width");
            if (_config.Height > 0) SetNumber(_config.Height, "Height");
            if (!string.IsNullOrWhiteSpace(_config.PixelFormat)) SetEnum("PixelFormat", _config.PixelFormat);
            // 5. Tắt tự động để Exposure/Gain đúng theo giá trị nhập trên giao diện.
            DisableAuto("ExposureAuto");
            SetNumber(_config.Exposure, "ExposureTime", "ExposureTimeAbs");
            DisableAuto("GainAuto");
            double initialGain = _config.Gain;
            if (initialGain == 0 && !_camera.Parameters["Gain"].IsWritable && !_camera.Parameters["GainAbs"].IsWritable &&
                _camera.Parameters["GainRaw"] is IIntegerParameter rawGain && rawGain.IsWritable)
                initialGain = rawGain.GetMinimum(); // Model GigE cũ có thể có GainRaw tối thiểu lớn hơn 0.
            SetNumber(initialGain, "Gain", "GainAbs", "GainRaw");
            _config.Gain = initialGain;
            // 6. Chỉ đặt giới hạn FPS nếu cấu hình >0; 0 giữ nguyên tham số FPS của camera.
            if (_config.FrameRate > 0)
            {
                var enabled = _camera.Parameters["AcquisitionFrameRateEnable"] as IBooleanParameter;
                if (enabled?.IsWritable == true) enabled.SetValue(true);
                SetNumber(_config.FrameRate, "AcquisitionFrameRate", "AcquisitionFrameRateAbs");
            }
            // 7. Áp dụng trigger cho chụp ảnh; khi nhấn LIVE sẽ tạm tắt trigger này.
            ConfigureTrigger();
            cancellationToken.ThrowIfCancellationRequested();
            // 8. Ghi lại danh tính thiết bị thực tế và báo sẵn sàng cho giao diện.
            _config.Name = selected.Info.Name;
            _config.SerialNumber = selected.Info.SerialNumber;
            _config.IpAddress = selected.Info.IpAddress;
            _config.ConnectionType = selected.Info.InterfaceType;
            Status = ConnectionStatus.Ready;
            _logger.LogInformation("Connected to {Camera}.", selected.Info);
        }
        catch
        {
            // Mở/cấu hình thất bại thì giải phóng thiết bị, tránh giữ camera ở phiên lỗi.
            try { CloseCamera(); }
            finally { Status = ConnectionStatus.Error; }
            throw;
        }
    }, cancellationToken);

    /// <summary>Sự kiện SDK báo mất kết nối: báo lỗi và yêu cầu worker LIVE dừng.</summary>
    private void OnConnectionLost(object? sender, EventArgs args)
    {
        _logger.LogError("Basler camera connection was lost.");
        Status = ConnectionStatus.Error;
        try { _liveCts?.Cancel(); }
        catch (ObjectDisposedException) { } // CTS có thể đã được giải phóng khi LIVE đang dừng.
    }

    // Kiểm tra trước khi đọc/ghi camera để không thao tác trên kết nối chưa mở.
    private void RequireCamera()
    {
        if (_camera == null || !IsConnected || !_camera.IsOpen)
            throw new CameraException("Basler camera is not connected.");
    }

    // Ghi tham số kiểu lựa chọn, ví dụ TriggerMode="On" hoặc TriggerSource="Software".
    private void SetEnum(string name, string value)
    {
        if (_camera!.Parameters[name] is not IEnumParameter parameter || !parameter.IsWritable)
            throw new CameraException($"Camera parameter {name} is unavailable or read-only.");
        parameter.SetValue(value);
    }

    // Tắt chế độ tự động nếu model có hỗ trợ; tham số không có thì bỏ qua.
    private void DisableAuto(string name)
    {
        if (_camera!.Parameters[name] is IEnumParameter parameter && parameter.IsWritable)
            parameter.SetValue("Off");
    }

    /// <summary>
    /// Ghi tham số số sau khi kiểm tra giới hạn. Thử các tên tương đương giữa model cũ/mới.
    /// Ví dụ ExposureTime/ExposureTimeAbs cùng biểu diễn thời gian phơi sáng.
    /// </summary>
    private void SetNumber(double value, params string[] names)
    {
        if (!double.IsFinite(value) || value < 0) throw new CameraException($"Invalid {names[0]}: {value}.");
        foreach (string name in names)
        {
            var parameter = _camera!.Parameters[name];
            if (!parameter.IsWritable) continue;
            if (parameter is IFloatParameter floating)
            {
                // Tham số số thực, ví dụ exposure: phải nằm trong min/max camera cho phép.
                if (value < floating.GetMinimum() || value > floating.GetMaximum())
                    throw new CameraException($"{name} must be between {floating.GetMinimum()} and {floating.GetMaximum()}.");
                floating.SetValue(value);
                return;
            }
            if (parameter is IIntegerParameter integer)
            {
                // Tham số số nguyên, ví dụ Height/GainRaw: còn phải đúng bước tăng increment.
                long min = integer.GetMinimum(), max = integer.GetMaximum(), step = integer.GetIncrement();
                if (value < min || value > max || value != Math.Truncate(value))
                    throw new CameraException($"{name} must be an integer between {min} and {max} (step {step}).");
                long raw = checked((long)value);
                if (step > 0 && (raw - min) % step != 0)
                    throw new CameraException($"{name} requires increments of {step} from {min}.");
                integer.SetValue(raw);
                return;
            }
        }
        throw new CameraException($"No writable camera parameter found: {string.Join(" / ", names)}.");
    }

    /// <summary>
    /// LIVE tắt trigger để phát ảnh liên tục; CAPTURE dùng trigger đã cấu hình, hiện là On/Software.
    /// Bit trigger PLC được đọc ở service PLC/ViewModel; hàm này cấu hình trigger bên trong camera.
    /// </summary>
    private void ConfigureTrigger(bool live = false)
    {
        // live=true luôn cho enabled=false, dù camera.json đang đặt TriggerMode="On".
        bool enabled = !live && _config.TriggerMode.Equals("On", StringComparison.OrdinalIgnoreCase);
        if (_camera!.Parameters["TriggerSelector"] is IEnumParameter selector && selector.IsWritable)
        {
            var values = selector.GetAllValues().ToArray();
            // Tắt các trigger từ bộ cấu hình cũ của camera, tránh còn một trigger khác chặn LIVE.
            foreach (string value in values)
            {
                if (!selector.CanSetValue(value)) continue;
                selector.SetValue(value);
                if (_camera.Parameters["TriggerMode"] is IEnumParameter mode && mode.IsWritable) mode.SetValue("Off");
            }
            // Ưu tiên trigger từng frame; một số model cũ chỉ hỗ trợ AcquisitionStart.
            string? frameTrigger = selector.CanSetValue("FrameStart") ? "FrameStart"
                : selector.CanSetValue("AcquisitionStart") ? "AcquisitionStart" : null;
            if (frameTrigger == null && enabled) throw new CameraException("Camera has no supported frame trigger selector.");
            if (frameTrigger != null) selector.SetValue(frameTrigger);
        }
        SetEnum("TriggerMode", enabled ? "On" : "Off");
        // Chỉ chọn nguồn trigger khi bật; Software nghĩa là C# sẽ gọi ExecuteSoftwareTrigger.
        if (enabled) SetEnum("TriggerSource", _config.TriggerSource);
    }

    /// <summary>Áp dụng thời gian phơi sáng, đơn vị microsecond (µs).</summary>
    public Task SetExposureAsync(double value) => RunAsync(() =>
    {
        RequireCamera();
        lock (_sdkLock) { DisableAuto("ExposureAuto"); SetNumber(value, "ExposureTime", "ExposureTimeAbs"); }
        // Nếu đã đổi exposure khi LIVE line scan, phục hồi về giá trị người dùng vừa nhập.
        if (_liveExposureRestore.HasValue) _liveExposureRestore = value;
        // Cập nhật model trong bộ nhớ; SAVE SETTINGS ở ViewModel mới ghi camera.json.
        _config.Exposure = value;
    });

    /// <summary>Áp dụng gain theo tên tham số mà model hỗ trợ; đơn vị tùy Gain/GainAbs/GainRaw.</summary>
    public Task SetGainAsync(double value) => RunAsync(() =>
    {
        RequireCamera();
        lock (_sdkLock) { DisableAuto("GainAuto"); SetNumber(value, "Gain", "GainAbs", "GainRaw"); }
        _config.Gain = value;
    });

    /// <summary>
    /// Nút LIVE: bắt đầu nhận ảnh liên tục trên worker riêng, trả về sau khi khởi chạy.
    /// Driver lấy ảnh mới nhất; ViewModel nhận FrameReceived rồi cập nhật hình WPF.
    /// FPS thực tế phụ thuộc camera, exposure, truyền tải và tốc độ hiển thị của UI.
    /// </summary>
    public Task StartLiveAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        RequireCamera();
        if (_isLiveActive) return;
        // Dọn worker cũ đã kết thúc, rồi chuyển camera từ chờ trigger sang phát ảnh liên tục.
        StopLive();
        ConfigureTrigger(live: true);
        try
        {
            ConfigureLivePreview();
            // Chỉ giữ một frame mới nhất để ảnh cũ không tích lại gây chậm khi di chuyển vật.
            _camera!.Parameters[PLCameraInstance.OutputQueueSize].SetValue(1);
            // ProvidedByUser: chương trình tự gọi RetrieveResult trong vòng lặp phía dưới.
            _camera.StreamGrabber!.Start(GrabStrategy.LatestImages, GrabLoop.ProvidedByUser);
        }
        catch
        {
            // Khởi động LIVE thất bại thì phục hồi tham số chụp trước khi báo lỗi.
            RestoreLivePreview();
            ConfigureTrigger();
            throw;
        }
        // Có thể dừng bằng STOP LIVE hoặc bằng yêu cầu hủy từ người gọi.
        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _liveCts.Token;
        _isLiveActive = true;
        // RunAsync chỉ bảo vệ lúc khởi chạy; worker này tiếp tục chạy nền sau khi hàm trả về.
        _liveTask = Task.Run(() =>
        {
            try
            {
                var interval = Stopwatch.StartNew();
                long received = 0;
                bool firstFrame = true;
                while (!token.IsCancellationRequested)
                {
                    ImageFrame? frame;
                    // Chờ tối đa 100ms mỗi lần để kiểm tra hủy thường xuyên; ảnh có sẵn trả ngay.
                    // Đây là timeout chờ ảnh, không phải đặt tốc độ LIVE thành 10 FPS.
                    lock (_sdkLock) frame = ReadFrame(100);
                    if (frame != null && !token.IsCancellationRequested)
                    {
                        if (firstFrame)
                        {
                            // Báo ảnh đầu tiên; Elapsed tính từ đầu khoảng thống kê hiện tại (reset mỗi 5 giây).
                            _logger.LogInformation("First live frame: {Width}x{Height}, received after {Elapsed} ms.",
                                frame.Width, frame.Height, interval.ElapsedMilliseconds);
                            firstFrame = false;
                        }
                        received++;
                        // Sự kiện phát từ worker nền; MainViewModel chịu trách nhiệm đưa ảnh lên UI.
                        FrameReceived?.Invoke(this, frame);
                    }
                    // Thống kê FPS nhận ở driver, không phải đợi 5 giây mới hiển thị một ảnh.
                    if (interval.Elapsed >= TimeSpan.FromSeconds(5))
                    {
                        if (received == 0)
                            _logger.LogWarning("No live frames received in 5 seconds. Check line rate, exposure, trigger and GigE transport.");
                        else
                            _logger.LogInformation("Live acquisition: {Fps:F1} frames/s (UI keeps the newest frame).",
                                received / interval.Elapsed.TotalSeconds);
                        received = 0;
                        interval.Restart();
                    }
                }
            }
            catch (Exception ex)
            {
                // Lỗi grab/chuyển ảnh: kết thúc worker và chuyển trạng thái camera sang Error.
                _logger.LogError(ex, "Basler live acquisition failed.");
                Status = ConnectionStatus.Error;
            }
            finally
            {
                // Dừng grab, phục hồi tham số tạm; chỉ khôi phục trigger nếu trạng thái kết nối hợp lệ.
                _isLiveActive = false;
                try
                {
                    lock (_sdkLock)
                    {
                        if (_camera!.StreamGrabber.IsGrabbing) _camera.StreamGrabber.Stop();
                        if (_camera.IsOpen)
                        {
                            RestoreLivePreview();
                            if (IsConnected) ConfigureTrigger();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stop Basler live acquisition or restore trigger.");
                    Status = ConnectionStatus.Error;
                }
            }
        });
    }, cancellationToken);

    /// <summary>
    /// Nhánh tương thích camera line scan (raL): giảm số dòng/exposure để preview ra ảnh sớm hơn.
    /// Camera area scan acA1600 đang dùng bỏ qua nhánh này, giữ kích thước và exposure hiện tại.
    /// </summary>
    private void ConfigureLivePreview()
    {
        // Nhận diện kiểu camera theo model hoặc tham số DeviceScanType của SDK.
        string model = _camera!.CameraInfo is { } info ? InfoValue(info, CameraInfoKey.ModelName) : string.Empty;
        bool lineScan = model.StartsWith("raL", StringComparison.OrdinalIgnoreCase)
            || (_camera.Parameters["DeviceScanType"] is IEnumParameter scanType
                && scanType.IsReadable && scanType.GetValue() == "Linescan");
        if (!lineScan) return;

        // Camera line scan phải tích đủ Height dòng mới ra một frame: giảm Height giúp preview nhanh hơn.
        if (_config.LiveHeight > 0 && _camera.Parameters["Height"] is IIntegerParameter height && height.IsWritable)
        {
            long original = height.GetValue();
            long minimum = height.GetMinimum();
            long increment = Math.Max(1, height.GetIncrement());
            long target = Math.Clamp((long)_config.LiveHeight, minimum, original);
            // Căn số dòng theo bước increment hợp lệ; lưu giá trị gốc để CAPTURE dùng lại.
            target = minimum + (target - minimum) / increment * increment;
            _liveHeightRestore = original;
            height.SetValue(target);
            _logger.LogInformation("Line scan LIVE height: {Height} lines (capture height {Original}).", target, original);
        }

        if (_config.LiveExposure > 0)
        {
            // Chỉ giảm exposure LIVE nếu model hỗ trợ; không ghi đè exposure chụp trong _config.
            foreach (string name in new[] { "ExposureTime", "ExposureTimeAbs" })
            {
                if (_camera.Parameters[name] is not IFloatParameter exposure || !exposure.IsWritable) continue;
                double original = exposure.GetValue();
                double target = Math.Clamp(_config.LiveExposure, exposure.GetMinimum(), original);
                _liveExposureRestore = original;
                exposure.SetValue(target);
                _logger.LogInformation("Line scan LIVE exposure: {Exposure} us (capture exposure {Original} us).", target, original);
                break;
            }
        }
    }

    // Phục hồi tham số line scan đã đổi tạm cho LIVE; area scan không có giá trị cần phục hồi.
    private void RestoreLivePreview()
    {
        if (_liveHeightRestore is long height)
        {
            SetNumber(height, "Height");
            _liveHeightRestore = null;
        }
        if (_liveExposureRestore is double exposure)
        {
            SetNumber(exposure, "ExposureTime", "ExposureTimeAbs");
            _liveExposureRestore = null;
        }
    }

    /// <summary>Yêu cầu worker dừng và chờ nó dọn SDK xong trước khi chụp/đóng camera.</summary>
    private void StopLive()
    {
        if (_liveTask == null) return;
        _liveCts!.Cancel();
        // Không giữ _sdkLock khi chờ: worker cần khóa đó để hoàn tất phần finally của nó.
        // Mỗi lần chờ RetrieveResult tối đa 100ms; thời gian dừng còn gồm chuyển ảnh và dọn SDK.
        _liveTask.GetAwaiter().GetResult();
        _liveTask = null;
        var cts = _liveCts;
        _liveCts = null;
        cts.Dispose();
        _isLiveActive = false;
    }

    /// <summary>Nút STOP LIVE: dừng worker rồi đưa camera về trigger chụp đã cấu hình.</summary>
    public Task StopLiveAsync() => RunAsync(() =>
    {
        StopLive();
        if (_camera?.IsOpen == true && IsConnected) ConfigureTrigger();
    });

    /// <summary>
    /// Chụp một ảnh cho nút CAPTURE hoặc chu trình inspection của ViewModel.
    /// Hàm chỉ trả ảnh; việc bật/tắt đèn, OCR và ghi Result/Complete về PLC nằm ở ViewModel.
    /// </summary>
    public Task<ImageFrame> CaptureAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        RequireCamera();
        // 1. Dừng LIVE để worker preview không lấy mất ảnh dành cho chu trình PLC.
        StopLive();
        ConfigureTrigger();
        // 2. Chỉ phát lệnh chụp phần mềm khi cấu hình là On/Software.
        // Khi TriggerMode=On và nguồn là Line1, camera chờ tín hiệu điện từ bên ngoài.
        bool software = _config.TriggerMode.Equals("On", StringComparison.OrdinalIgnoreCase) &&
            _config.TriggerSource.Equals("Software", StringComparison.OrdinalIgnoreCase);
        int timeout = _config.TimeoutMs > 0 ? _config.TimeoutMs : 3000;
        // Cùng một hạn chờ cho cả bước camera sẵn sàng và bước nhận ảnh.
        var timer = Stopwatch.StartNew();
        // 3. Chuẩn bị lấy đúng một frame, không bỏ frame theo chiến lược LatestImages của LIVE.
        _camera!.StreamGrabber!.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByUser);
        try
        {
            if (software)
            {
                // 4. Nếu SDK/model hỗ trợ, chờ camera sẵn sàng nhận software trigger.
                if (_camera.CanWaitForFrameTriggerReady)
                {
                    bool ready = false;
                    while (!ready && timer.ElapsedMilliseconds < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // Chờ từng đoạn tối đa 100ms để vẫn kiểm tra được yêu cầu hủy.
                        ready = _camera.WaitForFrameTriggerReady((int)Math.Clamp(timeout - timer.ElapsedMilliseconds, 1, 100), TimeoutHandling.Return);
                    }
                    if (!ready) throw new CameraException($"Basler camera was not ready for a software trigger within {timeout}ms.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                // Lệnh C# gửi tới camera để chụp; khác với việc PLC bật bit D01040.0.
                _camera.ExecuteSoftwareTrigger();
            }
            // 5. Đọc đến khi có ảnh hoặc hết hạn; timeout một đoạn chưa có ảnh thì thử tiếp.
            while (timer.ElapsedMilliseconds < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = ReadFrame((int)Math.Clamp(timeout - timer.ElapsedMilliseconds, 1, 100));
                if (frame != null) return frame;
            }
            throw new CameraException($"No Basler frame received within {timeout}ms. Check trigger source and connection.");
        }
        // 6. Luôn dừng grab sau khi nhận ảnh, bị lỗi hoặc hủy, tránh giữ phiên chụp dang dở.
        finally { if (_camera.StreamGrabber.IsGrabbing) _camera.StreamGrabber.Stop(); }
    }, cancellationToken);

    /// <summary>Nhận một kết quả từ SDK và copy pixel sang ImageFrame chung của ứng dụng.</summary>
    private ImageFrame? ReadFrame(int timeoutMs)
    {
        // Return: hết đoạn chờ thì trả null. using: trả buffer về SDK khi ra khỏi hàm.
        using var result = _camera!.StreamGrabber!.RetrieveResult(timeoutMs, TimeoutHandling.Return);
        if (result == null)
        {
            // Chưa có ảnh khác với mất kết nối: chưa có ảnh thì LIVE/capture có thể thử tiếp.
            if (!_camera.IsConnected) throw new CameraException("Basler camera connection was lost.");
            return null;
        }
        if (!result.GrabSucceeded) throw new CameraException($"Basler grab failed ({result.ErrorCode}): {result.ErrorDescription}");
        // Ảnh Mono đổi sang một kênh 8-bit; ảnh màu/Bayer đổi sang ba kênh RGB 8-bit.
        bool mono = result.PixelTypeValue.ToString().StartsWith("Mono", StringComparison.OrdinalIgnoreCase);
        int channels = mono ? 1 : 3;
        // Cấp bộ nhớ pixel của app; checked báo lỗi nếu phép tính kích thước bị tràn số.
        var data = new byte[checked(result.Width * result.Height * channels)];
        _converter!.OutputPixelFormat = mono ? PixelType.Mono8 : PixelType.RGB8packed;
        // Copy trước khi using giải phóng result, để UI/OCR không đọc buffer SDK đã được tái sử dụng.
        _converter.Convert(data, result);
        // Timestamp là giờ PC nhận/chuyển frame, không phải timestamp phần cứng của camera.
        return new ImageFrame(data, result.Width, result.Height, channels, mono ? "Mono8" : "RGB8", DateTime.Now);
    }

    /// <summary>Đóng quyền điều khiển camera và giải phóng tài nguyên SDK/converter.</summary>
    private void CloseCamera()
    {
        var camera = _camera;
        _camera = null;
        _liveHeightRestore = null;
        _liveExposureRestore = null;
        try
        {
            if (camera != null)
            {
                // Bỏ handler để không xử lý sự kiện của camera cũ sau khi đóng.
                camera.ConnectionLost -= OnConnectionLost;
                try { if (camera.IsOpen) camera.Close(); }
                finally { camera.Dispose(); }
            }
        }
        // Dù đóng camera bị lỗi vẫn giải phóng bộ chuyển đổi pixel.
        finally { _converter?.Dispose(); _converter = null; }
    }

    /// <summary>Nút DISCONNECT: dừng LIVE, đóng camera, báo trạng thái Disconnected.</summary>
    public Task DisconnectAsync() => RunAsync(() =>
    {
        try { StopLive(); }
        finally
        {
            try { CloseCamera(); }
            finally { Status = ConnectionStatus.Disconnected; }
        }
    });

    /// <summary>DI gọi khi giải phóng service; dọn tài nguyên kể cả nếu người dùng chưa DISCONNECT.</summary>
    public void Dispose()
    {
        // Chờ thao tác đang chạy hoàn tất để không đóng SDK giữa chừng.
        _operations.Wait();
        try
        {
            if (_disposed) return; // Gọi Dispose nhiều lần vẫn chỉ dọn một lần.
            _disposed = true;
            try { StopLive(); }
            finally
            {
                try { CloseCamera(); }
                finally { Status = ConnectionStatus.Disconnected; }
            }
        }
        finally { _operations.Release(); }
    }
}
