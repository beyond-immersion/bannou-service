using System;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for marking methods as service mapping event handlers.
/// Provides metadata for automatic registration with the service mapping event system.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ServiceMappingEventAttribute : Attribute
{
    /// <summary>
    /// The action this handler responds to (register, update, unregister, or * for all).
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Optional service name filter. If specified, only handles events for this service.
    /// Use "*" or null to handle events for all services.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Priority for handler execution when multiple handlers match (lower = higher priority).
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this handler should run asynchronously with other handlers.
    /// </summary>
    public bool RunAsync { get; set; } = false;

    /// <summary>
    /// Creates a new service mapping event handler attribute.
    /// </summary>
    /// <param name="action">The action this handler responds to (register, update, unregister, or *)</param>
    public ServiceMappingEventAttribute(string action = "*")
    {
        Action = action ?? "*";
    }
}

/// <summary>
/// Attribute for marking classes that contain service mapping event handlers.
/// Provides metadata for automatic discovery and registration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ServiceMappingHandlerAttribute : Attribute
{
    /// <summary>
    /// Human-readable name for this handler collection.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Description of what this handler collection does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether handlers in this class should be automatically registered.
    /// </summary>
    public bool AutoRegister { get; set; } = true;

    /// <summary>
    /// Creates a new service mapping handler attribute.
    /// </summary>
    /// <param name="name">Human-readable name for this handler collection</param>
    public ServiceMappingHandlerAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}
