using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Application;

namespace BeyondImmersion.BannouService.Logging
{
    public static class ServiceLogging
    {
        public static ILoggerFactory LoggerFactory { get; } = new LoggerFactory();
        public static ILogger CreateLogger<T>()
        {
            return LoggerFactory.CreateLogger<T>();
        }

        public static void Log(ILogger logger, LogLevel level, Exception? exception, string message, JObject? logParams,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
        {
            // avoid manipulating the params object, for safety
            JObject logParamsObj = new();
            if (logParams != null)
            {
                foreach (var kvp in logParams)
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null && kvp.Value.Type != JTokenType.Null)
                        logParamsObj[kvp.Key] = kvp.Value;
            }

            logParamsObj["calling-file"] = Path.GetFileName(callerFile);
            logParamsObj["calling-method"] = callerName;
            logParamsObj["calling-line-number"] = lineNumber.ToString();
            logParamsObj["message"] = message;

            logger.Log(level, new EventId(), logParamsObj, exception, DefaultLogStateFormatter);
        }

        public static string DefaultLogStateFormatter(JObject state, Exception? exc)
        {
            if (state != null && state.TryGetValue("message", out JToken msg) && msg.Type == JTokenType.String)
            {
                if (exc != null)
                    return msg.ToString() + "\n" + exc.ToString();

                return msg.ToString();
            }
            return exc?.ToString() ?? string.Empty;
        }
    }
}
