using System.Diagnostics;
using Basler.Pylon;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Exceptions;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Core.Models;
using Microsoft.Extensions.Logging;
using PylonCamera = Basler.Pylon.Camera;

namespace IndustrialVision.Camera;

/// <summary>Basler GigE/USB3 camera service backed by the pylon .NET 8 SDK.</summary>
public sealed class BaslerCameraService : ICameraService
{
    private readonly CameraConfiguration _config;
    private readonly ILogger<BaslerCameraService> _logger;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _sdkLock = new();
    private PylonCamera? _camera;
    private PixelDataConverter? _converter;
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;
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

    public BaslerCameraService(CameraConfiguration config, ILogger<BaslerCameraService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Keep SDK initialization lazy so missing native drivers do not prevent the HMI opening.
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken token = default)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(action, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsSdkLoadFailure(ex))
        {
            Status = ConnectionStatus.Error;
            throw new CameraException("Cannot load Basler pylon runtime. Install matching pylon 11 x64 runtime and GigE/USB3 drivers; check its Runtime/x64 DLLs and PATH.", ex);
        }
        catch (Exception ex) when (ex is not (CameraException or OperationCanceledException or ObjectDisposedException))
        {
            throw new CameraException($"Basler pylon: {ex.Message}", ex);
        }
        finally { _operations.Release(); }
    }

    private Task RunAsync(Action action, CancellationToken token = default)
        => RunAsync(() => { action(); return true; }, token);

    private static bool IsSdkLoadFailure(Exception ex)
        => ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException
            || (ex.InnerException != null && IsSdkLoadFailure(ex.InnerException));

    private static string InfoValue(ICameraInfo info, string key)
        => info.ContainsKey(key) ? info[key] ?? string.Empty : string.Empty;

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
            // The HMI supports Basler GigE and USB3 cameras, excluding camera emulation and other transports.
            if (connection is not ("GigE" or "USB3")) continue;
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

    public Task<IReadOnlyList<CameraDeviceInfo>> DiscoverCamerasAsync(CancellationToken cancellationToken = default)
        => RunAsync<IReadOnlyList<CameraDeviceInfo>>(() =>
        {
            var cameras = Enumerate().Select(d => d.Info).ToArray();
            _logger.LogInformation("Discovered {Count} Basler camera(s).", cameras.Length);
            return cameras;
        }, cancellationToken);

    public Task ConnectAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        StopLive();
        CloseCamera();
        Status = ConnectionStatus.Connecting;
        try
        {
            // Re-enumerate and identify by serial/IP instead of relying on a discovery index.
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
            _camera = new PylonCamera(selected.Device);
            _camera.CameraOpened += (sender, args) => Configuration.AcquireContinuous(sender!, args);
            _camera.ConnectionLost += OnConnectionLost;
            _camera.Open();
            _converter = new PixelDataConverter();
            _converter.Parameters[PLPixelDataConverter.OutputPaddingX].SetValue(0);
            if (_config.Width > 0) SetNumber(_config.Width, "Width");
            if (_config.Height > 0) SetNumber(_config.Height, "Height");
            if (!string.IsNullOrWhiteSpace(_config.PixelFormat)) SetEnum("PixelFormat", _config.PixelFormat);
            DisableAuto("ExposureAuto");
            SetNumber(_config.Exposure, "ExposureTime", "ExposureTimeAbs");
            DisableAuto("GainAuto");
            double initialGain = _config.Gain;
            if (initialGain == 0 && !_camera.Parameters["Gain"].IsWritable && !_camera.Parameters["GainAbs"].IsWritable &&
                _camera.Parameters["GainRaw"] is IIntegerParameter rawGain && rawGain.IsWritable)
                initialGain = rawGain.GetMinimum(); // Older GigE models can have a positive minimum raw gain.
            SetNumber(initialGain, "Gain", "GainAbs", "GainRaw");
            _config.Gain = initialGain;
            if (_config.FrameRate > 0)
            {
                var enabled = _camera.Parameters["AcquisitionFrameRateEnable"] as IBooleanParameter;
                if (enabled?.IsWritable == true) enabled.SetValue(true);
                SetNumber(_config.FrameRate, "AcquisitionFrameRate", "AcquisitionFrameRateAbs");
            }
            ConfigureTrigger();
            cancellationToken.ThrowIfCancellationRequested();
            _config.Name = selected.Info.Name;
            _config.SerialNumber = selected.Info.SerialNumber;
            _config.IpAddress = selected.Info.IpAddress;
            _config.ConnectionType = selected.Info.InterfaceType;
            Status = ConnectionStatus.Ready;
            _logger.LogInformation("Connected to {Camera}.", selected.Info);
        }
        catch
        {
            try { CloseCamera(); }
            finally { Status = ConnectionStatus.Error; }
            throw;
        }
    }, cancellationToken);

    private void OnConnectionLost(object? sender, EventArgs args)
    {
        _logger.LogError("Basler camera connection was lost.");
        Status = ConnectionStatus.Error;
        try { _liveCts?.Cancel(); }
        catch (ObjectDisposedException) { } // Disconnection can race with live-loop shutdown.
    }

    private void RequireCamera()
    {
        if (_camera == null || !IsConnected || !_camera.IsOpen)
            throw new CameraException("Basler camera is not connected.");
    }

    private void SetEnum(string name, string value)
    {
        if (_camera!.Parameters[name] is not IEnumParameter parameter || !parameter.IsWritable)
            throw new CameraException($"Camera parameter {name} is unavailable or read-only.");
        parameter.SetValue(value);
    }

    private void DisableAuto(string name)
    {
        if (_camera!.Parameters[name] is IEnumParameter parameter && parameter.IsWritable)
            parameter.SetValue("Off");
    }

    private void SetNumber(double value, params string[] names)
    {
        if (!double.IsFinite(value) || value < 0) throw new CameraException($"Invalid {names[0]}: {value}.");
        foreach (string name in names)
        {
            var parameter = _camera!.Parameters[name];
            if (!parameter.IsWritable) continue;
            if (parameter is IFloatParameter floating)
            {
                if (value < floating.GetMinimum() || value > floating.GetMaximum())
                    throw new CameraException($"{name} must be between {floating.GetMinimum()} and {floating.GetMaximum()}.");
                floating.SetValue(value);
                return;
            }
            if (parameter is IIntegerParameter integer)
            {
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

    private void ConfigureTrigger(bool live = false)
    {
        bool enabled = !live && _config.TriggerMode.Equals("On", StringComparison.OrdinalIgnoreCase);
        if (_camera!.Parameters["TriggerSelector"] is IEnumParameter selector && selector.IsWritable)
        {
            var values = selector.GetAllValues().ToArray();
            // Clear every trigger inherited from a camera user set (FrameBurstStart, AcquisitionStart, ...).
            foreach (string value in values)
            {
                if (!selector.CanSetValue(value)) continue;
                selector.SetValue(value);
                if (_camera.Parameters["TriggerMode"] is IEnumParameter mode && mode.IsWritable) mode.SetValue("Off");
            }
            string? frameTrigger = selector.CanSetValue("FrameStart") ? "FrameStart"
                : selector.CanSetValue("AcquisitionStart") ? "AcquisitionStart" : null;
            if (frameTrigger == null && enabled) throw new CameraException("Camera has no supported frame trigger selector.");
            if (frameTrigger != null) selector.SetValue(frameTrigger);
        }
        SetEnum("TriggerMode", enabled ? "On" : "Off");
        if (enabled) SetEnum("TriggerSource", _config.TriggerSource);
    }

    public Task SetExposureAsync(double value) => RunAsync(() =>
    {
        RequireCamera();
        lock (_sdkLock) { DisableAuto("ExposureAuto"); SetNumber(value, "ExposureTime", "ExposureTimeAbs"); }
        _config.Exposure = value;
    });

    public Task SetGainAsync(double value) => RunAsync(() =>
    {
        RequireCamera();
        lock (_sdkLock) { DisableAuto("GainAuto"); SetNumber(value, "Gain", "GainAbs", "GainRaw"); }
        _config.Gain = value;
    });

    public Task StartLiveAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        RequireCamera();
        if (_isLiveActive) return;
        StopLive();
        ConfigureTrigger(live: true);
        try { _camera!.StreamGrabber!.Start(GrabStrategy.LatestImages, GrabLoop.ProvidedByUser); }
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
                    if (frame != null && !token.IsCancellationRequested) FrameReceived?.Invoke(this, frame);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Basler live acquisition failed.");
                Status = ConnectionStatus.Error;
            }
            finally
            {
                _isLiveActive = false;
                try
                {
                    lock (_sdkLock)
                    {
                        if (_camera!.StreamGrabber.IsGrabbing) _camera.StreamGrabber.Stop();
                        if (_camera.IsOpen && IsConnected) ConfigureTrigger();
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

    private void StopLive()
    {
        if (_liveTask == null) return;
        _liveCts!.Cancel();
        _liveTask.GetAwaiter().GetResult(); // Reader waits at most 100ms; no SDK lock is held here.
        _liveTask = null;
        var cts = _liveCts;
        _liveCts = null;
        cts.Dispose();
        _isLiveActive = false;
    }

    public Task StopLiveAsync() => RunAsync(() =>
    {
        StopLive();
        if (_camera?.IsOpen == true && IsConnected) ConfigureTrigger();
    });

    public Task<ImageFrame> CaptureAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        RequireCamera();
        StopLive(); // Prevent the live reader from taking the PLC inspection frame.
        ConfigureTrigger();
        bool software = _config.TriggerMode.Equals("On", StringComparison.OrdinalIgnoreCase) &&
            _config.TriggerSource.Equals("Software", StringComparison.OrdinalIgnoreCase);
        int timeout = _config.TimeoutMs > 0 ? _config.TimeoutMs : 3000;
        var timer = Stopwatch.StartNew();
        _camera!.StreamGrabber!.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByUser);
        try
        {
            if (software)
            {
                if (_camera.CanWaitForFrameTriggerReady)
                {
                    bool ready = false;
                    while (!ready && timer.ElapsedMilliseconds < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ready = _camera.WaitForFrameTriggerReady((int)Math.Clamp(timeout - timer.ElapsedMilliseconds, 1, 100), TimeoutHandling.Return);
                    }
                    if (!ready) throw new CameraException($"Basler camera was not ready for a software trigger within {timeout}ms.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                _camera.ExecuteSoftwareTrigger();
            }
            while (timer.ElapsedMilliseconds < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = ReadFrame((int)Math.Clamp(timeout - timer.ElapsedMilliseconds, 1, 100));
                if (frame != null) return frame;
            }
            throw new CameraException($"No Basler frame received within {timeout}ms. Check trigger source and connection.");
        }
        finally { if (_camera.StreamGrabber.IsGrabbing) _camera.StreamGrabber.Stop(); }
    }, cancellationToken);

    private ImageFrame? ReadFrame(int timeoutMs)
    {
        using var result = _camera!.StreamGrabber!.RetrieveResult(timeoutMs, TimeoutHandling.Return);
        if (result == null)
        {
            if (!_camera.IsConnected) throw new CameraException("Basler camera connection was lost.");
            return null;
        }
        if (!result.GrabSucceeded) throw new CameraException($"Basler grab failed ({result.ErrorCode}): {result.ErrorDescription}");
        bool mono = result.PixelTypeValue.ToString().StartsWith("Mono", StringComparison.OrdinalIgnoreCase);
        int channels = mono ? 1 : 3;
        var data = new byte[checked(result.Width * result.Height * channels)];
        _converter!.OutputPixelFormat = mono ? PixelType.Mono8 : PixelType.RGB8packed;
        _converter.Convert(data, result); // Copy into managed storage before releasing the pylon buffer.
        return new ImageFrame(data, result.Width, result.Height, channels, mono ? "Mono8" : "RGB8", DateTime.Now);
    }

    private void CloseCamera()
    {
        var camera = _camera;
        _camera = null;
        try
        {
            if (camera != null)
            {
                camera.ConnectionLost -= OnConnectionLost;
                try { if (camera.IsOpen) camera.Close(); }
                finally { camera.Dispose(); }
            }
        }
        finally { _converter?.Dispose(); _converter = null; }
    }

    public Task DisconnectAsync() => RunAsync(() =>
    {
        try { StopLive(); }
        finally
        {
            try { CloseCamera(); }
            finally { Status = ConnectionStatus.Disconnected; }
        }
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
                try { CloseCamera(); }
                finally { Status = ConnectionStatus.Disconnected; }
            }
        }
        finally { _operations.Release(); }
    }
}
