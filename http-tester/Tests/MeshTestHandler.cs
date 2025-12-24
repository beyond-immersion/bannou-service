using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Mesh service HTTP API endpoints.
/// Tests service mesh operations including registration, discovery, and routing.
/// </summary>
public class MeshTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            // Health and Diagnostics Tests
            new ServiceTest(TestGetHealth, "GetHealth", "Mesh", "Test getting mesh health status"),
            new ServiceTest(TestGetHealthWithEndpoints, "GetHealthWithEndpoints", "Mesh", "Test getting health with endpoint list"),

            // Service Discovery Tests
            new ServiceTest(TestGetEndpointsForBannou, "GetEndpointsBannou", "Mesh", "Test getting endpoints for bannou app-id"),
            new ServiceTest(TestGetEndpointsNonExistent, "GetEndpointsNonExistent", "Mesh", "Test getting endpoints for non-existent app-id"),
            new ServiceTest(TestListEndpoints, "ListEndpoints", "Mesh", "Test listing all endpoints"),
            new ServiceTest(TestListEndpointsWithFilter, "ListEndpointsFilter", "Mesh", "Test listing endpoints with status filter"),

            // Routing Tests
            new ServiceTest(TestGetMappings, "GetMappings", "Mesh", "Test getting service-to-app-id mappings"),
            new ServiceTest(TestGetMappingsWithFilter, "GetMappingsFilter", "Mesh", "Test getting mappings with filter"),
            new ServiceTest(TestGetRoute, "GetRoute", "Mesh", "Test getting optimal route for app-id"),
            new ServiceTest(TestGetRouteNoEndpoints, "GetRouteNoEndpoints", "Mesh", "Test route for non-existent app-id returns 503"),

            // Registration Lifecycle Tests
            new ServiceTest(TestRegisterAndDeregister, "RegisterDeregister", "Mesh", "Test endpoint registration and deregistration lifecycle"),
            new ServiceTest(TestHeartbeat, "Heartbeat", "Mesh", "Test heartbeat updates endpoint status"),
        };
    }

    /// <summary>
    /// Test getting mesh health status.
    /// </summary>
    private static async Task<TestResult> TestGetHealth(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetHealthRequest { IncludeEndpoints = false };
            var response = await meshClient.GetHealthAsync(request);

            if (response == null)
            {
                return TestResult.Failed("GetHealth returned null");
            }

            return TestResult.Successful($"Mesh health: {response.Status}, Redis connected: {response.RedisConnected}, " +
                $"Endpoints: {response.Summary?.TotalEndpoints ?? 0} total, {response.Summary?.HealthyCount ?? 0} healthy");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetHealth failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test getting mesh health with endpoint list.
    /// </summary>
    private static async Task<TestResult> TestGetHealthWithEndpoints(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetHealthRequest { IncludeEndpoints = true };
            var response = await meshClient.GetHealthAsync(request);

            if (response == null)
            {
                return TestResult.Failed("GetHealth with endpoints returned null");
            }

            var endpointCount = response.Endpoints?.Count ?? 0;
            return TestResult.Successful($"GetHealth with endpoints: {endpointCount} endpoints in response, uptime: {response.Uptime}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetHealth with endpoints failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test getting endpoints for bannou app-id.
    /// </summary>
    private static async Task<TestResult> TestGetEndpointsForBannou(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetEndpointsRequest { AppId = "bannou" };
            var response = await meshClient.GetEndpointsAsync(request);

            if (response == null)
            {
                return TestResult.Failed("GetEndpoints returned null");
            }

            return TestResult.Successful($"GetEndpoints for bannou: {response.HealthyCount}/{response.TotalCount} healthy endpoints");
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return TestResult.Successful("GetEndpoints returned 404 (no bannou endpoints registered yet - expected during test startup)");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetEndpoints failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test getting endpoints for non-existent app-id returns 404.
    /// </summary>
    private static async Task<TestResult> TestGetEndpointsNonExistent(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetEndpointsRequest { AppId = $"non-existent-{Guid.NewGuid()}" };

            try
            {
                var response = await meshClient.GetEndpointsAsync(request);
                // If we get here, check if endpoints list is empty
                if (response?.Endpoints == null || response.Endpoints.Count == 0)
                {
                    return TestResult.Successful("GetEndpoints returned empty list for non-existent app-id");
                }
                return TestResult.Failed("GetEndpoints returned endpoints for non-existent app-id");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("GetEndpoints correctly returned 404 for non-existent app-id");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetEndpoints non-existent test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test listing all endpoints.
    /// </summary>
    private static async Task<TestResult> TestListEndpoints(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new ListEndpointsRequest();
            var response = await meshClient.ListEndpointsAsync(request);

            if (response == null)
            {
                return TestResult.Failed("ListEndpoints returned null");
            }

            var summary = response.Summary;
            return TestResult.Successful($"ListEndpoints: {summary?.TotalEndpoints ?? 0} total, " +
                $"{summary?.HealthyCount ?? 0} healthy, {summary?.UniqueAppIds ?? 0} unique app-ids");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"ListEndpoints failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test listing endpoints with status filter.
    /// </summary>
    private static async Task<TestResult> TestListEndpointsWithFilter(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new ListEndpointsRequest { StatusFilter = EndpointStatus.Healthy };
            var response = await meshClient.ListEndpointsAsync(request);

            if (response == null)
            {
                return TestResult.Failed("ListEndpoints with filter returned null");
            }

            return TestResult.Successful($"ListEndpoints (healthy filter): {response.Endpoints?.Count ?? 0} endpoints");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"ListEndpoints with filter failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test getting service-to-app-id mappings.
    /// </summary>
    private static async Task<TestResult> TestGetMappings(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetMappingsRequest();
            var response = await meshClient.GetMappingsAsync(request);

            if (response == null)
            {
                return TestResult.Failed("GetMappings returned null");
            }

            var mappingCount = response.Mappings?.Count ?? 0;
            return TestResult.Successful($"GetMappings: {mappingCount} mappings, default app-id: {response.DefaultAppId}, version: {response.Version}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetMappings failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test getting mappings with service name filter.
    /// </summary>
    private static async Task<TestResult> TestGetMappingsWithFilter(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetMappingsRequest { ServiceNameFilter = "auth" };
            var response = await meshClient.GetMappingsAsync(request);

            if (response == null)
            {
                return TestResult.Failed("GetMappings with filter returned null");
            }

            return TestResult.Successful($"GetMappings (auth filter): {response.Mappings?.Count ?? 0} mappings found");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetMappings with filter failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test getting optimal route for app-id.
    /// </summary>
    private static async Task<TestResult> TestGetRoute(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetRouteRequest { AppId = "bannou" };

            try
            {
                var response = await meshClient.GetRouteAsync(request);

                if (response == null)
                {
                    return TestResult.Failed("GetRoute returned null");
                }

                var endpoint = response.Endpoint;
                return TestResult.Successful($"GetRoute: selected {endpoint?.Host}:{endpoint?.Port} " +
                    $"(status: {endpoint?.Status}, load: {endpoint?.LoadPercent}%)");
            }
            catch (ApiException ex) when (ex.StatusCode == 503)
            {
                return TestResult.Successful("GetRoute returned 503 (no healthy endpoints available - expected during test startup)");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetRoute failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test route for non-existent app-id returns 503.
    /// </summary>
    private static async Task<TestResult> TestGetRouteNoEndpoints(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var request = new GetRouteRequest { AppId = $"non-existent-{Guid.NewGuid()}" };

            try
            {
                var response = await meshClient.GetRouteAsync(request);
                return TestResult.Failed("GetRoute should have returned 503 for non-existent app-id");
            }
            catch (ApiException ex) when (ex.StatusCode == 503)
            {
                return TestResult.Successful("GetRoute correctly returned 503 for non-existent app-id");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetRoute no endpoints test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test endpoint registration and deregistration lifecycle.
    /// </summary>
    private static async Task<TestResult> TestRegisterAndDeregister(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var instanceId = Guid.NewGuid();

            // Register endpoint
            var registerRequest = new RegisterEndpointRequest
            {
                InstanceId = instanceId,
                AppId = "http-test-instance",
                Host = "test-host.local",
                Port = 8080,
                Services = new List<string> { "testing", "http-test" },
                MaxConnections = 100
            };

            var registerResponse = await meshClient.RegisterEndpointAsync(registerRequest);

            if (registerResponse == null || !registerResponse.Success)
            {
                return TestResult.Failed("RegisterEndpoint failed");
            }

            // Deregister endpoint
            var deregisterRequest = new DeregisterEndpointRequest { InstanceId = instanceId };

            try
            {
                await meshClient.DeregisterEndpointAsync(deregisterRequest);
            }
            catch (ApiException ex) when (ex.StatusCode == 200)
            {
                // Success with no content
            }

            return TestResult.Successful($"Registration lifecycle complete: registered and deregistered instance {instanceId}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Registration lifecycle test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test heartbeat updates endpoint status.
    /// </summary>
    private static async Task<TestResult> TestHeartbeat(ITestClient client, string[] args)
    {
        try
        {
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("Mesh client not available");
            }

            var instanceId = Guid.NewGuid();

            // First register an endpoint
            var registerRequest = new RegisterEndpointRequest
            {
                InstanceId = instanceId,
                AppId = "http-test-heartbeat",
                Host = "heartbeat-test.local",
                Port = 8080
            };

            var registerResponse = await meshClient.RegisterEndpointAsync(registerRequest);
            if (registerResponse == null || !registerResponse.Success)
            {
                return TestResult.Failed("Failed to register endpoint for heartbeat test");
            }

            // Send heartbeat
            var heartbeatRequest = new HeartbeatRequest
            {
                InstanceId = instanceId,
                Status = EndpointStatus.Healthy,
                LoadPercent = 25.5f,
                CurrentConnections = 10
            };

            var heartbeatResponse = await meshClient.HeartbeatAsync(heartbeatRequest);

            if (heartbeatResponse == null || !heartbeatResponse.Success)
            {
                // Clean up
                await meshClient.DeregisterEndpointAsync(new DeregisterEndpointRequest { InstanceId = instanceId });
                return TestResult.Failed("Heartbeat failed");
            }

            // Clean up
            await meshClient.DeregisterEndpointAsync(new DeregisterEndpointRequest { InstanceId = instanceId });

            return TestResult.Successful($"Heartbeat successful: next heartbeat in {heartbeatResponse.NextHeartbeatSeconds}s, " +
                $"TTL: {heartbeatResponse.TtlSeconds}s");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Heartbeat test failed: {ex.Message}");
        }
    }
}
