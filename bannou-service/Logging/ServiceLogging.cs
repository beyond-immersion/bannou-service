using BeyondImmersion.BannouService.Configuration;
using Serilog;

namespace BeyondImmersion.BannouService.Logging;

/// <summary>
/// A collection of helpers for generating service logs.
/// Extends log method to able to include arbitrary JSON
/// metadata, along with the message.
/// </summary>
public static class ServiceLogging
{
    private static ILoggerFactory? _factory;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets or creates the shared logger factory. Disposed via DisposeFactory() on shutdown.
    /// </summary>
    private static ILoggerFactory GetOrCreateFactory()
    {
        if (_factory is not null)
        {
            return _factory;
        }

        lock (_lock)
        {
            // Double-check after acquiring lock
            if (_factory is not null)
            {
                return _factory;
            }

            var factory = new LoggerFactory();
            try
            {
                factory.AddSerilog(Log.Logger);
                _factory = factory;
                factory = null; // Ownership transferred to static field
            }
            finally
            {
                factory?.Dispose(); // Dispose only if ownership NOT transferred
            }

            return _factory;
        }
    }

    /// <summary>
    /// Create a JSON-enabled service logger.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger CreateApplicationLogger()
    {
        var logFilepath = Path.Combine(Directory.GetCurrentDirectory(), $"logs/app.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is((Serilog.Events.LogEventLevel)Program.Configuration.AppLoggingLevel)
            .WriteToFile(logFilepath).WriteToConsole().WriteToCloud()
            .ReadFrom.Configuration(IServiceConfiguration.BuildConfigurationRoot())
            .CreateLogger();

        return GetOrCreateFactory().CreateLogger("app");
    }

    /// <summary>
    /// Create a simple service logger.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger CreateServiceLogger(string serviceName)
    {
        if (string.Equals("app", serviceName, StringComparison.InvariantCultureIgnoreCase))
            throw new ArgumentException("'app' is not a valid service logger name. Use CreateApplicationLogger() instead is meaning to do so.");

        var logFilepath = Path.Combine(Directory.GetCurrentDirectory(), $"logs/{serviceName.ToLower()}.log");
        Log.Logger = new LoggerConfiguration()
            .WriteToFile(logFilepath).WriteToConsole().WriteToCloud()
            .ReadFrom.Configuration(IServiceConfiguration.BuildConfigurationRoot())
            .CreateLogger();

        return GetOrCreateFactory().CreateLogger(serviceName.ToLower());
    }

    /// <summary>
    /// Disposes the shared logger factory. Call on application shutdown.
    /// </summary>
    public static void DisposeFactory()
    {
        lock (_lock)
        {
            _factory?.Dispose();
            _factory = null;
        }
    }

    private static LoggerConfiguration WriteToFile(this LoggerConfiguration config, string logFilepath)
    {
        if (Program.Configuration.LogMode.HasFlag(AppConfiguration.LogModes.File))
            config.WriteTo.File(logFilepath, rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}");

        return config;
    }

    private static LoggerConfiguration WriteToConsole(this LoggerConfiguration config)
    {
        if (Program.Configuration.LogMode.HasFlag(AppConfiguration.LogModes.Console))
            config.WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}");

        return config;
    }

    private static LoggerConfiguration WriteToCloud(this LoggerConfiguration config)
    {
        //if (Program.Configuration.LogMode.HasFlag(AppConfiguration.LogModes.Cloud))
        //    config.WriteTo.File(logFilepath, rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}");

        return config;
    }

    /// <summary>
    /// Create a simple logger for the given service type.
    /// </summary>
    public static void CreateServiceLogger<T>()
        where T : IBannouService
        => CreateServiceLogger(typeof(T).Name);
}
