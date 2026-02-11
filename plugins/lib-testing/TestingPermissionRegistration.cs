#nullable enable

using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

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
    /// Generates the ServiceRegistrationEvent containing all endpoint permissions.
    /// </summary>
    /// <param name="instanceId">The unique instance GUID for this bannou instance</param>
    /// <param name="appId">The effective app ID for this service instance</param>
    public static ServiceRegistrationEvent CreateRegistrationEvent(Guid instanceId, string appId)
    {
        return new ServiceRegistrationEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ServiceId = instanceId,
            ServiceName = ServiceId,
            Version = ServiceVersion,
            AppId = appId,
            Endpoints = GetEndpoints()
        };
    }

    /// <summary>
    /// Gets the list of endpoints with their permission requirements.
    /// All testing endpoints are available to any authenticated user.
    /// </summary>
    public static ICollection<ServiceEndpoint> GetEndpoints()
    {
        var endpoints = new List<ServiceEndpoint>();

        // Health check - available to anyone
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/health",
            Method = ServiceEndpointMethod.GET,
            Description = "Testing service health check",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "anonymous",
                    RequiredStates = new Dictionary<string, string>()
                },
                new PermissionRequirement
                {
                    Role = "user",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        // Debug path endpoint - available to authenticated users
        // Note: Using POST because only POST endpoints are exposed in capability manifest
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/debug/path",
            Method = ServiceEndpointMethod.POST,
            Description = "Debug endpoint showing request path routing info",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "user",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        // Debug path with catch-all - available to authenticated users
        // Note: Using POST because only POST endpoints are exposed in capability manifest
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/debug/path/{catchAll}",
            Method = ServiceEndpointMethod.POST,
            Description = "Debug endpoint with catch-all path segment",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "user",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        // Run enabled tests - admin only
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/run-enabled",
            Method = ServiceEndpointMethod.GET,
            Description = "Run tests for enabled services",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "admin",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        // Run all tests - admin only
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/run",
            Method = ServiceEndpointMethod.GET,
            Description = "Run all infrastructure tests",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "admin",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        // Publish test event to a session - user role (for testing client event delivery)
        // Users can only send to sessions they know the ID of (which they receive in their capability manifest)
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/publish-test-event",
            Method = ServiceEndpointMethod.POST,
            Description = "Publish a test notification event to a WebSocket session",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "user",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        // Ping endpoint (GET) - available to any authenticated user for latency testing
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/ping",
            Method = ServiceEndpointMethod.GET,
            Description = "Ping endpoint for latency measurement (minimal request)",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "user",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        // Ping endpoint (POST) - available to any authenticated user for latency testing with client timestamp
        endpoints.Add(new ServiceEndpoint
        {
            Path = "/testing/ping",
            Method = ServiceEndpointMethod.POST,
            Description = "Ping endpoint for latency measurement (with client timestamp for RTT calculation)",
            Permissions = new List<PermissionRequirement>
            {
                new PermissionRequirement
                {
                    Role = "user",
                    RequiredStates = new Dictionary<string, string>()
                }
            }
        });

        return endpoints;
    }

    /// <summary>
    /// Builds the permission matrix for RegisterServicePermissionsAsync.
    /// Key structure: state -> role -> list of methods
    /// </summary>
    public static Dictionary<string, IDictionary<string, ICollection<string>>> BuildPermissionMatrix()
    {
        var matrix = new Dictionary<string, IDictionary<string, ICollection<string>>>();

        foreach (var endpoint in GetEndpoints())
        {
            var methodKey = endpoint.Path;

            foreach (var permission in endpoint.Permissions)
            {
                var stateKey = permission.RequiredStates.Count > 0
                    ? string.Join("|", permission.RequiredStates.Select(s =>
                        s.Key == ServiceId ? s.Value : $"{s.Key}:{s.Value}"))
                    : "default";

                if (!matrix.TryGetValue(stateKey, out var roleMap))
                {
                    roleMap = new Dictionary<string, ICollection<string>>();
                    matrix[stateKey] = roleMap;
                }

                if (!roleMap.TryGetValue(permission.Role, out var methods))
                {
                    methods = new List<string>();
                    roleMap[permission.Role] = methods;
                }

                if (!methods.Contains(methodKey))
                {
                    methods.Add(methodKey);
                }
            }
        }

        return matrix;
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
