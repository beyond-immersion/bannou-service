using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Logging
{
    public static class ServiceLogging
    {
        public static ILoggerFactory LogFactory { get; } = LoggerFactory.Create((options) =>
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

        public static ILogger CreateLogger() => LogFactory.CreateLogger("service");

        public static ILogger CreateLogger<T>() => LogFactory.CreateLogger<T>();

        public static void Log(ILogger logger, LogLevel level, Exception? exception, string message, JObject? logParams,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
        {
            // avoid manipulating the params object, for safety
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

        public static string DefaultLogStateFormatter(JObject state, Exception? exc)
        {
            return state != null && state.TryGetValue("message", out JToken? msg) && msg.Type == JTokenType.String
                ? exc != null ? msg.ToString() + "\n" + exc.ToString() : msg.ToString()
                : exc?.ToString() ?? string.Empty;
        }
    }
}
