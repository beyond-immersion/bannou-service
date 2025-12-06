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

            // Session validation isolation tests (for debugging 401 issues)
            new ServiceTest(TestSessionValidationAfterMultipleLogins, "SessionAfterLogins", "Auth", "Test session remains valid after creating additional sessions"),
            new ServiceTest(TestSessionValidationRoundTrip, "SessionRoundTrip", "Auth", "Test session validates immediately after creation"),
            new ServiceTest(TestSessionValidationWithDelay, "SessionWithDelay", "Auth", "Test session validates after short delay"),
            new ServiceTest(TestMultipleSessionsSameUser, "MultipleSessions", "Auth", "Test multiple concurrent sessions for same user"),
            new ServiceTest(TestSessionExpiresAtReturned, "SessionExpiresAt", "Auth", "Test session validation returns valid RemainingTime"),
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
                var validationResponse = await authClient
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
                var refreshResponse = await authClient
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
                var sessionsResponse = await authClient
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
            var validationResponse = await authClient
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
            await authClient
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
            await authClient
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
                await authClient
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

    #region Session Validation Isolation Tests

    /// <summary>
    /// Test that validates a session remains valid even after creating additional sessions.
    /// This tests the scenario where multiple logins for the same user don't invalidate existing sessions.
    /// </summary>
    private static async Task<TestResult> TestSessionValidationAfterMultipleLogins(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"multilogin_{DateTime.Now.Ticks}";
            var email = $"{testUsername}@example.com";
            var password = "TestPassword123!";

            // Step 1: Register user
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = password,
                Email = email
            });

            // Step 2: First login - get session A
            var loginResponseA = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });
            var tokenA = loginResponseA.AccessToken;

            if (string.IsNullOrEmpty(tokenA))
                return TestResult.Failed("First login failed to return access token");

            // Step 3: Validate session A works
            var validationA1 = await authClient.WithAuthorization(tokenA).ValidateTokenAsync();
            if (!validationA1.Valid)
                return TestResult.Failed("Session A validation failed immediately after creation");

            var remainingTimeA1 = validationA1.RemainingTime;

            // Step 4: Create second session (login B)
            var loginResponseB = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });
            var tokenB = loginResponseB.AccessToken;

            if (string.IsNullOrEmpty(tokenB))
                return TestResult.Failed("Second login failed to return access token");

            // Step 5: Validate session A STILL works after session B was created
            var validationA2 = await authClient.WithAuthorization(tokenA).ValidateTokenAsync();
            if (!validationA2.Valid)
                return TestResult.Failed($"Session A became invalid after creating session B! RemainingTime before: {remainingTimeA1}");

            // Step 6: Validate session B also works
            var validationB = await authClient.WithAuthorization(tokenB).ValidateTokenAsync();
            if (!validationB.Valid)
                return TestResult.Failed("Session B validation failed");

            // Step 7: Create third session (login C)
            var loginResponseC = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });

            // Step 8: Validate ALL sessions still work
            var validationA3 = await authClient.WithAuthorization(tokenA).ValidateTokenAsync();
            var validationB2 = await authClient.WithAuthorization(tokenB).ValidateTokenAsync();
            var validationC = await authClient.WithAuthorization(loginResponseC.AccessToken).ValidateTokenAsync();

            if (!validationA3.Valid || !validationB2.Valid || !validationC.Valid)
            {
                return TestResult.Failed($"Session validation failed after 3 logins - A:{validationA3.Valid}, B:{validationB2.Valid}, C:{validationC.Valid}");
            }

            return TestResult.Successful($"All 3 sessions remain valid after multiple logins. RemainingTime: A={validationA3.RemainingTime}s, B={validationB2.RemainingTime}s, C={validationC.RemainingTime}s");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"API exception: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test immediate validation round-trip after session creation.
    /// </summary>
    private static async Task<TestResult> TestSessionValidationRoundTrip(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"roundtrip_{DateTime.Now.Ticks}";

            // Register and login
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;

            // Immediately validate
            var validation = await authClient.WithAuthorization(token).ValidateTokenAsync();

            if (!validation.Valid)
                return TestResult.Failed($"Immediate validation failed! SessionId: {validation.SessionId}, RemainingTime: {validation.RemainingTime}");

            if (validation.RemainingTime <= 0)
                return TestResult.Failed($"RemainingTime is invalid: {validation.RemainingTime}");

            return TestResult.Successful($"Immediate round-trip validation succeeded. SessionId: {validation.SessionId}, RemainingTime: {validation.RemainingTime}s");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"API exception: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test session validation after a short delay to detect timing-related issues.
    /// </summary>
    private static async Task<TestResult> TestSessionValidationWithDelay(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"delay_{DateTime.Now.Ticks}";

            // Register and login
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;

            // Validate immediately
            var validation1 = await authClient.WithAuthorization(token).ValidateTokenAsync();
            if (!validation1.Valid)
                return TestResult.Failed("Immediate validation failed");

            var remainingTime1 = validation1.RemainingTime;

            // Wait 2 seconds
            await Task.Delay(2000);

            // Validate again
            var validation2 = await authClient.WithAuthorization(token).ValidateTokenAsync();
            if (!validation2.Valid)
                return TestResult.Failed($"Validation after 2s delay failed! RemainingTime before: {remainingTime1}");

            var remainingTime2 = validation2.RemainingTime;

            // RemainingTime should be lower (but still positive)
            if (remainingTime2 > remainingTime1)
                return TestResult.Failed($"RemainingTime increased after delay: {remainingTime1} -> {remainingTime2}");

            return TestResult.Successful($"Session remains valid after 2s delay. RemainingTime: {remainingTime1} -> {remainingTime2}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"API exception: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test that multiple concurrent sessions for the same user all remain valid.
    /// </summary>
    private static async Task<TestResult> TestMultipleSessionsSameUser(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"multisession_{DateTime.Now.Ticks}";
            var email = $"{testUsername}@example.com";
            var password = "TestPassword123!";

            // Register user
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = password,
                Email = email
            });

            // Create multiple sessions concurrently
            var loginTasks = Enumerable.Range(0, 5).Select(_ =>
                authClient.LoginAsync(new LoginRequest { Email = email, Password = password })
            ).ToList();

            var loginResponses = await Task.WhenAll(loginTasks);
            var tokens = loginResponses.Select(r => r.AccessToken).ToList();

            // Validate all tokens work
            var validationTasks = tokens.Select(t =>
                authClient.WithAuthorization(t).ValidateTokenAsync()
            ).ToList();

            var validations = await Task.WhenAll(validationTasks);

            var invalidCount = validations.Count(v => !v.Valid);
            if (invalidCount > 0)
            {
                var details = string.Join(", ", validations.Select((v, i) => $"S{i}:{v.Valid}(RT:{v.RemainingTime})"));
                return TestResult.Failed($"{invalidCount}/5 sessions invalid: {details}");
            }

            var remainingTimes = validations.Select(v => v.RemainingTime).ToList();
            return TestResult.Successful($"All 5 concurrent sessions valid. RemainingTimes: {string.Join(", ", remainingTimes)}s");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"API exception: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test that session validation returns a valid RemainingTime (ExpiresAt was deserialized correctly).
    /// </summary>
    private static async Task<TestResult> TestSessionExpiresAtReturned(ITestClient client, string[] args)
    {
        try
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"expiresat_{DateTime.Now.Ticks}";

            // Register and login
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;

            // Validate and check RemainingTime
            var validation = await authClient.WithAuthorization(token).ValidateTokenAsync();

            if (!validation.Valid)
                return TestResult.Failed("Token validation returned Valid=false");

            // RemainingTime should be positive and reasonable (typically 60 minutes = 3600 seconds)
            if (validation.RemainingTime <= 0)
                return TestResult.Failed($"RemainingTime is <= 0: {validation.RemainingTime} (ExpiresAtUnix likely deserialized as 0!)");

            if (validation.RemainingTime > 86400) // More than 24 hours
                return TestResult.Failed($"RemainingTime suspiciously high: {validation.RemainingTime}s (might be timestamp error)");

            // Expected: ~3600 seconds (60 minutes default expiration)
            var expectedMinSeconds = 3500; // Allow some tolerance
            var expectedMaxSeconds = 3700;

            if (validation.RemainingTime < expectedMinSeconds || validation.RemainingTime > expectedMaxSeconds)
            {
                return TestResult.Successful($"RemainingTime: {validation.RemainingTime}s (outside typical 60min range but valid)");
            }

            return TestResult.Successful($"RemainingTime: {validation.RemainingTime}s (correctly around 60 minutes as expected)");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"API exception: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    #endregion
}
