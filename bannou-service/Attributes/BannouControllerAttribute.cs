using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for locating Bannou controller API classes for a given service.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class BannouControllerAttribute : RouteAttribute, IServiceAttribute
{
    /// <summary>
    /// Service type this controller is for.
    /// If it's independent of any services, leave null.
    /// If requiring multiple service, add more attributes.
    /// </summary>
    public Type? InterfaceType { get; }

    /// <summary>
    /// Initializes a new instance of the BannouControllerAttribute with the specified interface type.
    /// </summary>
    /// <param name="interfaceType">The service interface type this controller is for.</param>
    public BannouControllerAttribute(Type? interfaceType = null)
        : this(GetControllerTemplate(interfaceType), interfaceType) { }

    /// <summary>
    /// Initializes a new instance of the BannouControllerAttribute with a custom template and interface type.
    /// </summary>
    /// <param name="template">The custom route template for this controller.</param>
    /// <param name="interfaceType">The service interface type this controller is for.</param>
    public BannouControllerAttribute(string template, Type? interfaceType = null)
        : base(template ?? GetControllerTemplate(interfaceType))
    {
        if (interfaceType != null && !typeof(IBannouService).IsAssignableFrom(interfaceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IBannouService)}");

        InterfaceType = interfaceType;
    }

    /// <summary>
    /// Gets the controller route template for the specified interface type.
    /// </summary>
    /// <param name="interfaceType">The service interface type to get the template for.</param>
    /// <returns>The route template string for the controller.</returns>
    public static string GetControllerTemplate(Type? interfaceType)
    {
        if (interfaceType == null)
            return "/";

        var serviceInfo = IBannouService.GetServiceInfo(interfaceType);
        if (serviceInfo != null && serviceInfo.HasValue)
            return serviceInfo.Value.Item3.Name;

        return "/";
    }
}
