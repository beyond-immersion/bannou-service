using BeyondImmersion.BannouService.Testing;
using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// Test handler for authorization/authentication-related API endpoints
/// </summary>
public class AuthTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestLoginFlow, "LoginFlow", "Auth", "Test complete login flow"),
            new ServiceTest(TestTokenValidation, "TokenValidation", "Auth", "Test access token validation"),
        };
    }

    private static Task<TestResult> TestLoginFlow(ITestClient client, string[] args)
    {
        try
        {
            // The client should already be authenticated at this point
            if (!client.IsAuthenticated)
                return Task.FromResult(TestResult.Failed("Client is not authenticated"));

            return Task.FromResult(TestResult.Successful($"Login flow completed successfully using {client.TransportType}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TestResult.Failed($"Test exception: {ex.Message}", ex));
        }
    }

    private static async Task<TestResult> TestTokenValidation(ITestClient client, string[] args)
    {
        try
        {
            if (!client.IsAuthenticated)
                return TestResult.Failed("Client is not authenticated");

            // Make a simple authenticated request to verify token works
            var response = await client.PostAsync<JObject>("api/accounts/get", new { id = 1, includeClaims = false });

            // We expect this to either succeed (account exists) or return 404 (account doesn't exist)
            // What we don't want is 401 (unauthorized)
            if (response.StatusCode == 401)
                return TestResult.Failed("Token validation failed - received 401 Unauthorized");

            if (response.Success)
                return TestResult.Successful("Token validation passed - authenticated request succeeded");

            if (response.StatusCode == 404)
                return TestResult.Successful("Token validation passed - authenticated request returned expected 404");

            return TestResult.Successful($"Token validation passed - received status {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
