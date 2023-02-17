using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;

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

        public static WebApplication AddDaprServices(this WebApplication webApp)
        {
            // get all dapr services in all loaded assemblies
            var serviceClasses = BaseServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
            if (!serviceClasses.Any())
            {
                Program.Logger.Log(LogLevel.Error, null, $"No dapr services found to intantiate.");
                return webApp;
            }

            // prefixes need to be unique, so build a registry
            var serviceLookup = new Dictionary<string, (Type, DaprServiceAttribute)>();
            foreach (var serviceClass in serviceClasses)
            {
                var servicePrefix = serviceClass.Item2.ServicePrefix.ToUpper();
                if (serviceLookup.ContainsKey(servicePrefix) && serviceClass.GetType().Assembly != Assembly.GetExecutingAssembly())
                    serviceLookup[servicePrefix] = serviceClass;
                else
                    serviceLookup[servicePrefix] = serviceClass;
            }

            // now we only have 1 per prefix, & with highest priority
            // instantiate them
            foreach (var serviceClass in serviceLookup.Values)
            {
                try
                {
                    var serviceInstObj = Activator.CreateInstance(serviceClass.Item1, true, null);
                    if (serviceInstObj == null)
                    {
                        Program.Logger.Log(LogLevel.Error, null, $"Instantiation of dapr service failed.",
                            logParams: new JObject() { ["service_type"] = serviceClass.Item1.Name });
                    }
                }
                catch (Exception e)
                {
                    Program.Logger.Log(LogLevel.Error, e, $"Instantiation of dapr service failed.",
                        logParams: new JObject() { ["service_type"] = serviceClass.Item1.Name });
                }
            }

            return webApp;
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
        public static void Log(this ILogger logger, LogLevel level, Exception? exc, string message, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
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
            var obsAttr = memberInfo.GetCustomAttribute<ObsoleteAttribute>();
            if (obsAttr != null)
            {
                message = obsAttr.Message;
                return true;
            }
            message = null;
            return false;
        }

        public static Type? GetFieldOrPropertyType(this MemberInfo memberInfo)
        {
            switch (memberInfo.MemberType)
            {
                case MemberTypes.Field:
                    if (memberInfo is FieldInfo fieldTypeData)
                        return fieldTypeData.FieldType;
                    break;
                case MemberTypes.Property:
                    if (memberInfo is PropertyInfo propTypeData)
                        return propTypeData.PropertyType;
                    break;
            }
            return null;
        }
    }
}
