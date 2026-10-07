using System.Windows.Input;
using IndustrialVision.App.Commands;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.App.ViewModels;

/// <summary>
/// ViewModel representing a single light channel (CH1 to CH8) of Rsee PW-D-24W20-8TE.
/// Provides two-way binding for slider intensity and toggle switch for live hardware control.
/// </summary>
public sealed class LightChannelViewModel : ViewModelBase
{
    private readonly ILightController _lightController;
    private readonly ILogger? _logger;
    private readonly Action<string>? _logAction;

    private int _intensity;
    private bool _isOn;

    public int Channel { get; }
    public string Name { get; }

    private bool _useForInspection = true;
    public bool UseForInspection
    {
        get => _useForInspection;
        set => SetProperty(ref _useForInspection, value);
    }

    public int Intensity
    {
        get => _intensity;
        set
        {
            if (SetProperty(ref _intensity, Math.Clamp(value, 0, 255)))
            {
                if (IsOn && _lightController.IsConnected)
                {
                    _ = ApplyIntensityAsync(_intensity);
                }
            }
        }
    }

    public bool IsOn
    {
        get => _isOn;
        set => SetProperty(ref _isOn, value);
    }

    public ICommand ToggleCommand { get; }
    public ICommand ApplyIntensityCommand { get; }

    public LightChannelViewModel(
        int channel,
        string name,
        int initialIntensity,
        ILightController lightController,
        Action<string>? logAction = null,
        ILogger? logger = null)
    {
        Channel = channel;
        Name = name;
        _intensity = Math.Clamp(initialIntensity, 0, 255);
        _lightController = lightController ?? throw new ArgumentNullException(nameof(lightController));
        _logAction = logAction;
        _logger = logger;

        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        ApplyIntensityCommand = new AsyncRelayCommand(() => ApplyIntensityAsync(Intensity));
    }

    public async Task ToggleAsync()
    {
        try
        {
            if (!_lightController.IsConnected)
            {
                _logAction?.Invoke($"⚠ Light controller is not connected. Connect first to toggle {Name}.");
                return;
            }

            if (IsOn)
            {
                await _lightController.TurnOffAsync(Channel);
                IsOn = false;
                _logAction?.Invoke($"Light {Name} turned OFF.");
            }
            else
            {
                if (Intensity <= 0)
                {
                    Intensity = 100;
                }
                await _lightController.SetChannelAsync(Channel, Intensity);
                await _lightController.TurnOnAsync(Channel);
                IsOn = true;
                _logAction?.Invoke($"Light {Name} turned ON (Intensity: {Intensity}).");
            }
        }
        catch (Exception ex)
        {
            _logAction?.Invoke($"❌ Failed to toggle {Name}: {ex.Message}");
            _logger?.LogError(ex, "Failed to toggle {Name}", Name);
        }
    }

    public async Task ApplyIntensityAsync(int newIntensity)
    {
        try
        {
            if (!_lightController.IsConnected) return;

            await _lightController.SetChannelAsync(Channel, newIntensity);
            _logAction?.Invoke($"Light {Name} set to intensity {newIntensity}/255.");
        }
        catch (Exception ex)
        {
            _logAction?.Invoke($"❌ Failed to set {Name} intensity: {ex.Message}");
            _logger?.LogError(ex, "Failed to set {Name} intensity", Name);
        }
    }
}
