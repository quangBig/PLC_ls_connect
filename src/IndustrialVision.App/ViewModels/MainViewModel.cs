using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialVision.App.Commands;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Core.Models;
using IndustrialVision.Infrastructure.Configuration;
using IndustrialVision.Plc.Handshake;
using IndustrialVision.Plc.Heartbeat;
using IndustrialVision.Plc.Trigger;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.App.ViewModels;

/// <summary>
/// Main ViewModel for the HMI.
/// Binds to MainWindow.xaml.
/// Contains NO hardware logic — only calls service interfaces.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ICameraService _cameraService;
    private readonly IPlcService _plcService;
    private readonly ILightController _lightController;
    private readonly IOcrService _ocrService;
    private readonly ConfigurationService _configService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IPlcHandshakeService? _handshakeService;
    private readonly PlcTriggerMonitor? _triggerMonitor;
    private readonly PlcHeartbeatService? _heartbeatService;
    private readonly SemaphoreSlim _cycleLock = new(1, 1);

    // ── Connection Status Properties ──────────────────────────────────
    private ConnectionStatus _plcStatus = ConnectionStatus.Disconnected;
    public ConnectionStatus PlcStatus
    {
        get => _plcStatus;
        set => SetProperty(ref _plcStatus, value);
    }

    private ConnectionStatus _cameraStatus = ConnectionStatus.Disconnected;
    public ConnectionStatus CameraStatus
    {
        get => _cameraStatus;
        set
        {
            if (SetProperty(ref _cameraStatus, value))
                OnPropertyChanged(nameof(CanSelectCamera));
        }
    }

    public ObservableCollection<CameraDeviceInfo> AvailableCameras { get; } = new();
    private CameraDeviceInfo? _selectedCamera;
    public CameraDeviceInfo? SelectedCamera
    {
        get => _selectedCamera;
        set => SetProperty(ref _selectedCamera, value);
    }

    public bool CanSelectCamera => CameraStatus is ConnectionStatus.Disconnected or ConnectionStatus.Error;
    private double _cameraExposure;
    public double CameraExposure
    {
        get => _cameraExposure;
        set => SetProperty(ref _cameraExposure, value);
    }
    private double _cameraGain;
    public double CameraGain
    {
        get => _cameraGain;
        set => SetProperty(ref _cameraGain, value);
    }
    private string _cameraMessage = "Discover and select a camera, then connect.";
    public string CameraMessage
    {
        get => _cameraMessage;
        private set => SetProperty(ref _cameraMessage, value);
    }

    private ConnectionStatus _lightStatus = ConnectionStatus.Disconnected;
    public ConnectionStatus LightStatus
    {
        get => _lightStatus;
        set => SetProperty(ref _lightStatus, value);
    }

    private ConnectionStatus _ocrStatus = ConnectionStatus.Disconnected;
    public ConnectionStatus OcrStatus
    {
        get => _ocrStatus;
        set => SetProperty(ref _ocrStatus, value);
    }

    // ── Machine State Properties ──────────────────────────────────────
    private MachineState _machineState = MachineState.Stopped;
    public MachineState MachineState
    {
        get => _machineState;
        set => SetProperty(ref _machineState, value);
    }

    private OperationMode _operationMode = OperationMode.Manual;
    public OperationMode OperationMode
    {
        get => _operationMode;
        set => SetProperty(ref _operationMode, value);
    }

    // ── Display Properties ────────────────────────────────────────────
    private string _applicationTitle = "OCR INSPECTION SYSTEM";
    public string ApplicationTitle
    {
        get => _applicationTitle;
        set => SetProperty(ref _applicationTitle, value);
    }

    private string _ocrResultText = string.Empty;
    public string OcrResultText
    {
        get => _ocrResultText;
        set => SetProperty(ref _ocrResultText, value);
    }

    private string _inspectionResultText = string.Empty;
    public string InspectionResultText
    {
        get => _inspectionResultText;
        set => SetProperty(ref _inspectionResultText, value);
    }

    private InspectionResult _lastResult = InspectionResult.Unknown;
    public InspectionResult LastResult
    {
        get => _lastResult;
        set => SetProperty(ref _lastResult, value);
    }

    private BitmapSource? _cameraImage;
    public BitmapSource? CameraImage
    {
        get => _cameraImage;
        set => SetProperty(ref _cameraImage, value);
    }

    private string _statusMessage = "Application started.";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isSimulationMode;
    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set => SetProperty(ref _isSimulationMode, value);
    }

    private int _cycleCount;
    public int CycleCount
    {
        get => _cycleCount;
        set => SetProperty(ref _cycleCount, value);
    }

    private int _okCount;
    public int OkCount
    {
        get => _okCount;
        set => SetProperty(ref _okCount, value);
    }

    private int _ngCount;
    public int NgCount
    {
        get => _ngCount;
        set => SetProperty(ref _ngCount, value);
    }

    // ── Log Messages ──────────────────────────────────────────────────
    private string _logText = string.Empty;
    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    // ── Light Controller Channels (Rsee PW-D-24W20-8TE) ──────────────
    public ObservableCollection<LightChannelViewModel> LightChannels { get; } = new();

    // ── Continuous Light Control (Điều Khiển Đèn Liên Tục) ─────────────
    public int[] AvailableLightChannels { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

    private int _continuousStartChannel = 1;
    public int ContinuousStartChannel
    {
        get => _continuousStartChannel;
        set
        {
            if (SetProperty(ref _continuousStartChannel, Math.Clamp(value, 1, 8)))
            {
                OnPropertyChanged(nameof(ContinuousRangeButtonText));
                OnPropertyChanged(nameof(ContinuousLoopButtonText));
                UpdateContinuousStatusText();
            }
        }
    }

    private int _continuousEndChannel = 4;
    public int ContinuousEndChannel
    {
        get => _continuousEndChannel;
        set
        {
            if (SetProperty(ref _continuousEndChannel, Math.Clamp(value, 1, 8)))
            {
                OnPropertyChanged(nameof(ContinuousRangeButtonText));
                OnPropertyChanged(nameof(ContinuousLoopButtonText));
                UpdateContinuousStatusText();
            }
        }
    }

    private int _loopIntervalMs = 500;
    public int LoopIntervalMs
    {
        get => _loopIntervalMs;
        set
        {
            if (SetProperty(ref _loopIntervalMs, Math.Clamp(value, 1, 10000)))
            {
                UpdateContinuousStatusText();
            }
        }
    }

    private bool _isContinuousRangeOn;
    public bool IsContinuousRangeOn
    {
        get => _isContinuousRangeOn;
        private set
        {
            if (SetProperty(ref _isContinuousRangeOn, value))
            {
                OnPropertyChanged(nameof(ContinuousRangeButtonText));
                UpdateContinuousStatusText();
            }
        }
    }

    private bool _isLoopRunning;
    public bool IsLoopRunning
    {
        get => _isLoopRunning;
        private set
        {
            if (SetProperty(ref _isLoopRunning, value))
            {
                OnPropertyChanged(nameof(ContinuousLoopButtonText));
                UpdateContinuousStatusText();
            }
        }
    }

    private string _continuousStatusText = "Sẵn sàng (CH1..CH4)";
    public string ContinuousStatusText
    {
        get => _continuousStatusText;
        private set => SetProperty(ref _continuousStatusText, value);
    }

    public string ContinuousRangeButtonText =>
        IsContinuousRangeOn
            ? $"🌑 TẮT LIÊN TỤC (CH{Math.Min(ContinuousStartChannel, ContinuousEndChannel)}..CH{Math.Max(ContinuousStartChannel, ContinuousEndChannel)})"
            : $"☀ BẬT LIÊN TỤC (CH{Math.Min(ContinuousStartChannel, ContinuousEndChannel)}..CH{Math.Max(ContinuousStartChannel, ContinuousEndChannel)})";

    public string ContinuousLoopButtonText =>
        IsLoopRunning
            ? $"⏹ DỪNG VÒNG LẶP (CH{Math.Min(ContinuousStartChannel, ContinuousEndChannel)}➔CH{Math.Max(ContinuousStartChannel, ContinuousEndChannel)})"
            : $"🔄 CHẠY VÒNG LẶP (CH{Math.Min(ContinuousStartChannel, ContinuousEndChannel)}➔CH{Math.Max(ContinuousStartChannel, ContinuousEndChannel)})";


    // ── Light Controller IP / Port / Ping Settings ────────────────────
    private string _lightIpAddress = "192.168.1.100";
    public string LightIpAddress
    {
        get => _lightIpAddress;
        set => SetProperty(ref _lightIpAddress, value);
    }

    private int _lightPort = 5000;
    public int LightPort
    {
        get => _lightPort;
        set => SetProperty(ref _lightPort, value);
    }

    private string _pingResultText = "Ready";
    public string PingResultText
    {
        get => _pingResultText;
        set => SetProperty(ref _pingResultText, value);
    }

    private bool? _isPingSuccess = null;
    public bool? IsPingSuccess
    {
        get => _isPingSuccess;
        set => SetProperty(ref _isPingSuccess, value);
    }

    // ── PLC LS Handshake & Monitor Signals ─────────────────────────────
    private bool _plcReadyFlag;
    public bool PlcReadyFlag
    {
        get => _plcReadyFlag;
        set => SetProperty(ref _plcReadyFlag, value);
    }

    private bool _plcTriggerFlag;
    public bool PlcTriggerFlag
    {
        get => _plcTriggerFlag;
        set => SetProperty(ref _plcTriggerFlag, value);
    }

    private bool _plcBusyFlag;
    public bool PlcBusyFlag
    {
        get => _plcBusyFlag;
        set => SetProperty(ref _plcBusyFlag, value);
    }

    private bool _plcCaptureCompleteFlag;
    public bool PlcCaptureCompleteFlag
    {
        get => _plcCaptureCompleteFlag;
        set => SetProperty(ref _plcCaptureCompleteFlag, value);
    }

    private bool _plcOkFlag;
    public bool PlcOkFlag
    {
        get => _plcOkFlag;
        set => SetProperty(ref _plcOkFlag, value);
    }

    private bool _plcNgFlag;
    public bool PlcNgFlag
    {
        get => _plcNgFlag;
        set => SetProperty(ref _plcNgFlag, value);
    }

    private bool _plcErrorFlag;
    public bool PlcErrorFlag
    {
        get => _plcErrorFlag;
        set => SetProperty(ref _plcErrorFlag, value);
    }

    private bool _plcHeartbeatFlag;
    public bool PlcHeartbeatFlag
    {
        get => _plcHeartbeatFlag;
        set => SetProperty(ref _plcHeartbeatFlag, value);
    }

    // ── PLC Configuration Info ────────────────────────────────────────
    public string PlcModel => _configService.Plc.Model;
    public string PlcProtocol => _configService.Plc.Protocol;

    // PLC IP/Port are typed in the UI (never hardcoded); applied to PlcConfiguration on Connect.
    private string _plcIpAddress = string.Empty;
    public string PlcIpAddress
    {
        get => _plcIpAddress;
        set => SetProperty(ref _plcIpAddress, value);
    }

    private int _plcPort;
    public int PlcPort
    {
        get => _plcPort;
        set => SetProperty(ref _plcPort, value);
    }

    private string _plcPingResultText = "-";
    public string PlcPingResultText
    {
        get => _plcPingResultText;
        set => SetProperty(ref _plcPingResultText, value);
    }

    private bool? _isPlcPingSuccess;
    public bool? IsPlcPingSuccess
    {
        get => _isPlcPingSuccess;
        set => SetProperty(ref _isPlcPingSuccess, value);
    }

    // PlcAddressMap normalizes NEEDS_* placeholders to empty, so an empty Trigger = not configured yet.
    public bool IsPlcConfigured => !_configService.Plc.Model.Contains("NEEDS_") &&
                                   !_configService.Plc.Protocol.Contains("NEEDS_") &&
                                   !string.IsNullOrWhiteSpace(_configService.Plc.Addresses.Trigger);

    public string PlcConfigWarning => IsPlcConfigured
        ? string.Empty
        : "⚠ PLC signal addresses (Ready/Trigger/Busy/OK/NG/...) are not configured in config/plc.json yet. Connection and Manual Read/Write test still work; automatic handshake is idle.";

    // ── PLC Manual Test Tool ──────────────────────────────────────────
    private string _plcTestAddress = string.Empty;
    public string PlcTestAddress
    {
        get => _plcTestAddress;
        set => SetProperty(ref _plcTestAddress, value);
    }

    private bool _plcTestBitValue = true;
    public bool PlcTestBitValue
    {
        get => _plcTestBitValue;
        set => SetProperty(ref _plcTestBitValue, value);
    }

    private ushort _plcTestWordValue = 0;
    public ushort PlcTestWordValue
    {
        get => _plcTestWordValue;
        set => SetProperty(ref _plcTestWordValue, value);
    }

    private string _plcTestStringValue = "TEST";
    public string PlcTestStringValue
    {
        get => _plcTestStringValue;
        set => SetProperty(ref _plcTestStringValue, value);
    }

    private int _plcTestStringLength = 16;
    public int PlcTestStringLength
    {
        get => _plcTestStringLength;
        set => SetProperty(ref _plcTestStringLength, value);
    }

    private string _plcTestResult = "Ready";
    public string PlcTestResult
    {
        get => _plcTestResult;
        set => SetProperty(ref _plcTestResult, value);
    }

    private bool _isAutoRunning;
    public bool IsAutoRunning
    {
        get => _isAutoRunning;
        set => SetProperty(ref _isAutoRunning, value);
    }

    // ── Commands ──────────────────────────────────────────────────────
    public ICommand ConnectAllCommand { get; }
    public ICommand DisconnectAllCommand { get; }
    public ICommand ConnectCameraCommand { get; }
    public ICommand DiscoverCamerasCommand { get; }
    public ICommand ApplyCameraParametersCommand { get; }
    public ICommand SaveCameraSettingsCommand { get; }
    public ICommand DisconnectCameraCommand { get; }
    public ICommand StartLiveCommand { get; }
    public ICommand StopLiveCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ConnectPlcCommand { get; }
    public ICommand DisconnectPlcCommand { get; }
    public ICommand ConnectLightCommand { get; }
    public ICommand DisconnectLightCommand { get; }
    public ICommand TurnOnAllLightsCommand { get; }
    public ICommand TurnOffAllLightsCommand { get; }
    public ICommand CycleTestLightsCommand { get; }
    public ICommand ToggleContinuousRangeCommand { get; }
    public ICommand ToggleContinuousLoopCommand { get; }
    public ICommand StopContinuousLightCommand { get; }
    public ICommand PingLightCommand { get; }
    public ICommand SaveLightSettingsCommand { get; }
    public ICommand PingPlcCommand { get; }
    public ICommand SavePlcSettingsCommand { get; }
    public ICommand InitOcrCommand { get; }
    public ICommand ResetCommand { get; }

    // PLC Manual Test & Workflow Commands
    public ICommand ReadPlcBitCommand { get; }
    public ICommand WritePlcBitCommand { get; }
    public ICommand ReadPlcWordCommand { get; }
    public ICommand WritePlcWordCommand { get; }
    public ICommand ReadPlcStringCommand { get; }
    public ICommand WritePlcStringCommand { get; }
    public ICommand SimulatePlcTriggerCommand { get; }
    public ICommand ExecuteCycleCommand { get; }
    public ICommand StartAutoInspectionCommand { get; }
    public ICommand StopAutoInspectionCommand { get; }

    public MainViewModel(
        ICameraService cameraService,
        IPlcService plcService,
        ILightController lightController,
        IOcrService ocrService,
        ConfigurationService configService,
        ILogger<MainViewModel> logger,
        IPlcHandshakeService? handshakeService = null,
        PlcTriggerMonitor? triggerMonitor = null,
        PlcHeartbeatService? heartbeatService = null)
    {
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        _lightController = lightController ?? throw new ArgumentNullException(nameof(lightController));
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handshakeService = handshakeService;
        _triggerMonitor = triggerMonitor;
        _heartbeatService = heartbeatService;

        // Read config
        ApplicationTitle = _configService.System.ApplicationTitle;
        IsSimulationMode = _configService.System.SimulationMode;
        CameraExposure = _configService.Camera.Exposure;
        CameraGain = _configService.Camera.Gain;
        LightIpAddress = !string.IsNullOrWhiteSpace(_configService.Light.IpAddress)
            ? _configService.Light.IpAddress
            : "192.168.1.100";
        LightPort = _configService.Light.Port > 0 ? _configService.Light.Port : 5000;

        // PLC network settings come from plc.json; blank/NEEDS_ means "user must type it in the UI"
        string cfgPlcIp = _configService.Plc.IpAddress ?? string.Empty;
        PlcIpAddress = cfgPlcIp.Contains("NEEDS_", StringComparison.OrdinalIgnoreCase) ? string.Empty : cfgPlcIp.Trim();
        PlcPort = _configService.Plc.Port > 0 ? _configService.Plc.Port : 0;

        // Default test address: only the configured Trigger address if present, otherwise the user types one
        PlcTestAddress = _configService.Plc.Addresses.Trigger;

        // Wire up status change events
        _cameraService.StatusChanged += (_, status) =>
            Application.Current?.Dispatcher.BeginInvoke(() => CameraStatus = status);
        _plcService.StatusChanged += (_, status) =>
            Application.Current?.Dispatcher.Invoke(() => PlcStatus = status);
        _lightController.StatusChanged += (_, status) =>
            Application.Current?.Dispatcher.Invoke(() => LightStatus = status);
        _ocrService.StatusChanged += (_, status) =>
            Application.Current?.Dispatcher.Invoke(() => OcrStatus = status);

        // Wire up camera frame received for live preview
        _cameraService.FrameReceived += OnFrameReceived;

        // Wire up PLC trigger monitor events
        if (_triggerMonitor != null)
        {
            _triggerMonitor.TriggerStateChanged += (_, state) =>
                Application.Current?.Dispatcher.Invoke(() => PlcTriggerFlag = state);

            _triggerMonitor.TriggerFired += async (_, _) =>
            {
                AppendLog("⚡ PLC Hardware Trigger (Rising Edge 0->1) detected!");
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await ExecuteInspectionCycleAsync();
                });
            };
        }

        // Wire up PLC heartbeat tick
        if (_heartbeatService != null)
        {
            _heartbeatService.HeartbeatTick += (_, state) =>
                Application.Current?.Dispatcher.Invoke(() => PlcHeartbeatFlag = state);
        }

        // Safety net: an exception escaping any button command is logged instead of closing the app
        AsyncRelayCommand.UnhandledException = ex =>
        {
            AppendLog($"❌ Unexpected error: {ex.Message}");
            _logger.LogError(ex, "Unhandled exception in command.");
        };

        // Initialize commands
        ConnectAllCommand = new AsyncRelayCommand(ConnectAllAsync);
        DisconnectAllCommand = new AsyncRelayCommand(DisconnectAllAsync);
        ConnectCameraCommand = new AsyncRelayCommand(ConnectCameraAsync);
        DiscoverCamerasCommand = new AsyncRelayCommand(DiscoverCamerasAsync);
        ApplyCameraParametersCommand = new AsyncRelayCommand(ApplyCameraParametersAsync);
        SaveCameraSettingsCommand = new AsyncRelayCommand(SaveCameraSettingsAsync);
        DisconnectCameraCommand = new AsyncRelayCommand(DisconnectCameraAsync);
        StartLiveCommand = new AsyncRelayCommand(StartLiveAsync);
        StopLiveCommand = new AsyncRelayCommand(StopLiveAsync);
        CaptureCommand = new AsyncRelayCommand(CaptureAsync);
        ConnectPlcCommand = new AsyncRelayCommand(ConnectPlcFromButtonAsync);
        DisconnectPlcCommand = new AsyncRelayCommand(DisconnectPlcAsync);
        ConnectLightCommand = new AsyncRelayCommand(ConnectLightAsync);
        DisconnectLightCommand = new AsyncRelayCommand(DisconnectLightAsync);
        TurnOnAllLightsCommand = new AsyncRelayCommand(TurnOnAllLightsAsync);
        TurnOffAllLightsCommand = new AsyncRelayCommand(TurnOffAllLightsAsync);
        CycleTestLightsCommand = new AsyncRelayCommand(CycleTestLightsAsync);
        ToggleContinuousRangeCommand = new AsyncRelayCommand(ToggleContinuousRangeAsync);
        ToggleContinuousLoopCommand = new AsyncRelayCommand(ToggleContinuousLoopAsync);
        StopContinuousLightCommand = new AsyncRelayCommand(StopAllContinuousLightAsync);
        PingLightCommand = new AsyncRelayCommand(PingLightAsync);
        SaveLightSettingsCommand = new AsyncRelayCommand(SaveLightSettingsAsync);
        PingPlcCommand = new AsyncRelayCommand(PingPlcAsync);
        SavePlcSettingsCommand = new AsyncRelayCommand(SavePlcSettingsAsync);
        InitOcrCommand = new AsyncRelayCommand(InitOcrAsync);
        ResetCommand = new AsyncRelayCommand(ResetAsync);

        // PLC Manual Test Commands
        ReadPlcBitCommand = new AsyncRelayCommand(ReadPlcBitAsync);
        WritePlcBitCommand = new AsyncRelayCommand(WritePlcBitAsync);
        ReadPlcWordCommand = new AsyncRelayCommand(ReadPlcWordAsync);
        WritePlcWordCommand = new AsyncRelayCommand(WritePlcWordAsync);
        ReadPlcStringCommand = new AsyncRelayCommand(ReadPlcStringAsync);
        WritePlcStringCommand = new AsyncRelayCommand(WritePlcStringAsync);
        SimulatePlcTriggerCommand = new AsyncRelayCommand(SimulatePlcTriggerAsync);
        ExecuteCycleCommand = new AsyncRelayCommand(() => ExecuteInspectionCycleAsync());
        StartAutoInspectionCommand = new AsyncRelayCommand(() => { StartAutoInspection(); return Task.CompletedTask; });
        StopAutoInspectionCommand = new AsyncRelayCommand(() => { StopAutoInspection(); return Task.CompletedTask; });

        // Initialize 8 channels for Rsee PW-D-24W20-8TE
        InitializeLightChannels();

        AppendLog("Application started.");
        if (IsSimulationMode)
        {
            AppendLog("⚠ SIMULATION MODE (Camera/PLC/OCR). Real Light Controller active for Rsee PW-D-24W20-8TE.");
        }
        if (!IsPlcConfigured)
        {
            AppendLog($"ℹ [PLC LS] Model={PlcModel}, Protocol={PlcProtocol}. Signal addresses not configured in plc.json yet (manual test available).");
        }
    }

    // ── Command Implementations ───────────────────────────────────────

    private async Task ConnectAllAsync()
    {
        AppendLog("Connecting all devices...");
        MachineState = MachineState.Connecting;

        try
        {
            await ConnectPlcAsync();
            await ConnectLightAsync();
            await ConnectCameraAsync();
            await InitOcrAsync();

            MachineState = MachineState.Ready;
            StatusMessage = "All devices connected.";
            AppendLog("All devices connected. System READY.");
        }
        catch (Exception ex)
        {
            MachineState = MachineState.Error;
            StatusMessage = $"Connection error: {ex.Message}";
            AppendLog($"❌ Connection error: {ex.Message}");
            _logger.LogError(ex, "Failed to connect all devices.");
        }
    }

    private async Task DisconnectAllAsync()
    {
        AppendLog("Disconnecting all devices...");
        try
        {
            if (_cameraService.IsLiveActive)
                await _cameraService.StopLiveAsync();

            await _cameraService.DisconnectAsync();
            await _plcService.DisconnectAsync();
            await _lightController.DisconnectAsync();

            MachineState = MachineState.Stopped;
            StatusMessage = "All devices disconnected.";
            AppendLog("All devices disconnected.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Disconnect error: {ex.Message}");
            _logger.LogError(ex, "Error during disconnect.");
        }
    }

    private async Task DiscoverCamerasAsync()
    {
        try
        {
            var devices = await _cameraService.DiscoverCamerasAsync();
            string serial = SelectedCamera?.SerialNumber ?? _configService.Camera.SerialNumber;
            AvailableCameras.Clear();
            foreach (var device in devices) AvailableCameras.Add(device);
            SelectedCamera = AvailableCameras.FirstOrDefault(c => c.SerialNumber == serial)
                ?? AvailableCameras.FirstOrDefault(c => !string.IsNullOrWhiteSpace(_configService.Camera.IpAddress) && c.IpAddress == _configService.Camera.IpAddress)
                ?? AvailableCameras.FirstOrDefault();
            CameraMessage = devices.Count > 0 ? $"Found {devices.Count} camera(s)." : "No Basler cameras found. Check pylon drivers, cable and network subnet.";
            AppendLog(CameraMessage);
        }
        catch (Exception ex)
        {
            CameraMessage = $"Camera discovery failed: {ex.Message}";
            AppendLog(CameraMessage);
            _logger.LogError(ex, "Camera discovery failed.");
        }
    }

    private void UpdateCameraConfiguration()
    {
        if (!double.IsFinite(CameraExposure) || CameraExposure <= 0 || !double.IsFinite(CameraGain) || CameraGain < 0)
            throw new InvalidOperationException("Exposure must be positive and Gain must be non-negative.");
        if (!_cameraService.IsConnected && SelectedCamera != null)
        {
            _configService.Camera.Name = SelectedCamera.Name;
            _configService.Camera.SerialNumber = SelectedCamera.SerialNumber;
            _configService.Camera.IpAddress = SelectedCamera.IpAddress;
            _configService.Camera.ConnectionType = SelectedCamera.InterfaceType;
        }
        _configService.Camera.Exposure = CameraExposure;
        _configService.Camera.Gain = CameraGain;
    }

    private async Task ApplyCameraParametersAsync()
    {
        if (!_cameraService.IsConnected) throw new InvalidOperationException("Connect the camera before applying parameters.");
        await _cameraService.SetExposureAsync(CameraExposure);
        await _cameraService.SetGainAsync(CameraGain);
        CameraMessage = $"Applied Exposure={CameraExposure:F0} µs, Gain={CameraGain:G}.";
        AppendLog(CameraMessage);
    }

    private async Task SaveCameraSettingsAsync()
    {
        UpdateCameraConfiguration();
        var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "camera.json");
        var json = System.Text.Json.JsonSerializer.Serialize(new { Camera = _configService.Camera },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(configPath, json);
        CameraMessage = "Saved camera settings to Config/camera.json.";
        AppendLog(CameraMessage);
    }

    private async Task ConnectCameraAsync()
    {
        try
        {
            AppendLog("Camera connecting...");
            UpdateCameraConfiguration();
            await _cameraService.ConnectAsync();
            CameraExposure = _configService.Camera.Exposure;
            CameraGain = _configService.Camera.Gain;
            CameraMessage = $"Connected: {_configService.Camera.SerialNumber} {_configService.Camera.IpAddress}";
            AppendLog("Camera connected.");
        }
        catch (Exception ex)
        {
            CameraMessage = $"Connection failed: {ex.Message}";
            AppendLog($"❌ Camera connection failed: {ex.Message}");
            _logger.LogError(ex, "Camera connection failed.");
            throw;
        }
    }

    private async Task DisconnectCameraAsync()
    {
        try
        {
            if (_cameraService.IsLiveActive)
                await _cameraService.StopLiveAsync();
            await _cameraService.DisconnectAsync();
            CameraImage = null;
            CameraMessage = "Camera disconnected.";
            AppendLog("Camera disconnected.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Camera disconnect error: {ex.Message}");
            _logger.LogError(ex, "Camera disconnect error.");
        }
    }

    private async Task StartLiveAsync()
    {
        try
        {
            AppendLog("Starting live preview...");
            await _cameraService.StartLiveAsync();
            CameraMessage = "Live preview active (continuous acquisition).";
            AppendLog("Live preview started.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Live preview error: {ex.Message}");
            _logger.LogError(ex, "Live preview error.");
        }
    }

    private async Task StopLiveAsync()
    {
        try
        {
            await _cameraService.StopLiveAsync();
            CameraMessage = "Live preview stopped; configured trigger restored.";
            AppendLog("Live preview stopped.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Stop live error: {ex.Message}");
            _logger.LogError(ex, "Stop live error.");
        }
    }

    private async Task CaptureAsync()
    {
        try
        {
            AppendLog("Capturing image...");
            var frame = await _cameraService.CaptureAsync();
            UpdateCameraImage(frame);
            CameraMessage = $"Captured {frame.Width}x{frame.Height}.";
            AppendLog($"Image captured: {frame.Width}x{frame.Height}");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Capture failed: {ex.Message}");
            _logger.LogError(ex, "Capture failed.");
        }
    }

    /// <summary>
    /// Button entry point. ConnectPlcAsync logs the failure itself and rethrows (ConnectAllAsync relies on
    /// that); a button click must never let the exception escape.
    /// </summary>
    private async Task ConnectPlcFromButtonAsync()
    {
        try
        {
            await ConnectPlcAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"PLC connection failed: {ex.Message}";
        }
    }

    private async Task ConnectPlcAsync()
    {
        try
        {
            // Apply the IP/Port typed in the UI (single shared PlcConfiguration instance used by the driver)
            _configService.Plc.IpAddress = PlcIpAddress.Trim();
            _configService.Plc.Port = PlcPort;

            AppendLog($"PLC connecting ({PlcModel} via {PlcProtocol} to {PlcIpAddress.Trim()}:{PlcPort})...");
            await _plcService.ConnectAsync();

            AppendLog($"✅ PLC connected successfully to {PlcIpAddress.Trim()}:{PlcPort}. Ready for manual Read/Write test.");
            StatusMessage = $"PLC connected ({PlcIpAddress.Trim()}:{PlcPort})";
        }
        catch (Exception ex)
        {
            AppendLog($"❌ PLC connection failed: {ex.Message}");
            _logger.LogError(ex, "PLC connection failed.");
            throw;
        }
    }

    private async Task DisconnectPlcAsync()
    {
        try
        {
            await _plcService.DisconnectAsync();
            PlcReadyFlag = false;
            PlcBusyFlag = false;
            AppendLog("PLC disconnected.");
            StatusMessage = "PLC disconnected.";
        }
        catch (Exception ex)
        {
            AppendLog($"❌ PLC disconnect error: {ex.Message}");
            _logger.LogError(ex, "PLC disconnect error.");
        }
    }

    private async Task PingPlcAsync()
    {
        if (string.IsNullOrWhiteSpace(PlcIpAddress))
        {
            PlcPingResultText = "NO IP";
            IsPlcPingSuccess = false;
            AppendLog("❌ Please enter the PLC IP address to ping.");
            return;
        }

        string targetIp = PlcIpAddress.Trim();
        PlcPingResultText = "Pinging...";
        IsPlcPingSuccess = null;
        AppendLog($"📡 Pinging PLC at {targetIp}...");

        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(targetIp, 2000);

            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
            {
                PlcPingResultText = $"OK ({reply.RoundtripTime}ms)";
                IsPlcPingSuccess = true;
                AppendLog($"✅ Ping PLC {targetIp} SUCCESS! Roundtrip: {reply.RoundtripTime}ms");
            }
            else
            {
                PlcPingResultText = $"FAIL ({reply.Status})";
                IsPlcPingSuccess = false;
                AppendLog($"❌ Ping PLC {targetIp} FAILED: {reply.Status}. Check LAN cable, PLC IP (set in XG5000), and PC subnet.");
            }
        }
        catch (Exception ex)
        {
            PlcPingResultText = "ERROR";
            IsPlcPingSuccess = false;
            AppendLog($"❌ Ping PLC {targetIp} exception: {ex.Message}");
        }
    }

    private async Task SavePlcSettingsAsync()
    {
        try
        {
            _configService.Plc.IpAddress = PlcIpAddress.Trim();
            _configService.Plc.Port = PlcPort;

            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "plc.json");
            if (System.IO.File.Exists(configPath))
            {
                var json = await System.IO.File.ReadAllTextAsync(configPath);
                // Only the top-level "IpAddress" / "Port" of plc.json are rewritten (first occurrence).
                var ipRegex = new System.Text.RegularExpressions.Regex(@"""IpAddress""\s*:\s*""[^""]*""");
                var portRegex = new System.Text.RegularExpressions.Regex(@"""Port""\s*:\s*\d+");
                var updatedJson = ipRegex.Replace(json, $"\"IpAddress\": \"{PlcIpAddress.Trim()}\"", 1);
                updatedJson = portRegex.Replace(updatedJson, $"\"Port\": {PlcPort}", 1);

                await System.IO.File.WriteAllTextAsync(configPath, updatedJson);
                AppendLog($"💾 Saved PLC settings (IP: {PlcIpAddress.Trim()}, Port: {PlcPort}) to plc.json");
            }
            else
            {
                AppendLog($"Updated in-memory PLC settings (IP: {PlcIpAddress.Trim()}, Port: {PlcPort}).");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Failed to save PLC settings: {ex.Message}");
            _logger.LogError(ex, "Failed to save PLC settings.");
        }
    }

    // ── PLC Manual Test Implementations ───────────────────────────────

    private async Task ReadPlcBitAsync()
    {
        if (string.IsNullOrWhiteSpace(PlcTestAddress))
        {
            PlcTestResult = "Address empty";
            return;
        }

        try
        {
            PlcTestResult = "Reading...";
            bool val = await _plcService.ReadBitAsync(PlcTestAddress.Trim());
            PlcTestResult = $"Bit [{PlcTestAddress.Trim()}] = {(val ? "1 (ON)" : "0 (OFF)")}";
            PlcTestBitValue = val;
            AppendLog($"[PLC MANUAL] Read Bit [{PlcTestAddress.Trim()}] -> {(val ? "1" : "0")}");
        }
        catch (Exception ex)
        {
            PlcTestResult = $"Error: {ex.Message}";
            AppendLog($"❌ [PLC MANUAL] Read Bit [{PlcTestAddress.Trim()}] failed: {ex.Message}");
        }
    }

    private async Task WritePlcBitAsync()
    {
        if (string.IsNullOrWhiteSpace(PlcTestAddress))
        {
            PlcTestResult = "Address empty";
            return;
        }

        try
        {
            PlcTestResult = "Writing...";
            await _plcService.WriteBitAsync(PlcTestAddress.Trim(), PlcTestBitValue);
            PlcTestResult = $"Wrote [{PlcTestAddress.Trim()}] = {(PlcTestBitValue ? "1" : "0")}";
            AppendLog($"[PLC MANUAL] Wrote Bit [{PlcTestAddress.Trim()}] = {PlcTestBitValue}");
        }
        catch (Exception ex)
        {
            PlcTestResult = $"Error: {ex.Message}";
            AppendLog($"❌ [PLC MANUAL] Write Bit [{PlcTestAddress.Trim()}] failed: {ex.Message}");
        }
    }

    private async Task ReadPlcWordAsync()
    {
        if (string.IsNullOrWhiteSpace(PlcTestAddress))
        {
            PlcTestResult = "Address empty";
            return;
        }

        try
        {
            PlcTestResult = "Reading...";
            ushort val = await _plcService.ReadWordAsync(PlcTestAddress.Trim());
            PlcTestResult = $"Word [{PlcTestAddress.Trim()}] = {val} (0x{val:X4})";
            PlcTestWordValue = val;
            AppendLog($"[PLC MANUAL] Read Word [{PlcTestAddress.Trim()}] -> {val}");
        }
        catch (Exception ex)
        {
            PlcTestResult = $"Error: {ex.Message}";
            AppendLog($"❌ [PLC MANUAL] Read Word [{PlcTestAddress.Trim()}] failed: {ex.Message}");
        }
    }

    private async Task WritePlcWordAsync()
    {
        if (string.IsNullOrWhiteSpace(PlcTestAddress))
        {
            PlcTestResult = "Address empty";
            return;
        }

        try
        {
            PlcTestResult = "Writing...";
            await _plcService.WriteWordAsync(PlcTestAddress.Trim(), PlcTestWordValue);
            PlcTestResult = $"Wrote Word [{PlcTestAddress.Trim()}] = {PlcTestWordValue}";
            AppendLog($"[PLC MANUAL] Wrote Word [{PlcTestAddress.Trim()}] = {PlcTestWordValue}");
        }
        catch (Exception ex)
        {
            PlcTestResult = $"Error: {ex.Message}";
            AppendLog($"❌ [PLC MANUAL] Write Word [{PlcTestAddress.Trim()}] failed: {ex.Message}");
        }
    }

    private async Task ReadPlcStringAsync()
    {
        if (string.IsNullOrWhiteSpace(PlcTestAddress))
        {
            PlcTestResult = "Address empty";
            return;
        }

        try
        {
            PlcTestResult = "Reading...";
            string val = await _plcService.ReadStringAsync(PlcTestAddress.Trim(), PlcTestStringLength);
            PlcTestResult = $"String [{PlcTestAddress.Trim()}] = \"{val}\"";
            PlcTestStringValue = val;
            AppendLog($"[PLC MANUAL] Read String [{PlcTestAddress.Trim()}, len={PlcTestStringLength}] -> \"{val}\"");
        }
        catch (Exception ex)
        {
            PlcTestResult = $"Error: {ex.Message}";
            AppendLog($"❌ [PLC MANUAL] Read String [{PlcTestAddress.Trim()}] failed: {ex.Message}");
        }
    }

    private async Task WritePlcStringAsync()
    {
        if (string.IsNullOrWhiteSpace(PlcTestAddress))
        {
            PlcTestResult = "Address empty";
            return;
        }

        try
        {
            PlcTestResult = "Writing...";
            await _plcService.WriteStringAsync(PlcTestAddress.Trim(), PlcTestStringValue);
            PlcTestResult = $"Wrote String [{PlcTestAddress.Trim()}] = \"{PlcTestStringValue}\"";
            AppendLog($"[PLC MANUAL] Wrote String [{PlcTestAddress.Trim()}] = \"{PlcTestStringValue}\"");
        }
        catch (Exception ex)
        {
            PlcTestResult = $"Error: {ex.Message}";
            AppendLog($"❌ [PLC MANUAL] Write String [{PlcTestAddress.Trim()}] failed: {ex.Message}");
        }
    }

    private async Task SimulatePlcTriggerAsync()
    {
        AppendLog("▶ Manual Trigger (UI Button) initiated. Executing inspection cycle...");
        await ExecuteInspectionCycleAsync();
    }

    private void StartAutoInspection()
    {
        IsAutoRunning = true;
        OperationMode = OperationMode.Auto;
        _triggerMonitor?.StartMonitoring();
        _heartbeatService?.Start();
        AppendLog("🚀 AUTO INSPECTION MODE started. Waiting for PLC triggers...");
    }

    private void StopAutoInspection()
    {
        IsAutoRunning = false;
        OperationMode = OperationMode.Manual;
        AppendLog("⏹ AUTO INSPECTION MODE stopped. Switched to Manual mode.");
    }

    // ── Inspection Cycle (PLC Handshake Orchestration) ────────────────

    public async Task ExecuteInspectionCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!await _cycleLock.WaitAsync(0, cancellationToken))
        {
            AppendLog("⚠ Inspection cycle already in progress. Ignoring trigger.");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            AppendLog("═════════ START INSPECTION CYCLE ═════════");

            // 1. Handshake: Clear old results, assert BUSY
            if (_handshakeService != null)
            {
                await _handshakeService.ClearResultFlagsAsync(cancellationToken);
                PlcOkFlag = false;
                PlcNgFlag = false;
                PlcCaptureCompleteFlag = false;

                await _handshakeService.SetBusyAsync(true, cancellationToken);
                PlcBusyFlag = true;
            }

            // 2. Light ON
            MachineState = MachineState.Lighting;
            AppendLog("💡 Lighting: Turning ON inspection lights...");
            if (_lightController.IsConnected)
            {
                foreach (var ch in LightChannels)
                {
                    if (ch.Intensity > 0)
                    {
                        await _lightController.TurnOnAsync(ch.Channel, cancellationToken);
                        ch.IsOn = true;
                    }
                }
            }
            await Task.Delay(50, cancellationToken); // Settling time

            // 3. Camera Capture
            MachineState = MachineState.Capturing;
            AppendLog("📷 Camera: Capturing image frame...");
            var frame = await _cameraService.CaptureAsync(cancellationToken);
            UpdateCameraImage(frame);

            // 4. Capture Complete (PLC can advance conveyor)
            if (_handshakeService != null)
            {
                await _handshakeService.SetCaptureCompleteAsync(true, cancellationToken);
                PlcCaptureCompleteFlag = true;
            }

            // Optional light turn off to prevent heating
            if (!_cameraService.IsLiveActive && _lightController.IsConnected)
            {
                await _lightController.TurnOffAllAsync(cancellationToken);
                foreach (var ch in LightChannels) ch.IsOn = false;
            }

            // 5. OCR Processing
            MachineState = MachineState.Processing;
            AppendLog($"🔍 OCR: Processing image ({frame.Width}x{frame.Height})...");
            var ocrResult = await _ocrService.ProcessAsync(frame, cancellationToken);

            OcrResultText = ocrResult.Text;
            bool isOk = ocrResult.Success && !string.IsNullOrWhiteSpace(ocrResult.Text);
            LastResult = isOk ? InspectionResult.OK : InspectionResult.NG;
            InspectionResultText = isOk ? "PASS" : "FAIL";

            CycleCount++;
            if (isOk) OkCount++; else NgCount++;

            AppendLog($"🔍 OCR Result: '{ocrResult.Text}' (Conf: {ocrResult.Confidence:P0}) -> {(isOk ? "✅ OK" : "❌ NG")}");

            // 6. Send Result to PLC
            MachineState = MachineState.SendingResult;
            if (_handshakeService != null)
            {
                if (isOk)
                {
                    await _handshakeService.SetResultOkAsync(cancellationToken);
                    PlcOkFlag = true;
                }
                else
                {
                    await _handshakeService.SetResultNgAsync(cancellationToken);
                    PlcNgFlag = true;
                }

                if (!string.IsNullOrWhiteSpace(_configService.Plc.Addresses.Result))
                {
                    await _handshakeService.WriteOcrResultAsync(ocrResult.Text ?? string.Empty, cancellationToken);
                }

                await Task.Delay(100, cancellationToken); // Result hold time

                // 7. Finish Handshake: Clear BUSY, assert READY
                await _handshakeService.SetBusyAsync(false, cancellationToken);
                PlcBusyFlag = false;
                await _handshakeService.SetReadyAsync(true, cancellationToken);
                PlcReadyFlag = true;
            }

            sw.Stop();
            MachineState = MachineState.Ready;
            AppendLog($"═════════ CYCLE FINISHED ({sw.ElapsedMilliseconds}ms) ═════════");
        }
        catch (Exception ex)
        {
            sw.Stop();
            MachineState = MachineState.Error;
            StatusMessage = $"Cycle Error: {ex.Message}";
            AppendLog($"❌ Inspection cycle error: {ex.Message}");
            _logger.LogError(ex, "Inspection cycle failed.");

            if (_handshakeService != null)
            {
                try
                {
                    await _handshakeService.SetErrorAsync(true, CancellationToken.None);
                    PlcErrorFlag = true;
                    await _handshakeService.SetBusyAsync(false, CancellationToken.None);
                    PlcBusyFlag = false;
                }
                catch { }
            }
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    private async Task ConnectLightAsync()
    {
        try
        {
            _configService.Light.IpAddress = LightIpAddress.Trim();
            _configService.Light.Port = LightPort;

            AppendLog($"Light controller connecting to {LightIpAddress.Trim()}:{LightPort}...");
            await _lightController.ConnectAsync();
            AppendLog("Light controller connected.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Light connection failed: {ex.Message}");
            _logger.LogError(ex, "Light connection failed.");
            throw;
        }
    }

    private async Task PingLightAsync()
    {
        if (string.IsNullOrWhiteSpace(LightIpAddress))
        {
            PingResultText = "NO IP";
            IsPingSuccess = false;
            AppendLog("❌ Please enter a valid IP address to ping.");
            return;
        }

        string targetIp = LightIpAddress.Trim();
        PingResultText = "Pinging...";
        IsPingSuccess = null;
        AppendLog($"📡 Pinging light controller at {targetIp}...");

        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(targetIp, 2000);

            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
            {
                PingResultText = $"OK ({reply.RoundtripTime}ms)";
                IsPingSuccess = true;
                AppendLog($"✅ Ping {targetIp} SUCCESS! Roundtrip: {reply.RoundtripTime}ms");
            }
            else
            {
                PingResultText = $"FAIL ({reply.Status})";
                IsPingSuccess = false;
                AppendLog($"❌ Ping {targetIp} FAILED: {reply.Status}. Check LAN cable, IP address, and PC subnet.");
            }
        }
        catch (Exception ex)
        {
            PingResultText = "ERROR";
            IsPingSuccess = false;
            AppendLog($"❌ Ping {targetIp} exception: {ex.Message}");
        }
    }

    private async Task SaveLightSettingsAsync()
    {
        try
        {
            _configService.Light.IpAddress = LightIpAddress.Trim();
            _configService.Light.Port = LightPort;

            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "light.json");
            if (System.IO.File.Exists(configPath))
            {
                var json = await System.IO.File.ReadAllTextAsync(configPath);
                var updatedJson = System.Text.RegularExpressions.Regex.Replace(
                    json,
                    @"""IpAddress""\s*:\s*""[^""]*""",
                    $"\"IpAddress\": \"{LightIpAddress.Trim()}\"");
                updatedJson = System.Text.RegularExpressions.Regex.Replace(
                    updatedJson,
                    @"""Port""\s*:\s*\d+",
                    $"\"Port\": {LightPort}");

                await System.IO.File.WriteAllTextAsync(configPath, updatedJson);
                AppendLog($"💾 Saved light settings (IP: {LightIpAddress.Trim()}, Port: {LightPort}) to light.json");
            }
            else
            {
                AppendLog($"Updated in-memory light settings (IP: {LightIpAddress.Trim()}, Port: {LightPort}).");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Failed to save light settings: {ex.Message}");
            _logger.LogError(ex, "Failed to save light settings.");
        }
    }

    private async Task DisconnectLightAsync()
    {
        try
        {
            StopContinuousLoop();
            IsContinuousRangeOn = false;
            await _lightController.DisconnectAsync();
            foreach (var ch in LightChannels)
            {
                ch.IsOn = false;
            }
            AppendLog("Light controller disconnected.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Light disconnect error: {ex.Message}");
            _logger.LogError(ex, "Light disconnect error.");
        }
    }

    private void InitializeLightChannels()
    {
        LightChannels.Clear();
        var cfgChannels = _configService.Light.Channels ?? new List<LightChannelConfiguration>();
        int defaultIntensity = _configService.Light.DefaultIntensity > 0 ? _configService.Light.DefaultIntensity : 100;

        for (int i = 1; i <= 8; i++)
        {
            var chConfig = cfgChannels.Find(c => c.Channel == i);
            int initialIntensity = chConfig?.Intensity > 0 ? chConfig.Intensity : defaultIntensity;
            string name = !string.IsNullOrWhiteSpace(chConfig?.Name) ? chConfig.Name : $"CH{i}";

            var chVm = new LightChannelViewModel(
                i,
                name,
                initialIntensity,
                _lightController,
                AppendLog,
                _logger);

            LightChannels.Add(chVm);
        }
    }

    private async Task TurnOnAllLightsAsync()
    {
        try
        {
            if (!_lightController.IsConnected)
            {
                AppendLog("Connecting light controller first...");
                await ConnectLightAsync();
            }

            AppendLog("Turning ON all light channels (CH1-CH8)...");
            foreach (var ch in LightChannels)
            {
                await _lightController.SetChannelAsync(ch.Channel, ch.Intensity);
                await _lightController.TurnOnAsync(ch.Channel);
                ch.IsOn = true;
            }
            AppendLog("All 8 light channels turned ON.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Turn ON all lights failed: {ex.Message}");
            _logger.LogError(ex, "Turn ON all lights failed.");
        }
    }

    private async Task TurnOffAllLightsAsync()
    {
        try
        {
            StopContinuousLoop();
            IsContinuousRangeOn = false;

            if (!_lightController.IsConnected) return;

            AppendLog("Turning OFF all light channels (CH1-CH8)...");
            await _lightController.TurnOffAllAsync();
            foreach (var ch in LightChannels)
            {
                ch.IsOn = false;
            }
            AppendLog("All 8 light channels turned OFF.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Turn OFF all lights failed: {ex.Message}");
            _logger.LogError(ex, "Turn OFF all lights failed.");
        }
    }

    private async Task CycleTestLightsAsync()
    {
        try
        {
            if (!_lightController.IsConnected)
            {
                AppendLog("Connecting light controller for cycle test...");
                await ConnectLightAsync();
            }

            AppendLog("═══ STARTING LIGHT SEQUENCE TEST (CH1 -> CH8) ═══");
            await _lightController.TurnOffAllAsync();
            foreach (var ch in LightChannels) ch.IsOn = false;

            for (int i = 0; i < LightChannels.Count; i++)
            {
                var ch = LightChannels[i];
                AppendLog($"Testing {ch.Name} (Intensity: {ch.Intensity})...");
                await _lightController.TurnOnAsync(ch.Channel);
                ch.IsOn = true;

                await Task.Delay(400);

                await _lightController.TurnOffAsync(ch.Channel);
                ch.IsOn = false;
            }

            AppendLog("═══ LIGHT SEQUENCE TEST FINISHED (All 8 channels OK) ═══");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Light sequence test failed: {ex.Message}");
            _logger.LogError(ex, "Light sequence test failed.");
        }
    }

    // ── Continuous Light Control Implementation ───────────────────────────
    private CancellationTokenSource? _continuousLoopCts;

    private async Task ToggleContinuousRangeAsync()
    {
        try
        {
            if (IsContinuousRangeOn)
            {
                await StopContinuousRangeAsync();
                return;
            }

            // Stop sequential loop if running
            StopContinuousLoop();

            if (!_lightController.IsConnected)
            {
                AppendLog("Đang kết nối bộ điều khiển đèn Rsee...");
                await ConnectLightAsync();
            }

            int start = Math.Min(ContinuousStartChannel, ContinuousEndChannel);
            int end = Math.Max(ContinuousStartChannel, ContinuousEndChannel);

            AppendLog($"Bật sáng liên tục dải kênh CH{start}..CH{end} theo độ sáng đã setup...");

            for (int i = 1; i <= LightChannels.Count; i++)
            {
                var ch = LightChannels[i - 1];
                if (i >= start && i <= end)
                {
                    int intensity = ch.Intensity > 0 ? ch.Intensity : 100;
                    await _lightController.SetChannelAsync(ch.Channel, intensity);
                    await _lightController.TurnOnAsync(ch.Channel);
                    SetChannelOnState(ch, true);
                }
                else
                {
                    await _lightController.TurnOffAsync(ch.Channel);
                    SetChannelOnState(ch, false);
                }
            }

            IsContinuousRangeOn = true;
            AppendLog($"✓ Đã bật sáng liên tục các kênh CH{start}..CH{end}.");
        }
        catch (Exception ex)
        {
            IsContinuousRangeOn = false;
            AppendLog($"❌ Lỗi bật đèn liên tục: {ex.Message}");
            _logger.LogError(ex, "Failed to start continuous lighting.");
        }
    }

    private async Task StopContinuousRangeAsync()
    {
        int start = Math.Min(ContinuousStartChannel, ContinuousEndChannel);
        int end = Math.Max(ContinuousStartChannel, ContinuousEndChannel);

        try
        {
            for (int i = start; i <= end && i <= LightChannels.Count; i++)
            {
                var ch = LightChannels[i - 1];
                await _lightController.TurnOffAsync(ch.Channel);
                SetChannelOnState(ch, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error turning off continuous range.");
        }
        finally
        {
            IsContinuousRangeOn = false;
            AppendLog($"Đã tắt chế độ sáng liên tục CH{start}..CH{end}.");
        }
    }

    private async Task ToggleContinuousLoopAsync()
    {
        if (IsLoopRunning)
        {
            StopContinuousLoop();
            return;
        }

        try
        {
            if (IsContinuousRangeOn)
            {
                await StopContinuousRangeAsync();
            }

            if (!_lightController.IsConnected)
            {
                AppendLog("Đang kết nối bộ điều khiển đèn Rsee...");
                await ConnectLightAsync();
            }

            _continuousLoopCts?.Cancel();
            _continuousLoopCts?.Dispose();
            _continuousLoopCts = new CancellationTokenSource();

            IsLoopRunning = true;
            var token = _continuousLoopCts.Token;
            _ = Task.Run(() => RunContinuousLoopAsync(token), token);
        }
        catch (Exception ex)
        {
            IsLoopRunning = false;
            AppendLog($"❌ Không thể khởi động vòng lặp đèn: {ex.Message}");
            _logger.LogError(ex, "Failed to start continuous loop.");
        }
    }

    private void StopContinuousLoop()
    {
        if (_continuousLoopCts != null && !_continuousLoopCts.IsCancellationRequested)
        {
            try
            {
                _continuousLoopCts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task StopAllContinuousLightAsync()
    {
        StopContinuousLoop();
        await StopContinuousRangeAsync();
        await TurnOffAllLightsAsync();
    }

    private async Task RunContinuousLoopAsync(CancellationToken token)
    {
        int start = Math.Min(ContinuousStartChannel, ContinuousEndChannel);
        int end = Math.Max(ContinuousStartChannel, ContinuousEndChannel);
        int interval = Math.Max(1, LoopIntervalMs);
        bool isFastMode = interval < 30;

        AppendLog($"═══ BẮT ĐẦU VÒNG LẶP SÁNG LIÊN TỤC: CH{start} ➔ CH{end} (Chu kỳ: {interval}ms) ═══");

        try
        {
            // Initial clear of all channels
            await _lightController.TurnOffAllAsync(token);
            foreach (var ch in LightChannels)
            {
                SetChannelOnState(ch, false, fastMode: false);
            }

            // Pre-set intensities once to avoid redundant command packets during rapid cycling
            for (int chNum = start; chNum <= end; chNum++)
            {
                if (chNum <= LightChannels.Count)
                {
                    var ch = LightChannels[chNum - 1];
                    int intensity = ch.Intensity > 0 ? ch.Intensity : 100;
                    await _lightController.SetChannelAsync(ch.Channel, intensity, token);
                }
            }

            while (!token.IsCancellationRequested)
            {
                for (int chNum = start; chNum <= end; chNum++)
                {
                    token.ThrowIfCancellationRequested();

                    if (chNum <= LightChannels.Count)
                    {
                        var ch = LightChannels[chNum - 1];

                        await _lightController.TurnOnAsync(ch.Channel, token);
                        SetChannelOnState(ch, true, isFastMode);

                        await AccurateDelayAsync(interval, token);

                        await _lightController.TurnOffAsync(ch.Channel, token);
                        SetChannelOnState(ch, false, isFastMode);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Vòng lặp đèn gặp sự cố: {ex.Message}");
            _logger.LogError(ex, "Continuous loop exception.");
        }
        finally
        {
            IsLoopRunning = false;
            try
            {
                await _lightController.TurnOffAllAsync(CancellationToken.None);
            }
            catch { }

            foreach (var ch in LightChannels)
            {
                SetChannelOnState(ch, false, fastMode: false);
            }
            AppendLog("═══ ĐÃ DỪNG VÒNG LẶP SÁNG LIÊN TỤC ═══");
        }
    }

    private static async Task AccurateDelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0) return;

        if (milliseconds >= 25)
        {
            await Task.Delay(milliseconds, cancellationToken);
            return;
        }

        // High-resolution precision delay for sub-25ms intervals using Stopwatch
        var sw = Stopwatch.StartNew();
        long targetTicks = (long)(milliseconds * (Stopwatch.Frequency / 1000.0));

        while (sw.ElapsedTicks < targetTicks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long remainingTicks = targetTicks - sw.ElapsedTicks;
            long remainingMs = remainingTicks * 1000 / Stopwatch.Frequency;

            if (remainingMs > 5)
            {
                await Task.Delay(1, cancellationToken);
            }
            else if (remainingTicks > 500)
            {
                Thread.Yield();
            }
            else
            {
                Thread.SpinWait(20);
            }
        }
    }

    private void UpdateContinuousStatusText()
    {
        int start = Math.Min(ContinuousStartChannel, ContinuousEndChannel);
        int end = Math.Max(ContinuousStartChannel, ContinuousEndChannel);

        if (IsLoopRunning)
        {
            ContinuousStatusText = $"🔄 Đang lặp CH{start}➔CH{end} ({LoopIntervalMs}ms)";
        }
        else if (IsContinuousRangeOn)
        {
            ContinuousStatusText = $"☀ Đang sáng liên tục CH{start}..CH{end}";
        }
        else
        {
            ContinuousStatusText = $"Sẵn sàng (CH{start}..CH{end})";
        }
    }

    private static void SetChannelOnState(LightChannelViewModel ch, bool isOn, bool fastMode = false)
    {
        if (fastMode)
        {
            // In high-speed mode (< 30ms), non-blocking BeginInvoke ensures background loop isn't delayed
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(() => ch.IsOn = isOn);
            }
            else
            {
                ch.IsOn = isOn;
            }
            return;
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => ch.IsOn = isOn);
        }
        else
        {
            ch.IsOn = isOn;
        }
    }

    private async Task InitOcrAsync()
    {
        try
        {
            AppendLog("OCR service initializing...");
            await _ocrService.InitializeAsync();
            AppendLog("OCR service ready.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ OCR initialization failed: {ex.Message}");
            _logger.LogError(ex, "OCR initialization failed.");
            throw;
        }
    }

    private async Task ResetAsync()
    {
        MachineState = MachineState.Ready;
        OcrResultText = string.Empty;
        InspectionResultText = string.Empty;
        LastResult = InspectionResult.Unknown;

        if (_handshakeService != null && _plcService.IsConnected)
        {
            try
            {
                await _handshakeService.ClearResultFlagsAsync();
                await _handshakeService.SetErrorAsync(false);
                await _handshakeService.SetBusyAsync(false);
                await _handshakeService.SetReadyAsync(true);

                PlcReadyFlag = true;
                PlcBusyFlag = false;
                PlcOkFlag = false;
                PlcNgFlag = false;
                PlcErrorFlag = false;
                PlcCaptureCompleteFlag = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reset PLC handshake signals.");
            }
        }

        StatusMessage = "System reset.";
        AppendLog("System reset to READY state. Handshake flags cleared.");
    }

    // ── Helper Methods ────────────────────────────────────────────────

    private void OnFrameReceived(object? sender, ImageFrame frame)
    {
        // Must update UI on dispatcher thread
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            UpdateCameraImage(frame);
        });
    }

    private void UpdateCameraImage(ImageFrame frame)
    {
        try
        {
            if (frame.Channels == 1)
            {
                // Mono8 image
                var bitmap = BitmapSource.Create(
                    frame.Width, frame.Height,
                    96, 96,
                    PixelFormats.Gray8,
                    null,
                    frame.PixelData,
                    frame.Stride);
                bitmap.Freeze(); // Required for cross-thread use
                CameraImage = bitmap;
            }
            else if (frame.Channels == 3)
            {
                // RGB image
                var bitmap = BitmapSource.Create(
                    frame.Width, frame.Height,
                    96, 96,
                    PixelFormats.Rgb24,
                    null,
                    frame.PixelData,
                    frame.Stride);
                bitmap.Freeze();
                CameraImage = bitmap;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update camera image.");
        }
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] {message}";
        LogText = line + Environment.NewLine + LogText;

        // Keep log text manageable (max ~500 lines)
        const int maxLength = 50000;
        if (LogText.Length > maxLength)
        {
            LogText = LogText[..maxLength];
        }

        _logger.LogInformation(message);
    }

    public void Dispose()
    {
        StopContinuousLoop();
        _continuousLoopCts?.Dispose();
        _cameraService.FrameReceived -= OnFrameReceived;
        _triggerMonitor?.Dispose();
        _heartbeatService?.Dispose();
        _cycleLock.Dispose();
    }
}
