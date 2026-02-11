namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Push-based registry for service permission registration.
/// Services push their permission matrices TO this registry during startup.
/// Implemented by the Permission service (L1 AppFoundation).
/// </summary>
/// <remarks>
/// <para>
/// This replaces the event-based <c>permission.service-registered</c> pattern.
/// Instead of publishing events that the Permission service subscribes to,
/// services call <see cref="RegisterServiceAsync"/> directly via DI.
/// </para>
/// <para>
/// The registry is resolved from DI by PluginLoader during startup and passed
/// to each service's <see cref="IBannouService.RegisterServicePermissionsAsync(string, IPermissionRegistry?)"/>
/// method. Generated code in <c>*PermissionRegistration.cs</c> handles the call.
/// </para>
/// </remarks>
public interface IPermissionRegistry
{
    /// <summary>
    /// Registers a service's permission matrix with the Permission service.
    /// The matrix maps state keys to role-to-endpoint collections.
    /// </summary>
    /// <param name="serviceId">The service identifier (e.g., "account", "game-session")</param>
    /// <param name="version">The service version from the OpenAPI schema</param>
    /// <param name="permissionMatrix">
    /// Permission matrix: state -> role -> list of endpoint paths.
    /// State key "default" means no specific state required.
    /// </param>
    Task RegisterServiceAsync(
        string serviceId,
        string version,
        Dictionary<string, IDictionary<string, ICollection<string>>> permissionMatrix);
}
