using BeyondImmersion.BannouService.Logging;
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
    /// Checks if a string is safe to use as a segment with a Path.Combine().
    /// </summary>
    public static bool IsSafeForPath(this string pathSegment)
    {
        if (string.IsNullOrEmpty(pathSegment))
            return false;

        if (pathSegment.Any(ch => Path.GetInvalidFileNameChars().Contains(ch)))
            return false;

        if (pathSegment.Contains(".."))
            return false;

        if (Path.IsPathRooted(pathSegment))
            return false;

        return true;
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

    public static string? GetServiceName(this Type serviceType)
    {
        DaprServiceAttribute? serviceAttr = serviceType.GetCustomAttributes<DaprServiceAttribute>().FirstOrDefault();
        if (serviceAttr != null && !string.IsNullOrWhiteSpace(serviceAttr.Name))
            return serviceAttr.Name;

        var serviceInfo = IDaprService.GetServiceInfo(serviceType);
        if (serviceInfo != null && serviceInfo.HasValue)
            return serviceInfo.Value.Item3.Name;

        return null;
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
        foreach (var serviceInfo in IDaprService.EnabledServices)
        {
            Type interfaceType = serviceInfo.Item1;
            Type implementationType = serviceInfo.Item2;
            ServiceLifetime serviceLifetime = serviceInfo.Item3.Lifetime;
            var serviceName = implementationType.GetServiceName();

            Program.Logger?.Log(LogLevel.Trace, null, $"Service {serviceName} has been enabled to handle type {interfaceType}.");

            builder.Add(new ServiceDescriptor(interfaceType, implementationType, serviceLifetime));
        }
    }

    /// <summary>
    /// Returns whether the configuration is provided for a given service type to run properly.
    /// </summary>
    public static bool HasRequiredConfiguration(this Type implementationType)
        => IDaprService.HasRequiredConfiguration(implementationType);

    /// <summary>
    /// Returns the best service configuration type for the given service type.
    /// Returned type is based on DaprServiceAttribute service target type.
    /// </summary>
    public static Type GetConfigurationType(this Type implementationType)
        => IDaprService.GetConfigurationType(implementationType);

    /// <summary>
    /// Binds HTTP endpoints for admin commands.
    /// </summary>
    public static IEndpointRouteBuilder MapNonServiceControllers(this IEndpointRouteBuilder builder)
    {
        return builder;
    }

    /// <summary>
    /// Binds HTTP endpoints for all registered dapr service handlers.
    /// </summary>
    public static IEndpointRouteBuilder MapDaprServiceControllers(this IEndpointRouteBuilder builder)
    {
        foreach (var serviceInfo in IDaprService.EnabledServices)
        {
            Type interfaceType = serviceInfo.Item1;
            Type implementationType = serviceInfo.Item2;
            var serviceName = implementationType.GetServiceName();

            foreach ((Type, DaprControllerAttribute) controllerClassInfo in IDaprController.FindForImplementation(interfaceType))
            {
                var controllerName = controllerClassInfo.Item2?.Name ?? controllerClassInfo.Item2?.Template;
                if (string.IsNullOrWhiteSpace(controllerName))
                    controllerName = controllerClassInfo.Item1.GetServiceName();

                if (string.IsNullOrWhiteSpace(controllerName))
                    continue;

                Program.Logger?.Log(LogLevel.Trace, null, $"Activating service controller route {controllerName}/{{action}}/{{id}}.");
                _ = builder.MapControllerRoute(
                    name: "ControllerActionIdApi",
                    pattern: controllerName + "/{action}/{id}");

                Program.Logger?.Log(LogLevel.Trace, null, $"Activating service controller route {controllerName}/{{action}}.");
                _ = builder.MapControllerRoute(
                    name: "ControllerActionApi",
                    pattern: controllerName + "/{action}");
            }
        }

        return builder;
    }
}
