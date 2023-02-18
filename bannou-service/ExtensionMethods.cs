using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BeyondImmersion.BannouService
{
    public static partial class ExtensionMethods
    {
        public static string GenerateDaprServiceID<T>(this T _)
            where T : IDaprService
        {
            var serviceAttr = typeof(T).GetCustomAttributes(typeof(DaprServiceAttribute), true).FirstOrDefault();
            if (serviceAttr != null)
                return $"{((DaprServiceAttribute)serviceAttr).ServicePrefix}_{Program.ServiceGUID}";

            var serviceName = typeof(T).Name.ToUpperInvariant();
            if (serviceName.EndsWith("service", StringComparison.CurrentCultureIgnoreCase))
                serviceName = serviceName.Remove(serviceName.Length - "service".Length, "service".Length);

            return $"{serviceName}_{Program.ServiceGUID}";
        }

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
    }
}
