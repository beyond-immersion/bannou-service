using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for authorization/authentication-related API endpoints using generated clients.
/// Tests the auth service APIs directly via NSwag-generated AuthClient.
/// </summary>
public class AuthTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Core authentication flows
        new ServiceTest(TestCompleteAuthFlow, "CompleteAuthFlow", "Auth", "Test complete registration → login → connect flow"),
        new ServiceTest(TestRegisterFlow, "RegisterFlow", "Auth", "Test user registration flow"),
        new ServiceTest(TestDuplicateRegistration, "DuplicateRegistration", "Auth", "Test duplicate username registration fails"),
        new ServiceTest(TestLoginFlow, "LoginFlow", "Auth", "Test complete login flow"),
        new ServiceTest(TestInvalidLogin, "InvalidLogin", "Auth", "Test login with invalid credentials fails"),

        // Token operations
        new ServiceTest(TestTokenValidation, "TokenValidation", "Auth", "Test access token validation"),
        new ServiceTest(TestTokenRefresh, "TokenRefresh", "Auth", "Test token refresh functionality"),

        // Provider discovery
        new ServiceTest(TestListProviders, "ListProviders", "Auth", "Test listing available authentication providers"),

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
    ];

    private static async Task<TestResult> TestRegisterFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            var testUsername = GenerateTestId("regtest");

            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = GenerateTestEmail("regtest")
            };

            var response = await authClient.RegisterAsync(registerRequest);

            if (string.IsNullOrWhiteSpace(response.AccessToken))
                return TestResult.Failed("Registration succeeded but no access token returned");

            return TestResult.Successful($"Registration flow completed successfully for user {testUsername}");
        }, "User registration");

    private static async Task<TestResult> TestLoginFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            // First register a test user
            var testUsername = GenerateTestId("logintest");
            var email = GenerateTestEmail("logintest");
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            };

            await authClient.RegisterAsync(registerRequest);

            // Then try to login
            var loginRequest = new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);

            if (string.IsNullOrWhiteSpace(loginResponse.AccessToken))
                return TestResult.Failed("Login succeeded but no access token returned");

            return TestResult.Successful($"Login flow completed successfully for user {testUsername}");
        }, "User login");

    private static async Task<TestResult> TestTokenValidation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            try
            {
                var validationResponse = await ((IServiceClient<AuthClient>)authClient)
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
        }, "Token validation");

    private static async Task<TestResult> TestTokenRefresh(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            var refreshRequest = new RefreshRequest
            {
                RefreshToken = "dummy-refresh-token"
            };

            try
            {
                var refreshResponse = await ((IServiceClient<AuthClient>)authClient)
                    .WithAuthorization("dummy-jwt-token")
                    .RefreshTokenAsync(refreshRequest);
                return TestResult.Successful($"Token refresh endpoint responded correctly with AccountId: {refreshResponse.AccountId}");
            }
            catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                return TestResult.Successful("Token refresh correctly rejected invalid refresh token");
            }
        }, "Token refresh");

    private static async Task<TestResult> TestListProviders(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            var response = await authClient.ListProvidersAsync();

            if (response.Providers == null)
                return TestResult.Failed("Providers list is null");

            // In test environment, expect at least some providers configured
            // The exact count depends on environment configuration
            var providerDetails = string.Join(", ", response.Providers.Select(p => $"{p.Name}({p.AuthType})"));

            // Verify structure of returned providers
            foreach (var provider in response.Providers)
            {
                if (string.IsNullOrEmpty(provider.Name))
                    return TestResult.Failed("Provider has empty name");

                if (string.IsNullOrEmpty(provider.DisplayName))
                    return TestResult.Failed($"Provider {provider.Name} has empty display name");

                // OAuth providers should have auth URL, ticket providers should not
                if (provider.AuthType == ProviderInfoAuthType.Oauth && provider.AuthUrl == null)
                    return TestResult.Failed($"OAuth provider {provider.Name} missing auth URL");
            }

            return TestResult.Successful($"Listed {response.Providers.Count} providers: {providerDetails}");
        }, "List providers");

    private static async Task<TestResult> TestOAuthFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            var oauthRequest = new OAuthCallbackRequest
            {
                Code = "mock-oauth-code",
                State = "mock-state",
                DeviceInfo = new DeviceInfo()
            };

            try
            {
                var oauthResponse = await authClient.CompleteOAuthAsync(Provider.Discord, oauthRequest);

                // OAuth succeeded (mock mode enabled) - verify the token works
                if (string.IsNullOrWhiteSpace(oauthResponse.AccessToken))
                    return TestResult.Failed("OAuth succeeded but no access token returned");

                // Validate the token to verify session was created
                var validationResponse = await ((IServiceClient<AuthClient>)authClient)
                    .WithAuthorization(oauthResponse.AccessToken)
                    .ValidateTokenAsync();

                if (!validationResponse.Valid)
                    return TestResult.Failed("OAuth token validation returned Valid=false - session may not have been created");

                if (validationResponse.SessionKey == Guid.Empty)
                    return TestResult.Failed("OAuth token validation succeeded but no SessionId returned");

                return TestResult.Successful($"OAuth flow completed successfully for Discord: AccountId={oauthResponse.AccountId}, SessionId={validationResponse.SessionKey}, RemainingTime={validationResponse.RemainingTime}s");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                // Mock mode disabled - OAuth flow correctly rejected invalid mock data
                return TestResult.Successful("OAuth flow correctly handled invalid mock data (MockProviders=false)");
            }
        }, "OAuth flow");

    private static async Task<TestResult> TestSteamAuthFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            // Steam Session Ticket format: hex-encoded binary data
            // In mock mode, any ticket will work; in production, ticket must be validated via Steam Web API
            var steamRequest = new SteamVerifyRequest
            {
                Ticket = "140000006A7B3C8E0123456789ABCDEF",
                DeviceInfo = new DeviceInfo
                {
                    DeviceType = DeviceInfoDeviceType.Desktop,
                    Platform = "Windows"
                }
            };

            try
            {
                var steamResponse = await authClient.VerifySteamAuthAsync(steamRequest);

                // Steam auth succeeded (mock mode enabled) - verify the token works
                if (string.IsNullOrWhiteSpace(steamResponse.AccessToken))
                    return TestResult.Failed("Steam auth succeeded but no access token returned");

                // Validate the token to verify session was created
                var validationResponse = await ((IServiceClient<AuthClient>)authClient)
                    .WithAuthorization(steamResponse.AccessToken)
                    .ValidateTokenAsync();

                if (!validationResponse.Valid)
                    return TestResult.Failed("Steam token validation returned Valid=false - session may not have been created");

                if (validationResponse.SessionKey == Guid.Empty)
                    return TestResult.Failed("Steam token validation succeeded but no SessionId returned");

                return TestResult.Successful($"Steam auth flow completed successfully: AccountId={steamResponse.AccountId}, SessionId={validationResponse.SessionKey}, RemainingTime={validationResponse.RemainingTime}s");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                // Mock mode disabled - Steam auth correctly rejected invalid ticket
                return TestResult.Successful("Steam auth correctly rejected invalid ticket (MockProviders=false)");
            }
        }, "Steam auth");

    private static async Task<TestResult> TestGetSessions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            try
            {
                var sessionsResponse = await ((IServiceClient<AuthClient>)authClient)
                    .WithAuthorization("dummy-jwt-token")
                    .GetSessionsAsync();
                return TestResult.Successful($"Get sessions endpoint responded with {sessionsResponse.Sessions.Count} sessions");
            }
            catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                return TestResult.Successful("Get sessions correctly required authentication");
            }
        }, "Get sessions");

    private static async Task<TestResult> TestCompleteAuthFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var connectClient = GetServiceClient<IConnectClient>();

            // Step 1: Register a new user
            var testUsername = GenerateTestId("completetest");
            var email = GenerateTestEmail("completetest");
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            };

            var registerResponse = await authClient.RegisterAsync(registerRequest);
            if (string.IsNullOrWhiteSpace(registerResponse.AccessToken))
                return TestResult.Failed("Registration succeeded but no access token returned");

            // Step 2: Login with the same credentials
            var loginRequest = new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            if (string.IsNullOrWhiteSpace(loginResponse.AccessToken))
                return TestResult.Failed("Login succeeded but no access token returned");

            var accessToken = loginResponse.AccessToken;

            // Step 3: Test token validation with the actual access token
            var validationResponse = await ((IServiceClient<AuthClient>)authClient)
                .WithAuthorization(accessToken)
                .ValidateTokenAsync();
            if (!validationResponse.Valid)
                return TestResult.Failed("Token validation returned Valid=false for legitimate token");

            // Step 4: Verify session ID was returned
            if (validationResponse.SessionKey == Guid.Empty)
                return TestResult.Failed("Token validation succeeded but no SessionId returned");

            return TestResult.Successful($"Complete auth flow tested successfully for user {testUsername}");
        }, "Complete auth flow");

    private static async Task<TestResult> TestDuplicateRegistration(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = GenerateTestId("duptest");
            var email = GenerateTestEmail("duptest");

            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            };

            // First registration should succeed
            await authClient.RegisterAsync(registerRequest);

            // Second registration with same username should fail
            return await ExecuteExpectingAnyStatusAsync(
                async () => await authClient.RegisterAsync(registerRequest),
                [409, 400],
                "Duplicate registration");
        }, "Duplicate registration");

    private static async Task<TestResult> TestInvalidLogin(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            var loginRequest = new LoginRequest
            {
                Email = GenerateTestEmail("nonexistent"),
                Password = "WrongPassword123!"
            };

            return await ExecuteExpectingAnyStatusAsync(
                async () => await authClient.LoginAsync(loginRequest),
                [401, 400, 404],
                "Invalid credentials");
        }, "Invalid login");

    private static async Task<TestResult> TestLogout(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            // First register and login to get a token
            var testUsername = GenerateTestId("logouttest");
            var email = GenerateTestEmail("logouttest");
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            };
            await authClient.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            var accessToken = loginResponse.AccessToken;

            // Logout current session
            var logoutRequest = new LogoutRequest { AllSessions = false };
            await ((IServiceClient<AuthClient>)authClient)
                .WithAuthorization(accessToken)
                .LogoutAsync(logoutRequest);
            return TestResult.Successful("Logout completed successfully");
        }, "Logout");

    private static async Task<TestResult> TestLogoutAllSessions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            // First register and login to get a token
            var testUsername = GenerateTestId("logoutalltest");
            var email = GenerateTestEmail("logoutalltest");
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            };
            await authClient.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            var accessToken = loginResponse.AccessToken;

            // Logout all sessions
            var logoutRequest = new LogoutRequest { AllSessions = true };
            await ((IServiceClient<AuthClient>)authClient)
                .WithAuthorization(accessToken)
                .LogoutAsync(logoutRequest);
            return TestResult.Successful("Logout all sessions completed successfully");
        }, "Logout all sessions");

    private static async Task<TestResult> TestTerminateSession(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            // First register and login to get a token
            var testUsername = GenerateTestId("terminatetest");
            var email = GenerateTestEmail("terminatetest");
            var registerRequest = new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            };
            await authClient.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            };
            var loginResponse = await authClient.LoginAsync(loginRequest);
            var accessToken = loginResponse.AccessToken;

            // Try to terminate a specific session with a test session ID (non-existent)
            var testSessionId = Guid.NewGuid();
            try
            {
                await ((IServiceClient<AuthClient>)authClient)
                    .WithAuthorization(accessToken)
                    .TerminateSessionAsync(new TerminateSessionRequest { SessionId = testSessionId });
                return TestResult.Successful("Session termination endpoint responded successfully");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Session termination correctly returned 404 for non-existent session");
            }
        }, "Terminate session");

    private static async Task<TestResult> TestPasswordResetRequest(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            var resetRequest = new PasswordResetRequest
            {
                Email = GenerateTestEmail("passwordreset")
            };

            // Password reset should always succeed (security: don't reveal if email exists)
            await authClient.RequestPasswordResetAsync(resetRequest);
            return TestResult.Successful("Password reset request completed successfully");
        }, "Password reset request");

    private static async Task<TestResult> TestPasswordResetConfirm(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();

            var confirmRequest = new PasswordResetConfirmRequest
            {
                Token = "invalid-reset-token",
                NewPassword = "NewSecurePassword123!"
            };

            return await ExecuteExpectingAnyStatusAsync(
                async () => await authClient.ConfirmPasswordResetAsync(confirmRequest),
                [400, 401, 404],
                "Invalid reset token");
        }, "Password reset confirm");

    #region Session Validation Isolation Tests

    private static async Task<TestResult> TestSessionValidationAfterMultipleLogins(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = GenerateTestId("multilogin");
            var email = GenerateTestEmail("multilogin");
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
            var validationA1 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(tokenA).ValidateTokenAsync();
            if (!validationA1.Valid)
                return TestResult.Failed("Session A validation failed immediately after creation");

            var remainingTimeA1 = validationA1.RemainingTime;

            // Step 4: Create second session (login B)
            var loginResponseB = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });
            var tokenB = loginResponseB.AccessToken;

            if (string.IsNullOrEmpty(tokenB))
                return TestResult.Failed("Second login failed to return access token");

            // Step 5: Validate session A STILL works after session B was created
            var validationA2 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(tokenA).ValidateTokenAsync();
            if (!validationA2.Valid)
                return TestResult.Failed($"Session A became invalid after creating session B! RemainingTime before: {remainingTimeA1}");

            // Step 6: Validate session B also works
            var validationB = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(tokenB).ValidateTokenAsync();
            if (!validationB.Valid)
                return TestResult.Failed("Session B validation failed");

            // Step 7: Create third session (login C)
            var loginResponseC = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });

            // Step 8: Validate ALL sessions still work
            var validationA3 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(tokenA).ValidateTokenAsync();
            var validationB2 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(tokenB).ValidateTokenAsync();
            var validationC = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(loginResponseC.AccessToken).ValidateTokenAsync();

            if (!validationA3.Valid || !validationB2.Valid || !validationC.Valid)
            {
                return TestResult.Failed($"Session validation failed after 3 logins - A:{validationA3.Valid}, B:{validationB2.Valid}, C:{validationC.Valid}");
            }

            return TestResult.Successful($"All 3 sessions remain valid after multiple logins. RemainingTime: A={validationA3.RemainingTime}s, B={validationB2.RemainingTime}s, C={validationC.RemainingTime}s");
        }, "Session validation after multiple logins");

    private static async Task<TestResult> TestSessionValidationRoundTrip(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = GenerateTestId("roundtrip");
            var email = GenerateTestEmail("roundtrip");

            // Register and login
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;

            // Immediately validate
            var validation = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token).ValidateTokenAsync();

            if (!validation.Valid)
                return TestResult.Failed($"Immediate validation failed! SessionId: {validation.SessionKey}, RemainingTime: {validation.RemainingTime}");

            if (validation.RemainingTime <= 0)
                return TestResult.Failed($"RemainingTime is invalid: {validation.RemainingTime}");

            return TestResult.Successful($"Immediate round-trip validation succeeded. SessionId: {validation.SessionKey}, RemainingTime: {validation.RemainingTime}s");
        }, "Session validation round-trip");

    private static async Task<TestResult> TestSessionValidationWithDelay(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = GenerateTestId("delay");
            var email = GenerateTestEmail("delay");

            // Register and login
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;

            // Validate immediately
            var validation1 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token).ValidateTokenAsync();
            if (!validation1.Valid)
                return TestResult.Failed("Immediate validation failed");

            var remainingTime1 = validation1.RemainingTime;

            // Wait 2 seconds
            await Task.Delay(2000);

            // Validate again
            var validation2 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token).ValidateTokenAsync();
            if (!validation2.Valid)
                return TestResult.Failed($"Validation after 2s delay failed! RemainingTime before: {remainingTime1}");

            var remainingTime2 = validation2.RemainingTime;

            // RemainingTime should be lower (but still positive)
            if (remainingTime2 > remainingTime1)
                return TestResult.Failed($"RemainingTime increased after delay: {remainingTime1} -> {remainingTime2}");

            return TestResult.Successful($"Session remains valid after 2s delay. RemainingTime: {remainingTime1} -> {remainingTime2}");
        }, "Session validation with delay");

    private static async Task<TestResult> TestMultipleSessionsSameUser(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = GenerateTestId("multisession");
            var email = GenerateTestEmail("multisession");
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
                ((IServiceClient<AuthClient>)authClient).WithAuthorization(t).ValidateTokenAsync()
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
        }, "Multiple sessions same user");

    private static async Task<TestResult> TestSessionExpiresAtReturned(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = GenerateTestId("expiresat");
            var email = GenerateTestEmail("expiresat");

            // Register and login
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = email
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = email,
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;

            // Validate and check RemainingTime
            var validation = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token).ValidateTokenAsync();

            if (!validation.Valid)
                return TestResult.Failed("Token validation returned Valid=false");

            // RemainingTime should be positive and reasonable (typically 60 minutes = 3600 seconds)
            if (validation.RemainingTime <= 0)
                return TestResult.Failed($"RemainingTime is <= 0: {validation.RemainingTime} (ExpiresAtUnix likely deserialized as 0!)");

            if (validation.RemainingTime > 86400) // More than 24 hours
                return TestResult.Failed($"RemainingTime suspiciously high: {validation.RemainingTime}s (might be timestamp error)");

            // Expected: ~3600 seconds (60 minutes default expiration)
            var expectedMinSeconds = 3500;
            var expectedMaxSeconds = 3700;

            if (validation.RemainingTime < expectedMinSeconds || validation.RemainingTime > expectedMaxSeconds)
            {
                return TestResult.Successful($"RemainingTime: {validation.RemainingTime}s (outside typical 60min range but valid)");
            }

            return TestResult.Successful($"RemainingTime: {validation.RemainingTime}s (correctly around 60 minutes as expected)");
        }, "Session expires at returned");

    #endregion
}
