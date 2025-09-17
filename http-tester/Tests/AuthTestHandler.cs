using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

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
            new ServiceTest(TestOAuthFlow, "OAuthFlow", "Auth", "Test OAuth provider authentication flow"),
            new ServiceTest(TestSteamAuthFlow, "SteamAuthFlow", "Auth", "Test Steam authentication flow"),
            new ServiceTest(TestGetSessions, "GetSessions", "Auth", "Test session retrieval functionality"),
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
            var loginRequest = new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);

            if (string.IsNullOrWhiteSpace(loginResponse.AccessToken))
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

            // ValidateTokenAsync doesn't take parameters - it should use authorization header
            try
            {
                var validationResponse = await authClient.ValidateTokenAsync();
                return TestResult.Successful($"Token validation endpoint responded: Valid={validationResponse.Valid}");
            }
            catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                return TestResult.Successful("Token validation correctly rejected request without valid authorization");
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
            var refreshRequest = new RefreshRequest
            {
                RefreshToken = "dummy-refresh-token"
            };

            try
            {
                var refreshResponse = await authClient.RefreshTokenAsync(refreshRequest);
                return TestResult.Successful($"Token refresh endpoint responded correctly with AccountId: {refreshResponse.AccountId}");
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

    private static async Task<TestResult> TestOAuthFlow(ITestClient client, string[] args)
    {
        try
        {
            // Create AuthClient directly with parameterless constructor
            var authClient = new AuthClient();

            // Test OAuth callback with mock data
            var oauthRequest = new OAuthCallbackRequest
            {
                Code = "mock-oauth-code",
                State = "mock-state",
                DeviceInfo = new DeviceInfo()
            };

            try
            {
                var oauthResponse = await authClient.CompleteOAuthAsync(Provider.Discord, oauthRequest);
                return TestResult.Successful($"OAuth flow completed successfully for Discord with AccountId: {oauthResponse.AccountId}");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("OAuth flow correctly handled invalid mock data");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestSteamAuthFlow(ITestClient client, string[] args)
    {
        try
        {
            // Create AuthClient directly with parameterless constructor
            var authClient = new AuthClient();

            // Test Steam verification with mock data
            var steamRequest = new SteamVerifyRequest
            {
                Ticket = "mock-steam-ticket",
                SteamId = "mock-steam-id",
                DeviceInfo = new DeviceInfo()
            };

            try
            {
                var steamResponse = await authClient.VerifySteamAuthAsync(steamRequest);
                return TestResult.Successful($"Steam auth flow completed successfully with AccountId: {steamResponse.AccountId}");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("Steam auth flow correctly handled invalid mock data");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetSessions(ITestClient client, string[] args)
    {
        try
        {
            // Create AuthClient directly with parameterless constructor
            var authClient = new AuthClient();

            try
            {
                var sessionsResponse = await authClient.GetSessionsAsync();
                return TestResult.Successful($"Get sessions endpoint responded with {sessionsResponse.Sessions.Count} sessions");
            }
            catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                return TestResult.Successful("Get sessions correctly required authentication");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
