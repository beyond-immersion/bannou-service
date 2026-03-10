using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Attributes;

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
    /// If null, the concrete class type is used for registration.
    /// </summary>
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
