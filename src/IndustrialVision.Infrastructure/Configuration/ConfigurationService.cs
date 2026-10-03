using IndustrialVision.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace IndustrialVision.Infrastructure.Configuration;

/// <summary>
/// Centralized configuration service.
/// Reads from JSON config files and provides strongly typed configuration objects.
/// 
/// Architecture:
///   JSON files → IConfiguration → ConfigurationService → Strongly Typed Objects → Services
/// 
/// Services must NOT read JSON directly. They receive configuration through DI.
/// </summary>
public sealed class ConfigurationService
{
    private readonly IConfiguration _configuration;

    public SystemConfiguration System { get; }
    public CameraConfiguration Camera { get; }
    public PlcConfiguration Plc { get; }
    public LightConfiguration Light { get; }
    public OcrConfiguration Ocr { get; }

    public ConfigurationService(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        System = new SystemConfiguration();
        Camera = new CameraConfiguration();
        Plc = new PlcConfiguration();
        Light = new LightConfiguration();
        Ocr = new OcrConfiguration();

        _configuration.GetSection("System").Bind(System);
        _configuration.GetSection("Camera").Bind(Camera);
        _configuration.GetSection("PLC").Bind(Plc);
        _configuration.GetSection("Light").Bind(Light);
        _configuration.GetSection("OCR").Bind(Ocr);
    }

    /// <summary>
    /// Reload configuration from files.
    /// Useful when configuration is changed at runtime without restarting the application.
    /// </summary>
    public void Reload()
    {
        if (_configuration is IConfigurationRoot configRoot)
        {
            configRoot.Reload();
        }

        _configuration.GetSection("System").Bind(System);
        _configuration.GetSection("Camera").Bind(Camera);
        _configuration.GetSection("PLC").Bind(Plc);
        _configuration.GetSection("Light").Bind(Light);
        _configuration.GetSection("OCR").Bind(Ocr);
    }
}
