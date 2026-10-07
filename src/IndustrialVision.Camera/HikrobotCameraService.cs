using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Exceptions;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Core.Models;
using Microsoft.Extensions.Logging;
using MvCamCtrl.NET;

namespace IndustrialVision.Camera;

/// <summary>Hikrobot GigE/USB3 camera integration using the same MVS SDK as TestLight_Cam.</summary>
public sealed class HikrobotCameraService : ICameraService
{
    private readonly CameraConfiguration _config;
    private readonly ILogger<HikrobotCameraService> _logger;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _sdkLock = new();
    private MyCamera? _camera;
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;
    private bool _initialized;
    private bool _disposed;
    private volatile bool _isLiveActive;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            StatusChanged?.Invoke(this, value);
        }
    }

    public bool IsConnected => Status is ConnectionStatus.Connected or ConnectionStatus.Ready;
    public bool IsLiveActive => _isLiveActive;
    public event EventHandler<ConnectionStatus>? StatusChanged;
    public event EventHandler<ImageFrame>? FrameReceived;

    public HikrobotCameraService(CameraConfiguration config, ILogger<HikrobotCameraService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken token = default)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(action, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Status = ConnectionStatus.Error;
            throw new CameraException("Cannot load Hikrobot MVS SDK. Install the x64 MVS runtime/driver used by TestLight_Cam.", ex);
        }
        finally { _operations.Release(); }
    }

    private Task RunAsync(Action action, CancellationToken token = default)
        => RunAsync(() => { action(); return true; }, token);

    private static void Check(int result, string operation)
    {
        if (result != 0)
            throw new CameraException($"Hikrobot {operation} failed (0x{unchecked((uint)result):X8}).");
    }

    private void Initialize()
    {
        if (_initialized) return;
        Check(MyCamera.MV_CC_Initialize_NET(), "initialize SDK");
        _initialized = true;
    }

    private List<(CameraDeviceInfo Info, MyCamera.MV_CC_DEVICE_INFO Device)> Enumerate()
    {
        Initialize();
        var list = new MyCamera.MV_CC_DEVICE_INFO_LIST();
        Check(MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref list), "enumerate cameras");
        var devices = new List<(CameraDeviceInfo, MyCamera.MV_CC_DEVICE_INFO)>();
        for (int i = 0; i < list.nDeviceNum; i++)
        {
            var device = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(list.pDeviceInfo[i]);
            var info = new CameraDeviceInfo();
            if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                var gigE = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(device.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                info.InterfaceType = "GigE";
                info.Name = gigE.chUserDefinedName?.Trim('\0') ?? "";
                info.ModelName = gigE.chModelName?.Trim('\0') ?? "";
                info.SerialNumber = gigE.chSerialNumber?.Trim('\0') ?? "";
                info.Manufacturer = gigE.chManufacturerName?.Trim('\0') ?? "";
                info.IpAddress = new IPAddress(BitConverter.GetBytes(gigE.nCurrentIp).Reverse().ToArray()).ToString();
            }
            else if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
            {
                var usb = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(device.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                info.InterfaceType = "USB3";
                info.Name = usb.chUserDefinedName?.Trim('\0') ?? "";
                info.ModelName = usb.chModelName?.Trim('\0') ?? "";
                info.SerialNumber = usb.chSerialNumber?.Trim('\0') ?? "";
                info.Manufacturer = usb.chManufacturerName?.Trim('\0') ?? "";
            }
            else continue;
            devices.Add((info, device));
        }
        return devices;
    }

    public Task<IReadOnlyList<CameraDeviceInfo>> DiscoverCamerasAsync(CancellationToken cancellationToken = default)
        => RunAsync<IReadOnlyList<CameraDeviceInfo>>(() =>
        {
            var devices = Enumerate().Select(d => d.Info).ToArray();
            _logger.LogInformation("Discovered {Count} Hikrobot camera(s).", devices.Length);
            return devices;
        }, cancellationToken);

    public Task ConnectAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        StopLive();
        CloseCamera();
        Status = ConnectionStatus.Connecting;
        try
        {
            var devices = Enumerate();
            var matches = devices.Where(d =>
                (!string.IsNullOrWhiteSpace(_config.SerialNumber)
                    ? d.Info.SerialNumber == _config.SerialNumber.Trim()
                    : string.IsNullOrWhiteSpace(_config.IpAddress) || d.Info.IpAddress == _config.IpAddress.Trim()) &&
                (string.IsNullOrWhiteSpace(_config.ConnectionType) ||
                    d.Info.InterfaceType.Equals(_config.ConnectionType, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count != 1)
                throw new CameraException(matches.Count == 0
                    ? "Selected camera was not found. Discover cameras and check the cable, IP/subnet and MVS driver."
                    : "Multiple cameras found. Discover and select a camera before connecting.");

            cancellationToken.ThrowIfCancellationRequested();
            var selected = matches[0];
            var device = selected.Device;
            _camera = new MyCamera();
            Check(_camera.MV_CC_CreateDevice_NET(ref device), "create device");
            Check(_camera.MV_CC_OpenDevice_NET(), "open device (check whether another application is using it)");
            if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                int packetSize = _camera.MV_CC_GetOptimalPacketSize_NET();
                if (packetSize > 0) Check(_camera.MV_CC_SetIntValue_NET("GevSCPSPacketSize", (uint)packetSize), "set packet size");
            }
            Check(_camera.MV_CC_SetEnumValueByString_NET("AcquisitionMode", "Continuous"), "set acquisition mode");
            if (_config.Width > 0) Check(_camera.MV_CC_SetIntValue_NET("Width", (uint)_config.Width), "set width");
            if (_config.Height > 0) Check(_camera.MV_CC_SetIntValue_NET("Height", (uint)_config.Height), "set height");
            if (!string.IsNullOrWhiteSpace(_config.PixelFormat))
                Check(_camera.MV_CC_SetEnumValueByString_NET("PixelFormat", _config.PixelFormat), "set pixel format");
            SetFloat("ExposureTime", _config.Exposure, "ExposureAuto");
            SetFloat("Gain", _config.Gain, "GainAuto");
            if (_config.FrameRate > 0)
            {
                Check(_camera.MV_CC_SetBoolValue_NET("AcquisitionFrameRateEnable", true), "enable frame rate");
                SetFloat("AcquisitionFrameRate", _config.FrameRate);
            }
            ConfigureTrigger();
            _config.SerialNumber = selected.Info.SerialNumber;
            _config.IpAddress = selected.Info.IpAddress;
            _config.ConnectionType = selected.Info.InterfaceType;
            Status = ConnectionStatus.Ready;
            _logger.LogInformation("Connected to {Camera}.", selected.Info);
        }
        catch
        {
            CloseCamera();
            Status = ConnectionStatus.Error;
            throw;
        }
    }, cancellationToken);

    private void RequireCamera()
    {
        if (_camera == null || !IsConnected) throw new CameraException("Camera is not connected.");
    }

    private void ConfigureTrigger(bool live = false)
    {
        bool enabled = !live && _config.TriggerMode.Equals("On", StringComparison.OrdinalIgnoreCase);
        Check(_camera!.MV_CC_SetEnumValueByString_NET("TriggerMode", enabled ? "On" : "Off"), "set trigger mode");
        if (enabled)
            Check(_camera.MV_CC_SetEnumValueByString_NET("TriggerSource", _config.TriggerSource), "set trigger source");
    }

    private void SetFloat(string feature, double value, string? autoFeature = null)
    {
        if (!double.IsFinite(value) || value < 0 || value > float.MaxValue)
            throw new CameraException($"Invalid {feature}: {value}.");
        if (autoFeature != null)
        {
            // Some models do not expose automatic exposure/gain.
            int autoResult = _camera!.MV_CC_SetEnumValueByString_NET(autoFeature, "Off");
            if (autoResult != 0) _logger.LogDebug("{Feature} is unavailable (0x{Code:X8}).", autoFeature, autoResult);
        }
        var range = new MyCamera.MVCC_FLOATVALUE();
        Check(_camera!.MV_CC_GetFloatValue_NET(feature, ref range), $"read {feature} range");
        if (value < range.fMin || value > range.fMax)
            throw new CameraException($"{feature} must be between {range.fMin} and {range.fMax}.");
        Check(_camera.MV_CC_SetFloatValue_NET(feature, (float)value), $"set {feature}");
    }

    public Task SetExposureAsync(double value) => RunAsync(() =>
    {
        RequireCamera();
        lock (_sdkLock) SetFloat("ExposureTime", value, "ExposureAuto");
        _config.Exposure = value;
    });

    public Task SetGainAsync(double value) => RunAsync(() =>
    {
        RequireCamera();
        lock (_sdkLock) SetFloat("Gain", value, "GainAuto");
        _config.Gain = value;
    });

    public Task StartLiveAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        RequireCamera();
        if (_isLiveActive) return;
        StopLive();
        ConfigureTrigger(live: true);
        try { Check(_camera!.MV_CC_StartGrabbing_NET(), "start live acquisition"); }
        catch { ConfigureTrigger(); throw; }
        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _liveCts.Token;
        _isLiveActive = true;
        _liveTask = Task.Run(() =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    ImageFrame? frame;
                    lock (_sdkLock) frame = ReadFrame(100);
                    if (frame != null && !token.IsCancellationRequested)
                        FrameReceived?.Invoke(this, frame);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera live acquisition failed.");
                Status = ConnectionStatus.Error;
            }
            finally
            {
                _isLiveActive = false;
                try
                {
                    lock (_sdkLock)
                    {
                        Check(_camera!.MV_CC_StopGrabbing_NET(), "stop live acquisition");
                        ConfigureTrigger();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stop live acquisition or restore trigger.");
                    Status = ConnectionStatus.Error;
                }
            }
        });
    }, cancellationToken);

    private void StopLive()
    {
        if (_liveTask == null) return;
        _liveCts!.Cancel();
        _liveTask.GetAwaiter().GetResult(); // ReadFrame waits at most 100ms; no SDK lock held here.
        _liveTask = null;
        _liveCts.Dispose();
        _liveCts = null;
        _isLiveActive = false;
    }

    public Task StopLiveAsync() => RunAsync(() =>
    {
        StopLive();
        if (_camera != null) ConfigureTrigger();
    });

    public Task<ImageFrame> CaptureAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        RequireCamera();
        // Stop the preview reader so it cannot consume the triggered inspection frame.
        StopLive();
        ConfigureTrigger();
        bool software = _config.TriggerMode.Equals("On", StringComparison.OrdinalIgnoreCase) &&
                        _config.TriggerSource.Equals("Software", StringComparison.OrdinalIgnoreCase);
        Check(_camera!.MV_CC_ClearImageBuffer_NET(), "clear stale frames");
        Check(_camera.MV_CC_StartGrabbing_NET(), "start capture");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (software) Check(_camera.MV_CC_SetCommandValue_NET("TriggerSoftware"), "software trigger");
            int timeout = _config.TimeoutMs > 0 ? _config.TimeoutMs : 3000;
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = ReadFrame((int)Math.Clamp(timeout - timer.ElapsedMilliseconds, 1, 100));
                if (frame != null) return frame;
            }
            throw new CameraException($"No camera frame received within {timeout}ms. Check trigger source and connection.");
        }
        finally { Check(_camera.MV_CC_StopGrabbing_NET(), "stop capture"); }
    }, cancellationToken);

    private ImageFrame? ReadFrame(int timeoutMs)
    {
        var output = new MyCamera.MV_FRAME_OUT();
        int result = _camera!.MV_CC_GetImageBuffer_NET(ref output, timeoutMs);
        if (unchecked((uint)result) == 0x80000007) // MV_E_NODATA: normal while waiting for a trigger.
        {
            if (!_camera.MV_CC_IsDeviceConnected_NET()) throw new CameraException("Camera connection was lost.");
            return null;
        }
        Check(result, "read frame");
        try
        {
            var info = output.stFrameInfo;
            if (info.nWidth == 0 || info.nHeight == 0 || output.pBufAddr == IntPtr.Zero)
                throw new CameraException("Camera returned an invalid image buffer.");
            bool mono = info.enPixelType.ToString().Contains("Mono", StringComparison.OrdinalIgnoreCase);
            int channels = mono ? 1 : 3;
            var data = new byte[checked(info.nWidth * info.nHeight * channels)];
            var targetType = mono ? MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8 : MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed;
            if (info.enPixelType == targetType)
            {
                if (info.nFrameLen < data.Length) throw new CameraException("Camera image buffer is incomplete.");
                Marshal.Copy(output.pBufAddr, data, 0, data.Length);
                return new ImageFrame(data, info.nWidth, info.nHeight, channels, mono ? "Mono8" : "RGB8", DateTime.Now);
            }
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var conversion = new MyCamera.MV_PIXEL_CONVERT_PARAM
                {
                    nWidth = info.nWidth, nHeight = info.nHeight,
                    pSrcData = output.pBufAddr, nSrcDataLen = info.nFrameLen,
                    enSrcPixelType = info.enPixelType,
                    enDstPixelType = targetType,
                    pDstBuffer = handle.AddrOfPinnedObject(), nDstBufferSize = (uint)data.Length
                };
                Check(_camera.MV_CC_ConvertPixelType_NET(ref conversion), "convert image pixels");
                if (conversion.nDstLen != data.Length) throw new CameraException("Converted camera image has an unexpected size.");
            }
            finally { handle.Free(); }
            return new ImageFrame(data, info.nWidth, info.nHeight, channels, mono ? "Mono8" : "RGB8", DateTime.Now);
        }
        finally { Check(_camera.MV_CC_FreeImageBuffer_NET(ref output), "release SDK image buffer"); }
    }

    private void CloseCamera()
    {
        var camera = _camera;
        _camera = null;
        if (camera == null) return;
        try { camera.MV_CC_CloseDevice_NET(); }
        finally { camera.MV_CC_DestroyDevice_NET(); }
    }

    public Task DisconnectAsync() => RunAsync(() =>
    {
        try { StopLive(); }
        finally { CloseCamera(); Status = ConnectionStatus.Disconnected; }
    });

    public void Dispose()
    {
        _operations.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            try { StopLive(); }
            finally
            {
                CloseCamera();
                if (_initialized) { MyCamera.MV_CC_Finalize_NET(); _initialized = false; }
                Status = ConnectionStatus.Disconnected;
            }
        }
        finally { _operations.Release(); }
    }
}
