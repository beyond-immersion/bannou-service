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

    public DaprControllerAttribute(Type? serviceType = null)
        : this(GetControllerTemplate(serviceType), serviceType) { }

    public DaprControllerAttribute(string template, Type? serviceType = null)
        : base(template)
    {
        if (serviceType != null && !typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        ServiceType = serviceType;
        if (serviceType != null)
            Name = serviceType.Name;

        ServiceAttribute = serviceType?.GetCustomAttribute<DaprServiceAttribute>();
        if (ServiceAttribute != null)
            Name = ServiceAttribute.Name;
    }

    public string GetControllerTemplate()
        => ServiceAttribute?.Name ?? "/";

    public static string GetControllerTemplate(Type? serviceType)
    {
        var serviceAttribute = serviceType?.GetCustomAttribute<DaprServiceAttribute>();
        if (serviceAttribute != null)
            return serviceAttribute.Name;

        return "/";
    }
}
