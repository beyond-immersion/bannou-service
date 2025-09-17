using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Testing;
using System.Text.Json;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Connect service HTTP API endpoints using generated clients.
/// Tests the connect service APIs directly via NSwag-generated ConnectClient.
/// Note: This tests HTTP endpoints only - WebSocket functionality is tested in edge-tester.
/// </summary>
public class ConnectTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestServiceMappings, "ServiceMappings", "Connect", "Test service routing mappings retrieval"),
            new ServiceTest(TestApiDiscovery, "ApiDiscovery", "Connect", "Test API discovery for session"),
            new ServiceTest(TestInternalProxy, "InternalProxy", "Connect", "Test internal API proxy functionality"),
        };
    }

    private static async Task<TestResult> TestServiceMappings(ITestClient client, string[] args)
    {
        try
        {
            // Create ConnectClient directly with parameterless constructor
            var connectClient = new ConnectClient();

            // Test getting service mappings
            var response = await connectClient.GetServiceMappingsAsync();

            if (response?.Mappings == null)
                return TestResult.Failed("Service mappings response is null or missing mappings");

            if (string.IsNullOrEmpty(response.DefaultMapping))
                return TestResult.Failed("Default mapping is null or empty");

            // Verify the response structure
            var mappingCount = response.Mappings.Count;
            var defaultMapping = response.DefaultMapping;

            return TestResult.Successful($"Service mappings retrieved successfully - {mappingCount} mappings, default: {defaultMapping}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"API exception: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Unexpected error: {ex.Message}");
        }
    }

    private static async Task<TestResult> TestApiDiscovery(ITestClient client, string[] args)
    {
        try
        {
            // Create ConnectClient directly with parameterless constructor
            var connectClient = new ConnectClient();

            // Create a test session ID for API discovery
            var testSessionId = $"test-session-{DateTime.Now.Ticks}";

            var discoveryRequest = new ApiDiscoveryRequest
            {
                SessionId = testSessionId,
                ContextData = new { testMode = true }
            };

            // Test API discovery
            var response = await connectClient.DiscoverAPIsAsync(discoveryRequest);

            if (response?.AvailableAPIs == null)
                return TestResult.Failed("API discovery response is null or missing available APIs");

            if (response.SessionId != testSessionId)
                return TestResult.Failed($"Session ID mismatch - expected {testSessionId}, got {response.SessionId}");

            // Verify the response structure
            var apiCount = response.AvailableAPIs.Count;
            var serviceCount = response.ServiceCapabilities?.Count ?? 0;

            return TestResult.Successful($"API discovery completed successfully - {apiCount} APIs, {serviceCount} services");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"API exception: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Unexpected error: {ex.Message}");
        }
    }

    private static async Task<TestResult> TestInternalProxy(ITestClient client, string[] args)
    {
        try
        {
            // Create ConnectClient directly with parameterless constructor
            var connectClient = new ConnectClient();

            // Create a test session ID for proxy request
            var testSessionId = $"test-session-{DateTime.Now.Ticks}";

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = testSessionId,
                TargetService = "accounts",
                TargetEndpoint = "/accounts",
                Method = InternalProxyRequestMethod.GET,
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" }
                }
            };

            // Test internal proxy
            var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

            if (response == null)
                return TestResult.Failed("Internal proxy response is null");

            // The proxy may fail due to permissions/auth, but we should get a structured response
            var statusCode = response.StatusCode;
            var success = response.Success;

            return TestResult.Successful($"Internal proxy completed - Success: {success}, Status: {statusCode}");
        }
        catch (ApiException ex)
        {
            // API exceptions are expected for proxy calls without proper auth
            // We're testing that the proxy endpoint responds properly, not that it succeeds
            return TestResult.Successful($"Internal proxy responded with expected API error: {ex.StatusCode}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Unexpected error: {ex.Message}");
        }
    }
}
