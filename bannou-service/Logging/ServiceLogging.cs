using Serilog;

namespace BeyondImmersion.BannouService.Logging;

/// <summary>
/// A collection of helpers for generating service logs.
/// Extends log method to able to include arbitrary JSON
/// metadata, along with the message.
/// </summary>
public static class ServiceLogging
{
    /// <summary>
    /// Create a JSON-enabled service logger.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger CreateApplicationLogger()
    {
        var logFilepath = Path.Combine(Directory.GetCurrentDirectory() + $"logs/app.log");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logFilepath, rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}")
            .ReadFrom.Configuration(Configuration.IServiceConfiguration.BuildConfigurationRoot())
            .CreateLogger();

        ILoggerFactory factory = new LoggerFactory().AddSerilog(Log.Logger);
        return factory.CreateLogger("app");
    }

    /// <summary>
    /// Create a simple service logger.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger CreateServiceLogger(string serviceName)
    {
        if (string.Equals("app", serviceName, StringComparison.InvariantCultureIgnoreCase))
            throw new ArgumentException("'app' is not a valid service logger name. Use CreateApplicationLogger() instead is meaning to do so.");

        var logFilepath = Path.Combine(Directory.GetCurrentDirectory() + $"logs/{serviceName.ToLower()}.log");
        var logger = new LoggerConfiguration()
            .WriteTo.File(logFilepath, rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}")
            .ReadFrom.Configuration(Configuration.IServiceConfiguration.BuildConfigurationRoot())
            .CreateLogger();

        ILoggerFactory factory = new LoggerFactory().AddSerilog(Log.Logger);
        return factory.CreateLogger(serviceName.ToLower());
    }

    /// <summary>
    /// Create a simple logger for the given service type.
    /// </summary>
    public static void CreateServiceLogger<T>()
        where T : IDaprService
        => CreateServiceLogger(typeof(T).Name);
}
