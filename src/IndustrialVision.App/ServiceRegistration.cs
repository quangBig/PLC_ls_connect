using IndustrialVision.Camera;
using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Infrastructure.Configuration;
using IndustrialVision.Infrastructure.Logging;
using IndustrialVision.Light;
using IndustrialVision.Ocr;
using IndustrialVision.Plc;
using IndustrialVision.Plc.Drivers;
using IndustrialVision.Plc.Handshake;
using IndustrialVision.Plc.Heartbeat;
using IndustrialVision.Plc.Trigger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.App;

/// <summary>
/// Centralized Dependency Injection setup.
/// Lives in the App project because it needs references to all service projects.
/// Infrastructure layer does NOT reference hardware service projects — avoiding circular deps.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceProvider ConfigureServices(string configBasePath)
    {
        var services = new ServiceCollection();

        // ── Configuration ──────────────────────────────────────────────
        var configuration = new ConfigurationBuilder()
            .SetBasePath(configBasePath)
            .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("Config/camera.json", optional: false, reloadOnChange: true)
            .AddJsonFile("Config/plc.json", optional: false, reloadOnChange: true)
            .AddJsonFile("Config/light.json", optional: false, reloadOnChange: true)
            .AddJsonFile("Config/ocr.json", optional: false, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // ── Configuration Service ──────────────────────────────────────
        var configService = new ConfigurationService(configuration);
        services.AddSingleton(configService);

        // Register individual configurations for direct injection
        services.AddSingleton(configService.System);
        services.AddSingleton(configService.Camera);
        services.AddSingleton(configService.Plc);
        services.AddSingleton(configService.Light);
        services.AddSingleton(configService.Ocr);

        // ── Logging ────────────────────────────────────────────────────
        var loggerFactory = LoggingSetup.CreateLoggerFactory(configService.System.LogPath);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // ── Configuration Validator ────────────────────────────────────
        services.AddSingleton<ConfigurationValidator>();

        // ── Hardware Services ──────────────────────────────────────────
        if (configService.System.SimulationMode)
        {
            RegisterSimulationServices(services);
        }
        else
        {
            RegisterRealServices(services, configService.Plc);
        }

        // ── PLC Handshake, Trigger, & Heartbeat Services ───────────────
        services.AddSingleton<IPlcHandshakeService, PlcHandshakeService>();
        services.AddSingleton<PlcTriggerMonitor>();
        services.AddSingleton<PlcHeartbeatService>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Register simulation/mock services for development and testing.
    /// </summary>
    private static void RegisterSimulationServices(IServiceCollection services)
    {
        services.AddSingleton<ICameraService, MockCameraService>();
        services.AddSingleton<IPlcService, MockPlcService>();
        // Light controller: Real Rsee PW-D-24W20-8TE (8-channel LAN)
        services.AddSingleton<ILightController, RseeLightController>();
        services.AddSingleton<IOcrService, MockOcrService>();
    }

    /// <summary>
    /// Register real hardware services for production.
    /// </summary>
    private static void RegisterRealServices(IServiceCollection services, PlcConfiguration plcConfig)
    {
        // Basler pylon SDK for real camera hardware; simulation uses MockCameraService.
        services.AddSingleton<ICameraService, BaslerCameraService>();

        // Real Light Controller: Rsee PW-D-24W20-8TE
        services.AddSingleton<ILightController, RseeLightController>();

        // PLC LS Driver & Service — driver is selected by "Protocol" in plc.json
        if (LsXgtDedicatedDriver.Supports(plcConfig.Protocol))
            services.AddSingleton<ILsPlcDriver, LsXgtDedicatedDriver>();
        else
            services.AddSingleton<ILsPlcDriver, LsPlcPlaceholderDriver>();
        services.AddSingleton<IPlcService, PlcService>();

        // TODO Phase 5: Replace with real OCR service
        services.AddSingleton<IOcrService, MockOcrService>();
    }
}
