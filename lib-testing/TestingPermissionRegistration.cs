#nullable enable

using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Manual permission registration for Testing service.
/// Unlike other services, lib-testing doesn't have a schema file and generated code.
/// This class manually registers the testing endpoints with the permissions system
/// so that BannouClient (via WebSocket) can receive GUIDs for these endpoints.
///
/// This follows the same pattern as the generated *PermissionRegistration.Generated.cs
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
    public static ServiceRegistrationEvent CreateRegistrationEvent()
    {
        return new ServiceRegistrationEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            ServiceId = ServiceId,
            Version = ServiceVersion,
            // DAPR_APP_ID is a legitimate Tenet 21 exception - Dapr bootstrap variable
            AppId = Environment.GetEnvironmentVariable("DAPR_APP_ID") ?? AppConstants.DEFAULT_APP_NAME,
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
    /// Registers service permissions via event publishing.
    /// Should only be called after Dapr connectivity is confirmed.
    /// </summary>
    public static async Task RegisterViaEventAsync(DaprClient daprClient, ILogger? logger = null)
    {
        try
        {
            var registrationEvent = CreateRegistrationEvent();

            await daprClient.PublishEventAsync(
                "bannou-pubsub",
                "permissions.service-registered",
                registrationEvent);

            logger?.LogInformation(
                "Published service registration event for {ServiceId} v{Version} with {EndpointCount} endpoints",
                ServiceId, ServiceVersion, registrationEvent.Endpoints.Count);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to publish service registration event for {ServiceId}", ServiceId);
            throw;
        }
    }
}
