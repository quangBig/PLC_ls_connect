using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace IndustrialVision.Infrastructure.Logging;

/// <summary>
/// Configures Serilog logging for the application.
/// Logs to both Console and File.
/// Log file path is configurable.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Create and configure the Serilog logger.
    /// </summary>
    /// <param name="logPath">Base directory for log files.</param>
    /// <returns>Configured ILoggerFactory for DI registration.</returns>
    public static ILoggerFactory CreateLoggerFactory(string logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            logPath = "Logs";
        }

        // Ensure log directory exists
        Directory.CreateDirectory(logPath);

        var logFilePath = Path.Combine(logPath, "IndustrialVision-.log");

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 50 * 1024 * 1024, // 50 MB per file
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        var factory = new LoggerFactory();
        factory.AddSerilog(serilogLogger, dispose: true);

        return factory;
    }
}
