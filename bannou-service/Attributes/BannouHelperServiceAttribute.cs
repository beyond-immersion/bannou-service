using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Controls how PluginLoader registers a helper service in the DI container.
/// </summary>
public enum HelperRegistrationMode
{
    /// <summary>
    /// Standard interface → implementation registration.
    /// Requires <see cref="BannouHelperServiceAttribute.InterfaceType"/> to be set.
    /// PluginLoader registers: services.Add(ServiceDescriptor(InterfaceType, ConcreteType, Lifetime))
    /// </summary>
    Interface,

    /// <summary>
    /// Register as a hosted service (BackgroundService / IHostedService).
    /// <see cref="BannouHelperServiceAttribute.InterfaceType"/> is not used.
    /// PluginLoader registers via AddHostedService pattern.
    /// </summary>
    HostedService,

    /// <summary>
    /// Register as both a DI-injectable Singleton AND a hosted service.
    /// Used when a BackgroundService also needs to be injected into other services
    /// (e.g., RegistrationEventBatcher injected into PermissionService for Add() calls).
    /// PluginLoader registers: services.AddSingleton(ConcreteType) + AddHostedService(factory).
    /// </summary>
    SingletonAndHostedService
}

/// <summary>
/// Controls how PluginLoader registers the DI dependency for this helper service.
/// Orthogonal to <see cref="HelperRegistrationMode"/> which controls hosted service registration.
/// </summary>
public enum DependencyRegistrationMode
{
    /// <summary>
    /// Register interface → implementation only (default).
    /// Requires <see cref="BannouHelperServiceAttribute.InterfaceType"/> to be non-null.
    /// PluginLoader registers: services.Add(ServiceDescriptor(InterfaceType, ConcreteType, Lifetime))
    /// </summary>
    Interface,

    /// <summary>
    /// Register concrete type only (no interface mapping).
    /// <see cref="BannouHelperServiceAttribute.InterfaceType"/> is not used.
    /// PluginLoader registers: services.Add(ServiceDescriptor(ConcreteType, ConcreteType, Lifetime))
    /// </summary>
    Concrete,

    /// <summary>
    /// Register both: concrete type as Singleton for direct DI injection,
    /// AND interface → factory(ConcreteType) for interface-based resolution.
    /// Requires <see cref="BannouHelperServiceAttribute.InterfaceType"/> to be non-null.
    /// Used when a service needs to be injected both by concrete type (for direct access)
    /// and by interface (for IEnumerable&lt;T&gt; accumulation or polymorphic resolution).
    /// PluginLoader registers: services.AddSingleton(ConcreteType) +
    ///   services.AddSingleton(InterfaceType, sp =&gt; sp.GetRequiredService(ConcreteType))
    /// </summary>
    Both
}

/// <summary>
/// Attribute for helper/sub-services within a Bannou plugin assembly.
/// Helper services are discovered and registered in DI automatically,
/// just like primary services with <see cref="BannouServiceAttribute"/>.
/// They inherit their parent service's layer for hierarchy validation.
/// </summary>
/// <remarks>
/// <para>
/// Helper services are non-primary services that assist the main
/// <see cref="BannouServiceAttribute"/>-marked service in a plugin.
/// Examples: TokenService, SessionService (auth helpers),
/// ScaledTierCoordinator (voice helper), ActorRunner (actor helper).
/// </para>
/// <para>
/// Structural tests auto-discover helper services via this attribute
/// for constructor validation, hierarchy validation, and key builder checks.
/// </para>
/// <para>
/// <b>Registration modes</b> (controlled by <see cref="RegistrationMode"/>):
/// <list type="bullet">
/// <item><see cref="HelperRegistrationMode.Interface"/>: Standard interface→implementation (default, existing behavior)</item>
/// <item><see cref="HelperRegistrationMode.HostedService"/>: BackgroundService auto-registration via AddHostedService</item>
/// <item><see cref="HelperRegistrationMode.SingletonAndHostedService"/>: DI-injectable Singleton + hosted service factory</item>
/// </list>
/// </para>
/// </remarks>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class BannouHelperServiceAttribute : Attribute
{
    /// <summary>
    /// Name of the helper service (e.g., "token", "session", "mfa").
    /// Used for logging and diagnostics.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The interface type for DI registration (e.g., typeof(ITokenService)).
    /// If null, the concrete class type is used for registration and PluginLoader
    /// skips auto-registration for this type (requires manual registration in Plugin.cs).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WARNING — Multiple implementations of the same interface:</b> When two or more
    /// helper services specify the same <c>InterfaceType</c>, PluginLoader auto-registers
    /// ALL of them. .NET DI resolves <c>GetRequiredService&lt;T&gt;()</c> to the last
    /// registration, but which is "last" depends on <c>Assembly.GetTypes()</c> ordering
    /// (non-deterministic). Worse, if one implementation has dependencies that only exist
    /// in certain modes (e.g., <c>IChannelManager</c> only in RabbitMQ mode), resolving
    /// the interface in other modes will throw at runtime.
    /// </para>
    /// <para>
    /// For backend-conditional services (e.g., InMemoryMessageTap vs RabbitMQMessageTap),
    /// omit <c>InterfaceType</c> from BOTH attributes and register the correct implementation
    /// explicitly in each branch of <c>Plugin.ConfigureServices</c>.
    /// </para>
    /// </remarks>
    public Type? InterfaceType { get; }

    /// <summary>
    /// The parent service type that this helper belongs to.
    /// Must be a type marked with <see cref="BannouServiceAttribute"/>.
    /// The helper inherits the parent's <c>ServiceLayer</c> for hierarchy validation.
    /// </summary>
    public Type ParentServiceType { get; }

    /// <summary>
    /// How long the service instance lasts in the DI container.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Controls how PluginLoader registers this helper service in DI.
    /// Defaults to <see cref="HelperRegistrationMode.Interface"/> for backward compatibility.
    /// Set to <see cref="HelperRegistrationMode.HostedService"/> for BackgroundService workers,
    /// or <see cref="HelperRegistrationMode.SingletonAndHostedService"/> for workers that also
    /// need to be injected into other services.
    /// </summary>
    public HelperRegistrationMode RegistrationMode { get; set; } = HelperRegistrationMode.Interface;

    /// <summary>
    /// Controls how the DI dependency is registered (concrete type, interface, or both).
    /// Defaults to <see cref="DependencyRegistrationMode.Interface"/> (standard interface→implementation).
    /// Set to <see cref="DependencyRegistrationMode.Concrete"/> for helpers that only need
    /// concrete type injection. Set to <see cref="DependencyRegistrationMode.Both"/> for helpers
    /// that need both concrete type AND interface registration (e.g., action handlers registered
    /// as both their concrete type and IActionHandler for IEnumerable accumulation).
    /// Ignored when <see cref="RegistrationMode"/> is <see cref="HelperRegistrationMode.HostedService"/>.
    /// </summary>
    public DependencyRegistrationMode DependencyMode { get; set; } = DependencyRegistrationMode.Interface;

    /// <summary>
    /// Initializes a new instance of the BannouHelperServiceAttribute.
    /// </summary>
    /// <param name="name">Name of the helper service.</param>
    /// <param name="parentServiceType">The parent service type (must have [BannouService]).</param>
    /// <param name="interfaceType">The interface type for DI registration. Null uses the concrete type.</param>
    /// <param name="lifetime">DI lifetime (defaults to Scoped).</param>
    public BannouHelperServiceAttribute(
        string name,
        Type parentServiceType,
        Type? interfaceType = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        Name = name;
        ParentServiceType = parentServiceType ?? throw new ArgumentNullException(nameof(parentServiceType));
        InterfaceType = interfaceType;
        Lifetime = lifetime;
    }
}
