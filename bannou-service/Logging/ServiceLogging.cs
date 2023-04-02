using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Logging;

/// <summary>
/// A collection of helpers for generating service logs.
/// Extends log method to able to include arbitrary JSON
/// metadata, along with the message.
/// </summary>
public static class ServiceLogging
{
    /// <summary>
    /// Return a JSON-enabled logger factory.
    /// </summary>
    public static ILoggerFactory LogFactory { get; private set; } = LoggerFactory.Create((options) =>
        {
            ILoggingBuilder unused1 = options.AddJsonConsole((options) =>
            {
                options.JsonWriterOptions = new JsonWriterOptions()
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
                    Indented = true,
                    MaxDepth = 32,
                    SkipValidation = false
                };
            });
            ILoggingBuilder unused = options.SetMinimumLevel(LogLevel.Trace);
        });

    public static ILoggerFactory SimpleLogFactory { get; private set; } = LoggerFactory.Create((options) =>
        {
            _ = options.AddSimpleConsole((options) =>
            {
                options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                options.IncludeScopes = false;
                options.UseUtcTimestamp = true;
                options.SingleLine = true;
            });
            ILoggingBuilder unused = options.SetMinimumLevel(LogLevel.Trace);
        });

    /// <summary>
    /// Create a JSON-enabled service logger.
    /// </summary>
    public static ILogger CreateLogger() => LogFactory.CreateLogger("service");

    /// <summary>
    /// Create a JSON-enabled logger for the given class.
    /// </summary>
    public static ILogger CreateLogger<T>() => LogFactory.CreateLogger<T>();

    /// <summary>
    /// Create a simple service logger.
    /// </summary>
    public static ILogger CreateSimpleLogger() => SimpleLogFactory.CreateLogger("service");

    /// <summary>
    /// Create a simple logger for the given class.
    /// </summary>
    public static ILogger CreateSimpleLogger<T>() => SimpleLogFactory.CreateLogger<T>();

    /// <summary>
    /// Additional log method that allows for arbitrary JSON metadata to be included.
    /// Has accompanying extension methods to logger, for seamless use in service.
    /// </summary>
    public static void Log(ILogger logger, LogLevel level, Exception? exception, string message, JObject? logParams,
        [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            // avoid manipulating the params input
            JObject logParamsObj = new();
            if (logParams != null)
            {
                foreach (KeyValuePair<string, JToken?> kvp in logParams)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null && kvp.Value.Type != JTokenType.Null)
                        logParamsObj[kvp.Key] = kvp.Value;
                }
            }

            logParamsObj["calling-file"] = Path.GetFileName(callerFile);
            logParamsObj["calling-method"] = callerName;
            logParamsObj["calling-line-number"] = lineNumber.ToString();
            logParamsObj["message"] = message;

            logger.Log(level, new EventId(), logParamsObj, exception, DefaultLogStateFormatter);
        }
        catch { }
    }

    /// <summary>
    /// Log state formatter that packs the JSON log message (if given) in with the exception message (if exists).
    /// </summary>
    public static string DefaultLogStateFormatter(JObject state, Exception? exc)
    {
        return state != null && state.TryGetValue("message", out JToken? msg) && msg.Type == JTokenType.String
            ? exc != null ? msg.ToString() + "\n" + exc.ToString() : msg.ToString()
            : exc?.ToString() ?? string.Empty;
    }
}
