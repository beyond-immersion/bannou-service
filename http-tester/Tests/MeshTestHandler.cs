using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
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
            new ServiceTest(TestGetRouteNoEndpoints, "GetRouteNoEndpoints", "Mesh", "Test route for non-existent app-id returns 404"),

            // Registration Lifecycle Tests
            new ServiceTest(TestRegisterAndDeregister, "RegisterDeregister", "Mesh", "Test endpoint registration and deregistration lifecycle"),
            new ServiceTest(TestHeartbeat, "Heartbeat", "Mesh", "Test heartbeat updates endpoint status"),

            // Mesh Invocation Client Tests (replacement for Dapr InvokeMethodAsync)
            new ServiceTest(TestInvocationClientAvailable, "InvocationClientDI", "Mesh", "Test IMeshInvocationClient is available via DI"),
            new ServiceTest(TestInvocationCreateRequest, "InvocationCreateRequest", "Mesh", "Test CreateInvokeMethodRequest works correctly"),
            new ServiceTest(TestInvocationServiceAvailability, "InvocationServiceCheck", "Mesh", "Test IsServiceAvailableAsync for registered endpoints"),
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
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("GetRoute returned 404 (no healthy endpoints available - expected during test startup)");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"GetRoute failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test route for non-existent app-id returns 404.
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
                return TestResult.Failed("GetRoute should have returned 404 for non-existent app-id");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("GetRoute correctly returned 404 for non-existent app-id");
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

            // Deregister endpoint (returns 204 NoContent on success)
            var deregisterRequest = new DeregisterEndpointRequest { InstanceId = instanceId };
            await meshClient.DeregisterEndpointAsync(deregisterRequest);

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

    /// <summary>
    /// Test that IMeshInvocationClient is available via DI.
    /// This is the Dapr replacement client for service-to-service communication.
    /// </summary>
    private static async Task<TestResult> TestInvocationClientAvailable(ITestClient client, string[] args)
    {
        try
        {
            var invocationClient = Program.ServiceProvider?.GetRequiredService<IMeshInvocationClient>();
            if (invocationClient == null)
            {
                return TestResult.Failed("IMeshInvocationClient not registered in DI");
            }

            return TestResult.Successful("IMeshInvocationClient is available via DI (Dapr replacement ready)");
        }
        catch (InvalidOperationException ex)
        {
            return TestResult.Failed($"IMeshInvocationClient not registered: {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"InvocationClient DI test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test CreateInvokeMethodRequest creates properly configured HTTP requests.
    /// This method is equivalent to DaprClient.CreateInvokeMethodRequest.
    /// </summary>
    private static async Task<TestResult> TestInvocationCreateRequest(ITestClient client, string[] args)
    {
        try
        {
            var invocationClient = Program.ServiceProvider?.GetRequiredService<IMeshInvocationClient>();
            if (invocationClient == null)
            {
                return TestResult.Failed("IMeshInvocationClient not available");
            }

            // Test GET request creation
            var getRequest = invocationClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", "auth/status");
            if (getRequest == null)
            {
                return TestResult.Failed("CreateInvokeMethodRequest returned null for GET");
            }
            if (getRequest.Method != HttpMethod.Get)
            {
                return TestResult.Failed($"Expected GET method, got {getRequest.Method}");
            }

            // Test POST request creation with typed body
            var postRequest = invocationClient.CreateInvokeMethodRequest(
                HttpMethod.Post,
                "bannou",
                "testing/echo",
                new { Message = "test", Value = 42 });
            if (postRequest == null)
            {
                return TestResult.Failed("CreateInvokeMethodRequest returned null for POST");
            }
            if (postRequest.Content == null)
            {
                return TestResult.Failed("POST request missing content body");
            }
            if (postRequest.Content.Headers.ContentType?.MediaType != "application/json")
            {
                return TestResult.Failed($"Expected application/json content type, got {postRequest.Content.Headers.ContentType?.MediaType}");
            }

            return TestResult.Successful("CreateInvokeMethodRequest creates properly configured requests (GET, POST with JSON body)");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"InvocationCreateRequest test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test IsServiceAvailableAsync checks endpoint availability correctly.
    /// This is useful for health checks before invoking services.
    /// </summary>
    private static async Task<TestResult> TestInvocationServiceAvailability(ITestClient client, string[] args)
    {
        try
        {
            var invocationClient = Program.ServiceProvider?.GetRequiredService<IMeshInvocationClient>();
            if (invocationClient == null)
            {
                return TestResult.Failed("IMeshInvocationClient not available");
            }

            // First register an endpoint so we have something to find
            var meshClient = Program.ServiceProvider?.GetRequiredService<IMeshClient>();
            if (meshClient == null)
            {
                return TestResult.Failed("IMeshClient not available");
            }

            var testInstanceId = Guid.NewGuid();
            var testAppId = $"invocation-test-{Guid.NewGuid():N}";

            // Register a test endpoint
            var registerResponse = await meshClient.RegisterEndpointAsync(new RegisterEndpointRequest
            {
                InstanceId = testInstanceId,
                AppId = testAppId,
                Host = "test-host.local",
                Port = 8080,
                Services = new List<string> { "testing" }
            });

            if (registerResponse == null || !registerResponse.Success)
            {
                return TestResult.Failed("Failed to register test endpoint");
            }

            try
            {
                // Check that the registered endpoint is available
                var isAvailable = await invocationClient.IsServiceAvailableAsync(testAppId, CancellationToken.None);
                if (!isAvailable)
                {
                    return TestResult.Failed($"IsServiceAvailableAsync returned false for registered app-id {testAppId}");
                }

                // Check that a non-existent app-id returns false
                var nonExistentAvailable = await invocationClient.IsServiceAvailableAsync(
                    $"definitely-does-not-exist-{Guid.NewGuid():N}",
                    CancellationToken.None);
                if (nonExistentAvailable)
                {
                    return TestResult.Failed("IsServiceAvailableAsync returned true for non-existent app-id");
                }

                return TestResult.Successful($"IsServiceAvailableAsync correctly reports availability (registered={isAvailable}, non-existent={nonExistentAvailable})");
            }
            finally
            {
                // Clean up: deregister the test endpoint
                try
                {
                    await meshClient.DeregisterEndpointAsync(new DeregisterEndpointRequest { InstanceId = testInstanceId });
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"InvocationServiceAvailability test failed: {ex.Message}");
        }
    }
}
