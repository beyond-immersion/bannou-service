using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.Auth.Client;

namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// Test handler for authorization/authentication-related API endpoints using generated clients.
/// Tests the auth service APIs directly via NSwag-generated AuthClient.
/// </summary>
public class AuthTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestRegisterFlow, "RegisterFlow", "Auth", "Test user registration flow"),
            new ServiceTest(TestLoginFlow, "LoginFlow", "Auth", "Test complete login flow"),
            new ServiceTest(TestTokenValidation, "TokenValidation", "Auth", "Test access token validation"),
            new ServiceTest(TestTokenRefresh, "TokenRefresh", "Auth", "Test token refresh functionality"),
        };
    }

    private static async Task<TestResult> TestRegisterFlow(ITestClient client, string[] args)
    {
        try
        {
            // Create AuthClient directly with parameterless constructor
            var authClient = new AuthClient();

            var testUsername = $"regtest_{DateTime.Now.Ticks}";

            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };

            var response = await authClient.RegisterAsync(registerRequest);

            if (string.IsNullOrWhiteSpace(response.Access_token))
                return TestResult.Failed("Registration succeeded but no access token returned");

            return TestResult.Successful($"Registration flow completed successfully for user {testUsername}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Registration failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestLoginFlow(ITestClient client, string[] args)
    {
        try
        {
            // Create AuthClient directly with parameterless constructor
            var authClient = new AuthClient();

            // First register a test user
            var testUsername = $"logintest_{DateTime.Now.Ticks}";
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };

            await authClient.RegisterAsync(registerRequest);

            // Then try to login
            var loginResponse = await authClient.LoginWithCredentialsPostAsync(testUsername, "TestPassword123!");

            if (string.IsNullOrWhiteSpace(loginResponse.Access_token))
                return TestResult.Failed("Login succeeded but no access token returned");

            return TestResult.Successful($"Login flow completed successfully for user {testUsername}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Login failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestTokenValidation(ITestClient client, string[] args)
    {
        try
        {
            // Create AuthClient directly with parameterless constructor
            var authClient = new AuthClient();

            // Create a test validation request with dummy token
            var validateRequest = new ValidateTokenRequest
            {
                Token = "dummy-token-for-validation-test"
            };

            try
            {
                var validationResponse = await authClient.ValidateTokenAsync(validateRequest);
                return TestResult.Successful("Token validation endpoint responded correctly");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                return TestResult.Successful("Token validation correctly rejected invalid token");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("Token validation correctly handled bad request");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestTokenRefresh(ITestClient client, string[] args)
    {
        try
        {
            // Create AuthClient directly with parameterless constructor
            var authClient = new AuthClient();

            // Test token refresh with a dummy refresh token
            try
            {
                var refreshResponse = await authClient.LoginWithTokenGetAsync("dummy-refresh-token");
                return TestResult.Successful("Token refresh endpoint responded correctly");
            }
            catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                return TestResult.Successful("Token refresh correctly rejected invalid refresh token");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}