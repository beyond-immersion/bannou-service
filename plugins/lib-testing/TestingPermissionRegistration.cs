#nullable enable

using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Manual permission registration for Testing service.
/// Unlike other services, lib-testing doesn't have a schema file and generated code.
/// This class manually registers the testing endpoints with the permissions system
/// so that BannouClient (via WebSocket) can receive GUIDs for these endpoints.
///
/// This follows the same pattern as the generated *PermissionRegistration.cs
/// files but is manually maintained since there's no testing-api.yaml schema.
/// </summary>
public static class TestingPermissionRegistration
{
    /// <summary>
    /// Service ID for permission registration.
    /// </summary>
    public const string ServiceId = "testing";

    /// <summary>
    /// Service version.
    /// </summary>
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// Builds the permission matrix for RegisterServicePermissionsAsync.
    /// Key structure: state -> role -> list of endpoint paths.
    /// All testing endpoints use the "default" state (no specific state required).
    /// </summary>
    public static Dictionary<string, IDictionary<string, ICollection<string>>> BuildPermissionMatrix()
    {
        return new Dictionary<string, IDictionary<string, ICollection<string>>>
        {
            ["default"] = new Dictionary<string, ICollection<string>>
            {
                ["anonymous"] = new List<string>
                {
                    "/testing/health"
                },
                ["user"] = new List<string>
                {
                    "/testing/health",
                    "/testing/debug/path",
                    "/testing/debug/path/{catchAll}",
                    "/testing/publish-test-event",
                    "/testing/ping"
                },
                ["admin"] = new List<string>
                {
                    "/testing/run-enabled",
                    "/testing/run"
                }
            }
        };
    }
}

/// <summary>
/// Partial class overlay: registers Testing service permissions via DI registry.
/// Manually maintained since lib-testing has no API schema for code generation.
/// Push-based: this service pushes its permission matrix TO the IPermissionRegistry.
/// </summary>
public partial class TestingService
{
    /// <summary>
    /// Registers this service's permissions with the Permission service via DI registry.
    /// Called by PluginLoader during startup with the resolved IPermissionRegistry.
    /// </summary>
    async Task IBannouService.RegisterServicePermissionsAsync(
        string appId, IPermissionRegistry? registry)
    {
        if (registry != null)
        {
            await registry.RegisterServiceAsync(
                TestingPermissionRegistration.ServiceId,
                TestingPermissionRegistration.ServiceVersion,
                TestingPermissionRegistration.BuildPermissionMatrix());
        }
    }
}
