using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Mesh service HTTP API endpoints.
/// Tests service mesh operations including registration, discovery, and routing.
/// </summary>
public class MeshTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
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

        // Mesh Invocation Client Tests (replacement for direct service invocation)
        new ServiceTest(TestInvocationClientAvailable, "InvocationClientDI", "Mesh", "Test IMeshInvocationClient is available via DI"),
        new ServiceTest(TestInvocationCreateRequest, "InvocationCreateRequest", "Mesh", "Test CreateInvokeMethodRequest works correctly"),
        new ServiceTest(TestInvocationServiceAvailability, "InvocationServiceCheck", "Mesh", "Test IsServiceAvailableAsync for registered endpoints"),
    ];

    private static async Task<TestResult> TestGetHealth(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new GetHealthRequest { IncludeEndpoints = false };
            var response = await meshClient.GetHealthAsync(request);

            if (response == null)
                return TestResult.Failed("GetHealth returned null");

            return TestResult.Successful($"Mesh health: {response.Status}, Redis connected: {response.RedisConnected}, " +
                $"Endpoints: {response.Summary?.TotalEndpoints ?? 0} total, {response.Summary?.HealthyCount ?? 0} healthy");
        }, "Get mesh health");

    private static async Task<TestResult> TestGetHealthWithEndpoints(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new GetHealthRequest { IncludeEndpoints = true };
            var response = await meshClient.GetHealthAsync(request);

            if (response == null)
                return TestResult.Failed("GetHealth with endpoints returned null");

            var endpointCount = response.Endpoints?.Count ?? 0;
            return TestResult.Successful($"GetHealth with endpoints: {endpointCount} endpoints in response, uptime: {response.Uptime}");
        }, "Get mesh health with endpoints");

    private static async Task<TestResult> TestGetEndpointsForBannou(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new GetEndpointsRequest { AppId = "bannou" };
            var response = await meshClient.GetEndpointsAsync(request);

            if (response == null)
                return TestResult.Failed("GetEndpoints returned null");

            if (response.TotalCount == 0)
                return TestResult.Failed("GetEndpoints returned 0 endpoints for bannou - mesh not registering endpoints");

            return TestResult.Successful($"GetEndpoints for bannou: {response.HealthyCount}/{response.TotalCount} healthy endpoints");
        }, "Get endpoints for bannou");

    private static async Task<TestResult> TestGetEndpointsNonExistent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

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
        }, "Get endpoints for non-existent app-id");

    private static async Task<TestResult> TestListEndpoints(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new ListEndpointsRequest();
            var response = await meshClient.ListEndpointsAsync(request);

            if (response == null)
                return TestResult.Failed("ListEndpoints returned null");

            var summary = response.Summary;
            return TestResult.Successful($"ListEndpoints: {summary?.TotalEndpoints ?? 0} total, " +
                $"{summary?.HealthyCount ?? 0} healthy, {summary?.UniqueAppIds ?? 0} unique app-ids");
        }, "List all endpoints");

    private static async Task<TestResult> TestListEndpointsWithFilter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new ListEndpointsRequest { StatusFilter = EndpointStatus.Healthy };
            var response = await meshClient.ListEndpointsAsync(request);

            if (response == null)
                return TestResult.Failed("ListEndpoints with filter returned null");

            return TestResult.Successful($"ListEndpoints (healthy filter): {response.Endpoints?.Count ?? 0} endpoints");
        }, "List endpoints with status filter");

    private static async Task<TestResult> TestGetMappings(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new GetMappingsRequest();
            var response = await meshClient.GetMappingsAsync(request);

            if (response == null)
                return TestResult.Failed("GetMappings returned null");

            var mappingCount = response.Mappings?.Count ?? 0;
            return TestResult.Successful($"GetMappings: {mappingCount} mappings, default app-id: {response.DefaultAppId}, version: {response.Version}");
        }, "Get service-to-app-id mappings");

    private static async Task<TestResult> TestGetMappingsWithFilter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new GetMappingsRequest { ServiceNameFilter = "auth" };
            var response = await meshClient.GetMappingsAsync(request);

            if (response == null)
                return TestResult.Failed("GetMappings with filter returned null");

            return TestResult.Successful($"GetMappings (auth filter): {response.Mappings?.Count ?? 0} mappings found");
        }, "Get mappings with filter");

    private static async Task<TestResult> TestGetRoute(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new GetRouteRequest { AppId = "bannou" };
            var response = await meshClient.GetRouteAsync(request);

            if (response == null)
                return TestResult.Failed("GetRoute returned null");

            var endpoint = response.Endpoint;
            if (endpoint == null)
                return TestResult.Failed("GetRoute returned no endpoint for bannou - mesh not functioning");

            return TestResult.Successful($"GetRoute: selected {endpoint.Host}:{endpoint.Port} " +
                $"(status: {endpoint.Status}, load: {endpoint.LoadPercent}%)");
        }, "Get optimal route");

    private static async Task<TestResult> TestGetRouteNoEndpoints(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var request = new GetRouteRequest { AppId = $"non-existent-{Guid.NewGuid()}" };

            try
            {
                await meshClient.GetRouteAsync(request);
                return TestResult.Failed("GetRoute should have returned 404 for non-existent app-id");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("GetRoute correctly returned 404 for non-existent app-id");
            }
        }, "Get route for non-existent app-id");

    private static async Task<TestResult> TestRegisterAndDeregister(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

            var instanceId = Guid.NewGuid();

            // Register endpoint
            var registerRequest = new RegisterEndpointRequest
            {
                InstanceId = instanceId,
                AppId = "http-test-instance",
                Host = "test-host.local",
                Port = 8080,
                Services = ["testing", "http-test"],
                MaxConnections = 100
            };

            var registerResponse = await meshClient.RegisterEndpointAsync(registerRequest);

            // Validate response structure (RegisterEndpointResponse has endpoint required)
            if (registerResponse == null)
                return TestResult.Failed("RegisterEndpoint returned null response");

            if (registerResponse.Endpoint == null)
                return TestResult.Failed("RegisterEndpoint did not return endpoint details");

            if (registerResponse.Endpoint.InstanceId != instanceId)
                return TestResult.Failed($"Instance ID mismatch: expected {instanceId}, got {registerResponse.Endpoint.InstanceId}");

            // Deregister endpoint (returns 204 NoContent on success)
            var deregisterRequest = new DeregisterEndpointRequest { InstanceId = instanceId };
            await meshClient.DeregisterEndpointAsync(deregisterRequest);

            return TestResult.Successful($"Registration lifecycle complete: registered and deregistered instance {instanceId}");
        }, "Register and deregister endpoint");

    private static async Task<TestResult> TestHeartbeat(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var meshClient = GetServiceClient<IMeshClient>();

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
            // Success is implied by getting a response without exception
            if (registerResponse == null)
                return TestResult.Failed("Failed to register endpoint for heartbeat test");

            // Send heartbeat
            var heartbeatRequest = new HeartbeatRequest
            {
                InstanceId = instanceId,
                Status = EndpointStatus.Healthy,
                LoadPercent = 25.5f,
                CurrentConnections = 10
            };

            var heartbeatResponse = await meshClient.HeartbeatAsync(heartbeatRequest);

            // Success is implied by getting a response without exception
            if (heartbeatResponse == null)
            {
                // Clean up
                await meshClient.DeregisterEndpointAsync(new DeregisterEndpointRequest { InstanceId = instanceId });
                return TestResult.Failed("Heartbeat failed");
            }

            // Clean up
            await meshClient.DeregisterEndpointAsync(new DeregisterEndpointRequest { InstanceId = instanceId });

            return TestResult.Successful($"Heartbeat successful: next heartbeat in {heartbeatResponse.NextHeartbeatSeconds}s, " +
                $"TTL: {heartbeatResponse.TtlSeconds}s");
        }, "Heartbeat test");

    private static async Task<TestResult> TestInvocationClientAvailable(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            await Task.CompletedTask;
            var invocationClient = GetServiceClient<IMeshInvocationClient>();
            return TestResult.Successful("IMeshInvocationClient is available via DI (mesh replacement ready)");
        }, "Check IMeshInvocationClient DI");

    private static async Task<TestResult> TestInvocationCreateRequest(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            await Task.CompletedTask;
            var invocationClient = GetServiceClient<IMeshInvocationClient>();

            // Test GET request creation
            var getRequest = invocationClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", "auth/status");
            if (getRequest == null)
                return TestResult.Failed("CreateInvokeMethodRequest returned null for GET");
            if (getRequest.Method != HttpMethod.Get)
                return TestResult.Failed($"Expected GET method, got {getRequest.Method}");

            // Test POST request creation with typed body
            var postRequest = invocationClient.CreateInvokeMethodRequest(
                HttpMethod.Post,
                "bannou",
                "testing/echo",
                new { Message = "test", Value = 42 });
            if (postRequest == null)
                return TestResult.Failed("CreateInvokeMethodRequest returned null for POST");
            if (postRequest.Content == null)
                return TestResult.Failed("POST request missing content body");
            if (postRequest.Content.Headers.ContentType?.MediaType != "application/json")
                return TestResult.Failed($"Expected application/json content type, got {postRequest.Content.Headers.ContentType?.MediaType}");

            return TestResult.Successful("CreateInvokeMethodRequest creates properly configured requests (GET, POST with JSON body)");
        }, "Test CreateInvokeMethodRequest");

    private static async Task<TestResult> TestInvocationServiceAvailability(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var invocationClient = GetServiceClient<IMeshInvocationClient>();
            var meshClient = GetServiceClient<IMeshClient>();

            var testInstanceId = Guid.NewGuid();
            var testAppId = $"invocation-test-{Guid.NewGuid():N}";

            // Register a test endpoint
            var registerResponse = await meshClient.RegisterEndpointAsync(new RegisterEndpointRequest
            {
                InstanceId = testInstanceId,
                AppId = testAppId,
                Host = "test-host.local",
                Port = 8080,
                Services = ["testing"]
            });

            // Success is implied by getting a response without exception
            if (registerResponse == null)
                return TestResult.Failed("Failed to register test endpoint");

            try
            {
                // Check that the registered endpoint is available
                var isAvailable = await invocationClient.IsServiceAvailableAsync(testAppId, CancellationToken.None);
                if (!isAvailable)
                    return TestResult.Failed($"IsServiceAvailableAsync returned false for registered app-id {testAppId}");

                // Check that a non-existent app-id returns false
                var nonExistentAvailable = await invocationClient.IsServiceAvailableAsync(
                    $"definitely-does-not-exist-{Guid.NewGuid():N}",
                    CancellationToken.None);
                if (nonExistentAvailable)
                    return TestResult.Failed("IsServiceAvailableAsync returned true for non-existent app-id");

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
        }, "Test IsServiceAvailableAsync");
}
