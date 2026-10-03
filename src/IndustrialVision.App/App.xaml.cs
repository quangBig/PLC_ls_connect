using System.Windows;
using IndustrialVision.App.ViewModels;
using IndustrialVision.App.Views;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.App;

/// <summary>
/// Application entry point.
/// Configures DI, validates configuration, creates MainWindow with ViewModel.
/// Contains NO hardware logic.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global safety nets: log the real cause and keep the window alive instead of exiting silently.
        DispatcherUnhandledException += (_, args) =>
        {
            LogFatal("UI thread", args.Exception);
            MessageBox.Show(
                $"Unexpected error (the application keeps running):\n\n{args.Exception.Message}",
                "IndustrialVision - Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogFatal("Background task", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogFatal("AppDomain", args.ExceptionObject as Exception);

        try
        {
            // Determine config base path — use the application's base directory
            var configBasePath = AppDomain.CurrentDomain.BaseDirectory;

            // ── Configure Dependency Injection ──
            _serviceProvider = ServiceRegistration.ConfigureServices(configBasePath);

            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<App>();

            logger.LogInformation("═══════════════════════════════════════════════");
            logger.LogInformation("  IndustrialVision Application Starting");
            logger.LogInformation("═══════════════════════════════════════════════");

            // ── Validate Configuration ──
            var configValidator = _serviceProvider.GetRequiredService<ConfigurationValidator>();
            var validationErrors = configValidator.ValidateAll();

            if (validationErrors.Count > 0)
            {
                logger.LogWarning("Configuration has {Count} issue(s). Application will continue but some features may not work.", validationErrors.Count);
            }

            var configService = _serviceProvider.GetRequiredService<ConfigurationService>();
            if (configService.System.SimulationMode)
            {
                logger.LogWarning("══════════════════════════════════════════════════");
                logger.LogWarning("  ⚠ SIMULATION MODE — No real hardware connected");
                logger.LogWarning("══════════════════════════════════════════════════");
            }

            // ── Create MainViewModel via DI ──
            var mainViewModel = new MainViewModel(
                _serviceProvider.GetRequiredService<ICameraService>(),
                _serviceProvider.GetRequiredService<IPlcService>(),
                _serviceProvider.GetRequiredService<ILightController>(),
                _serviceProvider.GetRequiredService<IOcrService>(),
                configService,
                _serviceProvider.GetRequiredService<ILogger<MainViewModel>>(),
                _serviceProvider.GetService<IndustrialVision.Plc.Handshake.IPlcHandshakeService>(),
                _serviceProvider.GetService<IndustrialVision.Plc.Trigger.PlcTriggerMonitor>(),
                _serviceProvider.GetService<IndustrialVision.Plc.Heartbeat.PlcHeartbeatService>());

            // ── Create and show MainWindow ──
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            MainWindow = mainWindow;
            mainWindow.Show();

            logger.LogInformation("Application started successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Application startup failed:\n\n{ex.Message}\n\nSee log for details.",
                "IndustrialVision - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private void LogFatal(string source, Exception? ex)
    {
        try
        {
            var logger = _serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger<App>();
            if (logger != null)
                logger.LogError(ex, "[UNHANDLED:{Source}] {Message}", source, ex?.Message);
            else
                System.Diagnostics.Debug.WriteLine($"[UNHANDLED:{source}] {ex}");
        }
        catch
        {
            // never throw from the error reporter
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var loggerFactory = _serviceProvider?.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<App>();
            logger?.LogInformation("Application shutting down...");

            // Dispose all services
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            logger?.LogInformation("Application shutdown complete.");
        }
        catch
        {
            // Swallow exceptions during shutdown
        }

        base.OnExit(e);
    }
}
