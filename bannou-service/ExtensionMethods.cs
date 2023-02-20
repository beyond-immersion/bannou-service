using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService
{
    public static partial class ExtensionMethods
    {
        /// <summary>
        /// Regex for stripping out characters that would be invalid in URLs.
        /// </summary>
        [GeneratedRegex("[^a-zA-Z0-9\\s-]")]
        public static partial Regex REGEX_InvalidChars();

        /// <summary>
        /// Regex for replacing single spaces.
        /// </summary>
        [GeneratedRegex("\\s")]
        public static partial Regex REGEX_Spaces();

        /// <summary>
        /// Regex for replacing double spaces.
        /// </summary>
        [GeneratedRegex("\\s+")]
        public static partial Regex REGEX_MultipleSpaces();

        /// <summary>
        /// Logging extension/helper methods, for including additional context as JSON.
        /// </summary>
        public static void Log(this ILogger logger, LogLevel level, Exception? exception, string message, JObject? logParams,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
            => ServiceLogging.Log(logger, level, exception, message, logParams, callerName, callerFile, lineNumber);

        /// <summary>
        /// Logging extension/helper methods, for including additional context as JSON.
        /// </summary>
        public static void Log(this ILogger logger, LogLevel level, Exception? exc, string message,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
            => logger.Log(level, exc, message, null, callerName, callerFile, lineNumber);

        /// <summary>
        /// Check if field or property has the "Obsolete" attribute attached.
        /// </summary>
        public static bool IsObsolete(this MemberInfo memberInfo)
            => memberInfo.GetCustomAttribute<ObsoleteAttribute>() != null;

        /// <summary>
        /// Check if field or property has the "Obsolete" attribute attached, and return message if so.
        /// </summary>
        public static bool IsObsolete(this MemberInfo memberInfo, out string? message)
        {
            ObsoleteAttribute? obsAttr = memberInfo.GetCustomAttribute<ObsoleteAttribute>();
            if (obsAttr != null)
            {
                message = obsAttr.Message;
                return true;
            }

            message = null;
            return false;
        }

        /// <summary>
        /// Generate a URL-safe slug from any string.
        /// </summary>
        public static string GenerateSlug(this string phrase)
        {
            var str = phrase.RemoveAccent().ToLower();
            str = REGEX_InvalidChars().Replace(str, "");
            str = REGEX_MultipleSpaces().Replace(str, " ").Trim();
            str = str[..(str.Length <= 45 ? str.Length : 45)].Trim();
            str = REGEX_Spaces().Replace(str, "-");
            return str;
        }

        /// <summary>
        /// Remove accent characters from a string.
        /// Returns new string.
        /// </summary>
        public static string RemoveAccent(this string txt)
        {
            var bytes = System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(txt);
            return System.Text.Encoding.ASCII.GetString(bytes);
        }

        /// <summary>
        /// Async extension method for generating and sending a JSON response to client.
        /// </summary>
        public static async Task SendResponseAsync<T>(this HttpContext context, T data, CancellationToken cancellationToken = default)
            where T : class
        {
            if (cancellationToken == default)
                cancellationToken = Program.ShutdownCancellationTokenSource.Token;

            await context.Response.WriteAsJsonAsync(data, cancellationToken);
            await context.Response.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Extension method for generating and sending a JSON response to client.
        /// </summary>
        public static void SendResponse<T>(this HttpContext context, T data)
            where T : class
        {
            context.Response.WriteAsJsonAsync(data, Program.ShutdownCancellationTokenSource.Token).Wait(Program.ShutdownCancellationTokenSource.Token);
            context.Response.StartAsync(Program.ShutdownCancellationTokenSource.Token).Wait(Program.ShutdownCancellationTokenSource.Token);
        }
    }
}
