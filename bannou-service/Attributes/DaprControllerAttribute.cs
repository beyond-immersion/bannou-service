using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for locating Dapr controller API classes for a given service.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class DaprControllerAttribute : RouteAttribute, IServiceAttribute
{
    /// <summary>
    /// Service type this controller is for.
    /// If it's independent of any services, leave null.
    /// If requiring multiple service, add more attributes.
    /// </summary>
    public Type? ServiceType { get; }
    public DaprServiceAttribute? ServiceAttribute { get; }

    public DaprControllerAttribute(Type? interfaceType = null)
        : this(GetControllerTemplate(interfaceType), interfaceType) { }

    public DaprControllerAttribute(string template, Type? interfaceType = null)
        : base(template)
    {
        if (interfaceType != null && !typeof(IDaprService).IsAssignableFrom(interfaceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");
    }

    public string GetControllerTemplate()
        => ServiceAttribute?.Name ?? "/";

    public static string GetControllerTemplate(Type? interfaceType)
    {
        if (interfaceType == null)
            return "/";

        var serviceAttribute = interfaceType.GetCustomAttribute<DaprServiceAttribute>();
        if (serviceAttribute != null)
            return serviceAttribute.Name;

        var serviceInfo = IDaprService.GetServiceInfo(interfaceType);
        if (serviceInfo != null && serviceInfo.HasValue)
            return serviceInfo.Value.Item3.Name;

        return "/";
    }
}
