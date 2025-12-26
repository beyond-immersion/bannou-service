using Microsoft.AspNetCore.Mvc;

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
    public Type? InterfaceType { get; }

    /// <summary>
    /// Initializes a new instance of the DaprControllerAttribute with the specified interface type.
    /// </summary>
    /// <param name="interfaceType">The service interface type this controller is for.</param>
    [Obsolete]
    public DaprControllerAttribute(Type? interfaceType = null)
        : this(GetControllerTemplate(interfaceType), interfaceType) { }

    /// <summary>
    /// Initializes a new instance of the DaprControllerAttribute with a custom template and interface type.
    /// </summary>
    /// <param name="template">The custom route template for this controller.</param>
    /// <param name="interfaceType">The service interface type this controller is for.</param>
    [Obsolete]
    public DaprControllerAttribute(string template, Type? interfaceType = null)
        : base(template ?? GetControllerTemplate(interfaceType))
    {
        if (interfaceType != null && !typeof(IDaprService).IsAssignableFrom(interfaceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        InterfaceType = interfaceType;
    }

    /// <summary>
    /// Gets the controller route template for the specified interface type.
    /// </summary>
    /// <param name="interfaceType">The service interface type to get the template for.</param>
    /// <returns>The route template string for the controller.</returns>
    [Obsolete]
    public static string GetControllerTemplate(Type? interfaceType)
    {
        if (interfaceType == null)
            return "/";

        var serviceInfo = IDaprService.GetServiceInfo(interfaceType);
        if (serviceInfo != null && serviceInfo.HasValue)
            return serviceInfo.Value.Item3.Name;

        return "/";
    }
}
