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
        [GeneratedRegex("[^a-z0-9\\s-]")]
        private static partial Regex Regex_InvalidChars();

        [GeneratedRegex("\\s")]
        private static partial Regex Regex_Spaces();

        [GeneratedRegex("\\s+")]
        private static partial Regex Regex_MultipleSpaces();

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
            str = Regex_InvalidChars().Replace(str, "");
            str = Regex_MultipleSpaces().Replace(str, " ").Trim();
            str = str[..(str.Length <= 45 ? str.Length : 45)].Trim();
            str = Regex_Spaces().Replace(str, "-");
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
    }
}
