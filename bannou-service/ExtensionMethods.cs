using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService;

public static partial class ExtensionMethods
{
    private static bool sProviderRegistered = false;

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

    public static string GetServiceName(this Type serviceType)
    {
        var serviceName = serviceType.Name;

        var serviceAttr = serviceType.GetCustomAttribute<DaprServiceAttribute>();
        if (serviceAttr != null && !string.IsNullOrWhiteSpace(serviceAttr.Name))
            serviceName = serviceAttr.Name;

        if (serviceName.EndsWith("Service", comparisonType: StringComparison.InvariantCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Service".Length, "Service".Length);

        if (serviceName.EndsWith("Controller", comparisonType: StringComparison.CurrentCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Controller".Length, "Controller".Length);

        if (serviceName.EndsWith("Dapr", comparisonType: StringComparison.CurrentCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Dapr".Length, "Dapr".Length);

        return serviceName;
    }

    /// <summary>
    /// Remove accent characters from a string.
    /// Returns new string.
    /// </summary>
    public static string RemoveAccent(this string txt)
    {
        if (!sProviderRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            sProviderRegistered = true;
        }

        var bytes = Encoding.GetEncoding("Cyrillic").GetBytes(txt);
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>
    /// Binds HTTP endpoints for all registered dapr services.
    /// </summary>
    public static void AddDaprServices(this IServiceCollection builder)
    {
        foreach ((Type, DaprServiceAttribute) serviceClassInfo in IDaprService.FindAll(enabledOnly: true))
        {
            var serviceType = serviceClassInfo.Item1;
            var serviceName = serviceType.GetServiceName();

            var serviceEnabled = IDaprService.IsEnabled(serviceType);
            if (!serviceEnabled)
            {
                Program.Logger?.Log(LogLevel.Debug, null, $"Service {serviceName} is disabled.");
                continue;
            }

            Program.Logger?.Log(LogLevel.Trace, null, $"Service {serviceName} is enabled.");
            builder.Add(new ServiceDescriptor(serviceType, serviceType, ServiceLifetime.Singleton));
        }
    }

    /// <summary>
    /// Binds HTTP endpoints for admin commands.
    /// </summary>
    public static IEndpointRouteBuilder MapNonServiceControllers(this IEndpointRouteBuilder builder)
    {
        return builder;
    }

    /// <summary>
    /// Binds HTTP endpoints for all registered dapr services.
    /// </summary>
    public static IEndpointRouteBuilder MapDaprServiceControllers(this IEndpointRouteBuilder builder)
    {
        foreach ((Type, DaprServiceAttribute) serviceClassInfo in IDaprService.FindAll(enabledOnly: true))
        {
            var serviceType = serviceClassInfo.Item1;
            var serviceName = serviceType.GetServiceName();

            var serviceEnabled = IDaprService.IsEnabled(serviceType);
            if (!serviceEnabled)
            {
                Program.Logger?.Log(LogLevel.Debug, null, $"Service {serviceName} is disabled.");
                continue;
            }

            Program.Logger?.Log(LogLevel.Trace, null, $"Service {serviceName} is enabled.");

            foreach ((Type, DaprControllerAttribute) controllerClassInfo in IDaprController.FindAll())
            {
                string? controllerName = controllerClassInfo.Item2?.Name ?? controllerClassInfo.Item2?.Template;
                if (string.IsNullOrWhiteSpace(controllerName))
                    controllerName = controllerClassInfo.Item1.GetServiceName();

                if (string.IsNullOrWhiteSpace(controllerName))
                    continue;

                Program.Logger?.Log(LogLevel.Trace, null, $"Activating service controller route {controllerName}/{{action}}/{{id}}.");
                builder.MapControllerRoute(
                    name: "ControllerActionIdApi",
                    pattern: controllerName + "/{action}/{id}");

                Program.Logger?.Log(LogLevel.Trace, null, $"Activating service controller route {controllerName}/{{action}}.");
                builder.MapControllerRoute(
                    name: "ControllerActionApi",
                    pattern: controllerName + "/{action}");
            }
        }

        return builder;
    }
}
