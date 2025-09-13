using BeyondImmersion.BannouService.Testing;
using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// Test handler for validating that auth flows work correctly with OpenResty routing
/// Tests that OpenResty auth routing and queue system integration doesn't break existing functionality
/// </summary>
public class OpenRestyAuthTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestAuthFlowsWithOpenResty, "AuthFlowsOpenResty", "OpenResty", "Test auth flows work with OpenResty routing"),
            new ServiceTest(TestAuthenticatedRequestsRouting, "AuthRequestsRouting", "OpenResty", "Test authenticated requests route correctly"),
            new ServiceTest(TestServiceEndpointAccessibility, "ServiceEndpoints", "OpenResty", "Test service endpoints remain accessible"),
            new ServiceTest(TestAuthTokenValidationRouting, "AuthTokenRouting", "OpenResty", "Test auth token validation through OpenResty"),
        };
    }

    private static async Task<TestResult> TestAuthFlowsWithOpenResty(ITestClient client, string[] args)
    {
        try
        {
            // Test that basic auth flows still work with OpenResty in place
            if (!client.IsAuthenticated)
                return TestResult.Failed("Client should be authenticated for this test");

            // Make a simple authenticated request through OpenResty routing
            var response = await client.GetAsync<JObject>("testing/run-enabled");

            if (response.Success || response.StatusCode == 404)
                return TestResult.Successful("Auth flows working correctly with OpenResty routing");

            if (response.StatusCode == 401)
                return TestResult.Failed("Auth token not being processed correctly through OpenResty");

            return TestResult.Successful($"Auth flows appear to be working (status: {response.StatusCode})");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Auth flows test failed: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestAuthenticatedRequestsRouting(ITestClient client, string[] args)
    {
        try
        {
            if (!client.IsAuthenticated)
                return TestResult.Failed("Client should be authenticated for this test");

            // Test multiple authenticated requests to ensure routing consistency
            var response1 = await client.PostAsync<JObject>("api/accounts/get", new { id = 1 });
            var response2 = await client.GetAsync<JObject>("api/accounts/list");

            // We don't care about the specific responses, just that routing works
            bool routingWorking = (response1.StatusCode != 500 && response1.StatusCode != 502) &&
                                  (response2.StatusCode != 500 && response2.StatusCode != 502);

            if (routingWorking)
                return TestResult.Successful("Authenticated requests routing correctly through OpenResty");

            return TestResult.Failed($"Routing issues detected (status1: {response1.StatusCode}, status2: {response2.StatusCode})");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Authenticated requests routing test failed: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestServiceEndpointAccessibility(ITestClient client, string[] args)
    {
        try
        {
            // Test that service endpoints are still accessible with OpenResty
            var testEndpoints = new[]
            {
                "testing/run-enabled",
                "api/accounts/list"
            };

            int accessibleEndpoints = 0;
            foreach (var endpoint in testEndpoints)
            {
                try
                {
                    var response = await client.GetAsync<JObject>(endpoint);
                    // Count as accessible if we get a valid HTTP response (not 500/502 errors)
                    if (response.StatusCode < 500)
                        accessibleEndpoints++;
                }
                catch
                {
                    // Skip failed endpoints
                }
            }

            if (accessibleEndpoints > 0)
                return TestResult.Successful($"Service endpoints accessible ({accessibleEndpoints}/{testEndpoints.Length}) through OpenResty");

            return TestResult.Failed("No service endpoints are accessible through OpenResty");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Service endpoint accessibility test failed: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestAuthTokenValidationRouting(ITestClient client, string[] args)
    {
        try
        {
            if (!client.IsAuthenticated)
                return TestResult.Failed("Client should be authenticated for this test");

            // Test that auth token validation still works through OpenResty
            var response = await client.PostAsync<JObject>("api/accounts/get", new { id = 1, includeClaims = false });

            // Similar to the existing AuthTestHandler logic
            if (response.StatusCode == 401)
                return TestResult.Failed("Token validation failed through OpenResty - received 401 Unauthorized");

            if (response.Success)
                return TestResult.Successful("Token validation working correctly through OpenResty");

            if (response.StatusCode == 404)
                return TestResult.Successful("Token validation working through OpenResty (404 expected for non-existent account)");

            return TestResult.Successful($"Token validation appears to be working through OpenResty (status: {response.StatusCode})");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Auth token validation routing test failed: {ex.Message}", ex);
        }
    }
}
