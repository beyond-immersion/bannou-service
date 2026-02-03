using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for auto-loading Bannou service plugins.
/// Use {Name}_SERVICE_ENABLE as ENV or switch to enable/disable.
/// </summary>
/// <remarks>
/// <para>
/// <b>SCHEMA-FIRST:</b> This attribute is generated from the service's API schema.
/// The <see cref="Layer"/> property is set via <c>x-service-layer</c> in the schema.
/// </para>
/// </remarks>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class BannouServiceAttribute : BaseServiceAttribute
{
    /// <summary>
    /// IBannouService interface type.
    /// Must implement/inherit IBannouService.
    /// Can be an interface, abstract or concrete class.
    /// If null, will use exact class type.
    ///
    /// MUST match the service pulled from DI in related controllers.
    /// </summary>
    public Type? InterfaceType { get; }

    /// <summary>
    /// How long the service instance lasts.
    /// - Transient - One instance per service/reference.
    /// - Scoped - One instance per request.
    /// - Singleton - Only one instance, re-used each request.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Automatic priority over any other handlers.
    /// </summary>
    public bool Priority { get; }

    /// <summary>
    /// Name of the service.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The service hierarchy layer per SERVICE_HIERARCHY.md.
    /// Determines plugin load order and valid dependency directions.
    /// Set via <c>x-service-layer</c> in the service's API schema.
    /// </summary>
    public ServiceLayer Layer { get; }

    /// <summary>
    /// Initializes a new instance of the BannouServiceAttribute with the specified configuration.
    /// </summary>
    /// <param name="name">Name of the service.</param>
    /// <param name="interfaceType">The service interface type (must implement IBannouService).</param>
    /// <param name="priority">Whether this service has automatic priority over other handlers.</param>
    /// <param name="lifetime">How long the service instance lasts (Transient, Scoped, or Singleton).</param>
    /// <param name="layer">The service hierarchy layer (defaults to GameFeatures for backward compatibility).</param>
    public BannouServiceAttribute(
        string name,
        Type? interfaceType = null,
        bool priority = false,
        ServiceLifetime lifetime = ServiceLifetime.Singleton,
        ServiceLayer layer = ServiceLayer.GameFeatures)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        // Note: We don't require the interface type to implement IBannouService
        // because the interface is for registration, and the implementation class
        // will implement IBannouService. The validation should be on the implementation type,
        // not the interface type used for DI registration.

        Name = name;
        Priority = priority;
        InterfaceType = interfaceType;
        Lifetime = lifetime;
        Layer = layer;
    }
}
