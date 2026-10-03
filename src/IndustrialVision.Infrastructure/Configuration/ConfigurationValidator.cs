using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Infrastructure.Configuration;

/// <summary>
/// Validates all configuration at application startup.
/// Reports missing or invalid values — does NOT crash the application.
/// In Simulation mode, hardware configuration is not required.
/// </summary>
public sealed class ConfigurationValidator
{
    private readonly ConfigurationService _config;
    private readonly ILogger<ConfigurationValidator> _logger;

    public ConfigurationValidator(ConfigurationService config, ILogger<ConfigurationValidator> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validate all configuration sections.
    /// Returns list of validation warnings/errors.
    /// Does not throw — caller decides how to handle.
    /// </summary>
    public IReadOnlyList<string> ValidateAll()
    {
        var errors = new List<string>();

        ValidateSystem(errors);

        // In simulation mode, hardware config is not required
        if (!_config.System.SimulationMode)
        {
            ValidateCamera(errors);
            ValidatePlc(errors);
            ValidateLight(errors);
            ValidateOcr(errors);
        }
        else
        {
            _logger.LogInformation("SimulationMode is enabled — hardware configuration validation skipped.");
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("Configuration validation found {Count} issue(s):", errors.Count);
            foreach (var error in errors)
            {
                _logger.LogWarning("  ❌ {Error}", error);
            }
        }
        else
        {
            _logger.LogInformation("Configuration validation passed.");
        }

        return errors;
    }

    private void ValidateSystem(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(_config.System.ImageSavePath))
        {
            errors.Add("System.ImageSavePath is not configured. Images will not be saved.");
        }

        if (string.IsNullOrWhiteSpace(_config.System.LogPath))
        {
            errors.Add("System.LogPath is not configured.");
        }
    }

    private void ValidateCamera(List<string> errors)
    {
        var cam = _config.Camera;

        if (string.IsNullOrWhiteSpace(cam.SerialNumber) && string.IsNullOrWhiteSpace(cam.IpAddress))
        {
            errors.Add("Camera.SerialNumber and Camera.IpAddress are both empty. At least one is required to connect.");
        }

        if (string.IsNullOrWhiteSpace(cam.ConnectionType))
        {
            errors.Add("Camera.ConnectionType is not configured (e.g. 'GigE' or 'USB3').");
        }
    }

    private void ValidatePlc(List<string> errors)
    {
        var plc = _config.Plc;

        if (string.IsNullOrWhiteSpace(plc.IpAddress) || plc.IpAddress.Contains("NEEDS_"))
            errors.Add("PLC.IpAddress: missing (NEEDS_CONFIGURATION)");

        if (plc.Port <= 0)
            errors.Add("PLC.Port: missing or invalid");

        if (string.IsNullOrWhiteSpace(plc.Protocol) || plc.Protocol.Contains("NEEDS_"))
            errors.Add("PLC.Protocol: missing (NEEDS_PLC_PROTOCOL_INFORMATION)");

        if (string.IsNullOrWhiteSpace(plc.Model) || plc.Model.Contains("NEEDS_"))
            errors.Add("PLC.Model: missing (NEEDS_PLC_INFORMATION)");

        // Validate critical signal addresses
        if (string.IsNullOrWhiteSpace(plc.Addresses.Trigger) || plc.Addresses.Trigger.Contains("NEEDS_"))
            errors.Add("Trigger Address: missing (NEEDS_CONFIGURATION)");

        if (string.IsNullOrWhiteSpace(plc.Addresses.Ready) || plc.Addresses.Ready.Contains("NEEDS_"))
            errors.Add("Ready Address: missing (NEEDS_CONFIGURATION)");

        if (string.IsNullOrWhiteSpace(plc.Addresses.OK) || plc.Addresses.OK.Contains("NEEDS_"))
            errors.Add("OK Address: missing (NEEDS_CONFIGURATION)");

        if (string.IsNullOrWhiteSpace(plc.Addresses.NG) || plc.Addresses.NG.Contains("NEEDS_"))
            errors.Add("NG Address: missing (NEEDS_CONFIGURATION)");
    }

    private void ValidateLight(List<string> errors)
    {
        var light = _config.Light;

        if (string.IsNullOrWhiteSpace(light.IpAddress))
            errors.Add("Light.IpAddress is not configured.");

        if (light.Port <= 0)
            errors.Add("Light.Port is not configured or invalid.");

        if (string.IsNullOrWhiteSpace(light.Protocol))
            errors.Add("Light.Protocol is not configured. NEEDS_PROTOCOL_INFORMATION.");
    }

    private void ValidateOcr(List<string> errors)
    {
        var ocr = _config.Ocr;

        if (string.IsNullOrWhiteSpace(ocr.Type))
        {
            errors.Add("OCR.Type is not configured (e.g. 'DLL', 'EXE', 'HTTP', 'ClassLibrary'). NEEDS_PROTOCOL_INFORMATION.");
        }
    }
}
