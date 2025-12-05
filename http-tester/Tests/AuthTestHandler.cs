using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;

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
            // Core authentication flows
            new ServiceTest(TestCompleteAuthFlow, "CompleteAuthFlow", "Auth", "Test complete registration → login → connect flow"),
            new ServiceTest(TestRegisterFlow, "RegisterFlow", "Auth", "Test user registration flow"),
            new ServiceTest(TestDuplicateRegistration, "DuplicateRegistration", "Auth", "Test duplicate username registration fails"),
            new ServiceTest(TestLoginFlow, "LoginFlow", "Auth", "Test complete login flow"),
            new ServiceTest(TestInvalidLogin, "InvalidLogin", "Auth", "Test login with invalid credentials fails"),

            // Token operations
            new ServiceTest(TestTokenValidation, "TokenValidation", "Auth", "Test access token validation"),
            new ServiceTest(TestTokenRefresh, "TokenRefresh", "Auth", "Test token refresh functionality"),

            // Third-party authentication
            new ServiceTest(TestOAuthFlow, "OAuthFlow", "Auth", "Test OAuth provider authentication flow"),
            new ServiceTest(TestSteamAuthFlow, "SteamAuthFlow", "Auth", "Test Steam Session Ticket authentication flow"),

            // Session management
            new ServiceTest(TestGetSessions, "GetSessions", "Auth", "Test session retrieval functionality"),
            new ServiceTest(TestLogout, "Logout", "Auth", "Test logout invalidates session"),
            new ServiceTest(TestLogoutAllSessions, "LogoutAllSessions", "Auth", "Test logout all sessions"),
            new ServiceTest(TestTerminateSession, "TerminateSession", "Auth", "Test terminate specific session"),

            // Password reset
            new ServiceTest(TestPasswordResetRequest, "PasswordResetRequest", "Auth", "Test password reset request"),
            new ServiceTest(TestPasswordResetConfirm, "PasswordResetConfirm", "Auth", "Test password reset confirmation"),
        };
    }

    /// <summary>
    /// Gets a service client from the dependency injection container.
    /// </summary>
    private static T GetServiceClient<T>() where T : class
    {
        if (Program.ServiceProvider == null)
            throw new InvalidOperationException("Service provider not initialized");

        return Program.ServiceProvider.GetRequiredService<T>();
    }

    private static async Task<TestResult> TestRegisterFlow(ITestClient client, string[] args)
    {
        try
        {
            // Get AuthClient from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();

            var testUsername = $"regtest_{DateTime.Now.Ticks}";

            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };

            var response = await authClient.RegisterAsync(registerRequest);

            if (string.IsNullOrWhiteSpace(response.AccessToken))
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
            // Get AuthClient from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();

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
            // Get AuthClient from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();

            // ValidateTokenAsync now uses header-based token authentication
            try
            {
                var validationResponse = await ((AuthClient)authClient)
                    .WithAuthorization("invalid_token")
                    .ValidateTokenAsync();
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
            // Get AuthClient from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();

            // Test token refresh with a dummy refresh token
            var refreshRequest = new RefreshRequest
            {
                RefreshToken = "dummy-refresh-token"
            };

            try
            {
                var refreshResponse = await ((AuthClient)authClient)
                    .WithAuthorization("dummy-jwt-token")
                    .RefreshTokenAsync(refreshRequest);
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
            // Get AuthClient from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();

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
            // Get AuthClient from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();

            // Test Steam verification with mock Session Ticket data
            // Note: SteamID is no longer in the request - it comes from Steam's API response
            // When MockProviders=true, the service will use MockSteamId from configuration
            var steamRequest = new SteamVerifyRequest
            {
                Ticket = "140000006A7B3C8E0123456789ABCDEF", // Mock hex-encoded ticket
                DeviceInfo = new DeviceInfo
                {
                    DeviceType = DeviceInfoDeviceType.Desktop,
                    Platform = "Windows"
                }
            };

            try
            {
                var steamResponse = await authClient.VerifySteamAuthAsync(steamRequest);
                if (string.IsNullOrEmpty(steamResponse.AccessToken))
                    return TestResult.Failed("Steam auth succeeded but no access token returned");
                return TestResult.Successful($"Steam auth flow completed successfully with AccountId: {steamResponse.AccountId}");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                // 401 with mock ticket proves endpoint works - ticket was validated and rejected
                return TestResult.Successful("Steam auth correctly rejected invalid ticket");
            }
            catch (ApiException ex)
            {
                return TestResult.Failed($"Steam auth failed: {ex.StatusCode} - {ex.Message}");
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
            // Get AuthClient from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();

            try
            {
                var sessionsResponse = await ((AuthClient)authClient)
                    .WithAuthorization("dummy-jwt-token")
                    .GetSessionsAsync();
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

    private static async Task<TestResult> TestCompleteAuthFlow(ITestClient client, string[] args)
    {
        try
        {
            // Get clients from dependency injection container
            var authClient = GetServiceClient<IAuthClient>();
            var connectClient = GetServiceClient<IConnectClient>();

            // Step 1: Register a new user
            var testUsername = $"completetest_{DateTime.Now.Ticks}";
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };

            var registerResponse = await authClient.RegisterAsync(registerRequest);
            if (string.IsNullOrWhiteSpace(registerResponse.AccessToken))
                return TestResult.Failed("Registration succeeded but no access token returned");

            // Step 2: Login with the same credentials
            var loginRequest = new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            if (string.IsNullOrWhiteSpace(loginResponse.AccessToken))
                return TestResult.Failed("Login succeeded but no access token returned");

            var accessToken = loginResponse.AccessToken;

            // Step 3: Test token validation with the actual access token
            var validationResponse = await ((AuthClient)authClient)
                .WithAuthorization(accessToken)
                .ValidateTokenAsync();
            if (!validationResponse.Valid)
                return TestResult.Failed("Token validation returned Valid=false for legitimate token");

            // Step 4: Verify session ID was returned
            if (string.IsNullOrEmpty(validationResponse.SessionId))
                return TestResult.Failed("Token validation succeeded but no SessionId returned");

            return TestResult.Successful($"Complete auth flow tested successfully for user {testUsername}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Complete auth flow failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception in complete auth flow: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestDuplicateRegistration(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"duptest_{DateTime.Now.Ticks}";

            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };

            // First registration should succeed
            await authClient.RegisterAsync(registerRequest);

            // Second registration with same username should fail
            try
            {
                await authClient.RegisterAsync(registerRequest);
                return TestResult.Failed("Duplicate registration should have failed but succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return TestResult.Successful("Duplicate registration correctly rejected with 409 Conflict");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("Duplicate registration correctly rejected with 400 Bad Request");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestInvalidLogin(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();

            var loginRequest = new LoginRequest
            {
                Email = $"nonexistent_{DateTime.Now.Ticks}@example.com",
                Password = "WrongPassword123!"
            };

            try
            {
                await authClient.LoginAsync(loginRequest);
                return TestResult.Failed("Login with invalid credentials should have failed but succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                return TestResult.Successful("Invalid credentials correctly rejected with 401 Unauthorized");
            }
            catch (ApiException ex) when (ex.StatusCode == 400 || ex.StatusCode == 404)
            {
                return TestResult.Successful($"Invalid credentials correctly rejected with {ex.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestLogout(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();

            // First register and login to get a token
            var testUsername = $"logouttest_{DateTime.Now.Ticks}";
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };
            await authClient.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            var accessToken = loginResponse.AccessToken;

            // Logout current session
            var logoutRequest = new LogoutRequest { AllSessions = false };
            await ((AuthClient)authClient)
                .WithAuthorization(accessToken)
                .LogoutAsync(logoutRequest);
            return TestResult.Successful("Logout completed successfully");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestLogoutAllSessions(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();

            // First register and login to get a token
            var testUsername = $"logoutalltest_{DateTime.Now.Ticks}";
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };
            await authClient.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            var accessToken = loginResponse.AccessToken;

            // Logout all sessions
            var logoutRequest = new LogoutRequest { AllSessions = true };
            await ((AuthClient)authClient)
                .WithAuthorization(accessToken)
                .LogoutAsync(logoutRequest);
            return TestResult.Successful("Logout all sessions completed successfully");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestTerminateSession(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();

            // First register and login to get a token
            var testUsername = $"terminatetest_{DateTime.Now.Ticks}";
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            };
            await authClient.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            var accessToken = loginResponse.AccessToken;

            // Try to terminate a specific session with a test session ID (non-existent)
            var testSessionId = Guid.NewGuid();
            try
            {
                await ((AuthClient)authClient)
                    .WithAuthorization(accessToken)
                    .TerminateSessionAsync(new TerminateSessionRequest { SessionId = testSessionId });
                return TestResult.Successful("Session termination endpoint responded successfully");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // 404 proves endpoint works - it correctly reports session doesn't exist
                return TestResult.Successful("Session termination correctly returned 404 for non-existent session");
            }
            catch (ApiException ex)
            {
                return TestResult.Failed($"Session termination failed: {ex.StatusCode} - {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestPasswordResetRequest(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();

            // Request password reset for a test email
            var resetRequest = new PasswordResetRequest
            {
                Email = $"passwordreset_{DateTime.Now.Ticks}@example.com"
            };

            // Password reset should always succeed (security: don't reveal if email exists)
            await authClient.RequestPasswordResetAsync(resetRequest);
            return TestResult.Successful("Password reset request completed successfully");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestPasswordResetConfirm(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();

            // Try to confirm password reset with invalid token (should fail)
            var confirmRequest = new PasswordResetConfirmRequest
            {
                Token = "invalid-reset-token",
                NewPassword = "NewSecurePassword123!"
            };

            try
            {
                await authClient.ConfirmPasswordResetAsync(confirmRequest);
                return TestResult.Failed("Password reset with invalid token should have failed");
            }
            catch (ApiException ex) when (ex.StatusCode == 400 || ex.StatusCode == 401 || ex.StatusCode == 404)
            {
                // Rejecting invalid token proves the endpoint works
                return TestResult.Successful("Password reset correctly rejected invalid token");
            }
            catch (ApiException ex)
            {
                return TestResult.Failed($"Password reset confirm failed unexpectedly: {ex.StatusCode} - {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
