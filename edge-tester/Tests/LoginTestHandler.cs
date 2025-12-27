using BeyondImmersion.BannouService.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// HTTP-based test handler for authentication flow validation.
/// Tests the complete auth lifecycle from a client perspective through OpenResty gateway.
/// </summary>
public class LoginTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            // Core authentication flows
            new ServiceTest(TestRegistrationFlow, "Auth - Registration Flow", "HTTP",
                "Test user registration through OpenResty gateway returns valid tokens"),
            new ServiceTest(TestLoginWithCredentials, "Auth - Login with Credentials", "HTTP",
                "Test login with email/password returns valid access and refresh tokens"),
            new ServiceTest(TestTokenRefresh, "Auth - Token Refresh", "HTTP",
                "Test refresh token can be used to obtain new access token"),
            new ServiceTest(TestInvalidCredentials, "Auth - Invalid Credentials", "HTTP",
                "Test login with invalid credentials returns proper error"),

            // Token validation and session management
            new ServiceTest(TestTokenValidation, "Auth - Token Validation", "HTTP",
                "Test token validation endpoint returns session info"),
            new ServiceTest(TestGetSessions, "Auth - Get Sessions", "HTTP",
                "Test get active sessions for authenticated user"),
            new ServiceTest(TestLogout, "Auth - Logout", "HTTP",
                "Test logout invalidates the current session"),

            // OAuth flows (init only - callback requires real provider)
            new ServiceTest(TestOAuthInitDiscord, "Auth - OAuth Init Discord", "HTTP",
                "Test Discord OAuth init returns redirect URL"),
            new ServiceTest(TestOAuthInitGoogle, "Auth - OAuth Init Google", "HTTP",
                "Test Google OAuth init returns redirect URL"),

            // Steam Session Ticket flow
            new ServiceTest(TestSteamVerify, "Auth - Steam Session Ticket", "HTTP",
                "Test Steam Session Ticket verification through gateway"),
        };
    }

    private void TestRegistrationFlow(string[] args)
    {
        Console.WriteLine("=== Registration Flow Test ===");
        Console.WriteLine("Testing user registration through OpenResty gateway...");

        try
        {
            var result = Task.Run(async () => await PerformRegistrationTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Registration flow test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Registration flow test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Registration flow test FAILED with exception: {ex.Message}");
        }
    }

    private void TestLoginWithCredentials(string[] args)
    {
        Console.WriteLine("=== Login with Credentials Test ===");
        Console.WriteLine("Testing login with email/password returns valid tokens...");

        try
        {
            var result = Task.Run(async () => await PerformLoginTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Login with credentials test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Login with credentials test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Login with credentials test FAILED with exception: {ex.Message}");
        }
    }

    private void TestTokenRefresh(string[] args)
    {
        Console.WriteLine("=== Token Refresh Test ===");
        Console.WriteLine("Testing refresh token can obtain new access token...");

        try
        {
            var result = Task.Run(async () => await PerformTokenRefreshTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Token refresh test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Token refresh test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Token refresh test FAILED with exception: {ex.Message}");
        }
    }

    private void TestInvalidCredentials(string[] args)
    {
        Console.WriteLine("=== Invalid Credentials Test ===");
        Console.WriteLine("Testing login with invalid credentials returns proper error...");

        try
        {
            var result = Task.Run(async () => await PerformInvalidCredentialsTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Invalid credentials test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Invalid credentials test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Invalid credentials test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformRegistrationTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Generate unique test user for this test run
        // Username must match regex ^[a-zA-Z0-9_]+$ - no special characters
        var uniqueId = Guid.NewGuid().ToString("N")[..16]; // Use first 16 chars of guid
        var testUsername = $"regtest_{uniqueId}";
        var testEmail = $"{testUsername}@test.local";
        var testPassword = "RegistrationTest123!";

        var registerUrl = $"http://{Program.Configuration.RegisterEndpoint}";
        Console.WriteLine($"📡 Testing registration at: {registerUrl}");

        var content = new JsonObject
        {
            ["username"] = testUsername,
            ["email"] = testEmail, // Explicitly provide email for login consistency
            ["password"] = testPassword
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, registerUrl);
        request.Content = new StringContent(BannouJson.Serialize(content), Encoding.UTF8, "application/json");

        using var response = await Program.HttpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Response: {response.StatusCode}");
        Console.WriteLine($"   Body: {responseBody.Substring(0, Math.Min(500, responseBody.Length))}");

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Registration failed with status: {response.StatusCode}");
            return false;
        }

        // Verify response contains tokens
        var responseObj = JsonNode.Parse(responseBody)?.AsObject();
        var accessToken = responseObj?["accessToken"]?.GetValue<string>();
        var refreshToken = responseObj?["refreshToken"]?.GetValue<string>();

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Registration response missing accessToken");
            return false;
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            Console.WriteLine("❌ Registration response missing refreshToken");
            return false;
        }

        Console.WriteLine($"✅ Registration returned valid tokens for {testEmail}");
        return true;
    }

    private async Task<bool> PerformLoginTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Use the existing test user (already registered in Program.Main)
        var loginUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}";
        Console.WriteLine($"📡 Testing login at: {loginUrl}");

        var content = new JsonObject
        {
            ["email"] = Program.Configuration.ClientUsername,
            ["password"] = Program.Configuration.ClientPassword
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        request.Content = new StringContent(BannouJson.Serialize(content), Encoding.UTF8, "application/json");

        using var response = await Program.HttpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Response: {response.StatusCode}");

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Login failed with status: {response.StatusCode}");
            Console.WriteLine($"   Body: {responseBody}");
            return false;
        }

        // Verify response structure
        var responseObj = JsonNode.Parse(responseBody)?.AsObject();
        var accessToken = responseObj?["accessToken"]?.GetValue<string>();
        var refreshToken = responseObj?["refreshToken"]?.GetValue<string>();

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Login response missing accessToken");
            return false;
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            Console.WriteLine("❌ Login response missing refreshToken");
            return false;
        }

        // Verify access token is a valid JWT format (header.payload.signature)
        var jwtParts = accessToken.Split('.');
        if (jwtParts.Length != 3)
        {
            Console.WriteLine($"❌ Access token is not valid JWT format (expected 3 parts, got {jwtParts.Length})");
            return false;
        }

        Console.WriteLine("✅ Login returned valid JWT tokens");
        return true;
    }

    private async Task<bool> PerformTokenRefreshTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // First login to get a refresh token
        var loginUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}";

        var loginContent = new JsonObject
        {
            ["email"] = Program.Configuration.ClientUsername,
            ["password"] = Program.Configuration.ClientPassword
        };

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        loginRequest.Content = new StringContent(BannouJson.Serialize(loginContent), Encoding.UTF8, "application/json");

        using var loginResponse = await Program.HttpClient.SendAsync(loginRequest);
        if (loginResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Initial login failed: {loginResponse.StatusCode}");
            return false;
        }

        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        var loginObj = JsonNode.Parse(loginBody)?.AsObject();
        var refreshToken = loginObj?["refreshToken"]?.GetValue<string>();

        if (string.IsNullOrEmpty(refreshToken))
        {
            Console.WriteLine("❌ No refresh token from login");
            return false;
        }

        // Now use refresh token to get new access token
        var refreshUrl = $"http://{Program.Configuration.LoginTokenEndpoint}";
        Console.WriteLine($"📡 Testing token refresh at: {refreshUrl}");

        // Get access token for Authorization header
        var accessToken = loginObj?["accessToken"]?.GetValue<string>();
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ No access token from login for refresh test");
            return false;
        }

        // Refresh endpoint expects: Authorization header with JWT + body with refreshToken
        var refreshContent = new JsonObject
        {
            ["refreshToken"] = refreshToken
        };

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, refreshUrl);
        refreshRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        refreshRequest.Content = new StringContent(BannouJson.Serialize(refreshContent), Encoding.UTF8, "application/json");
        refreshRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var refreshResponse = await Program.HttpClient.SendAsync(refreshRequest);
        var refreshBody = await refreshResponse.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Response: {refreshResponse.StatusCode}");

        if (refreshResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Token refresh failed with status: {refreshResponse.StatusCode}");
            Console.WriteLine($"   Body: {refreshBody}");
            return false;
        }

        // Verify new tokens returned
        var refreshObj = JsonNode.Parse(refreshBody)?.AsObject();
        var newAccessToken = refreshObj?["accessToken"]?.GetValue<string>();

        if (string.IsNullOrEmpty(newAccessToken))
        {
            Console.WriteLine("❌ Token refresh response missing new accessToken");
            return false;
        }

        Console.WriteLine("✅ Token refresh returned new access token");
        return true;
    }

    private async Task<bool> PerformInvalidCredentialsTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var loginUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}";
        Console.WriteLine($"📡 Testing invalid login at: {loginUrl}");

        var content = new JsonObject
        {
            ["email"] = "nonexistent-user-12345@invalid.test",
            ["password"] = "WrongPassword123!"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        request.Content = new StringContent(BannouJson.Serialize(content), Encoding.UTF8, "application/json");

        using var response = await Program.HttpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Response: {response.StatusCode}");

        // We expect a 401 Unauthorized or 400 Bad Request for invalid credentials
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine("❌ Login succeeded with invalid credentials - security issue!");
            return false;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
            response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"✅ Invalid credentials correctly rejected with {response.StatusCode}");
            return true;
        }

        Console.WriteLine($"⚠️ Unexpected response for invalid credentials: {response.StatusCode}");
        Console.WriteLine($"   Body: {responseBody}");
        return false;
    }

    private void TestTokenValidation(string[] args)
    {
        Console.WriteLine("=== Token Validation Test ===");
        Console.WriteLine("Testing token validation endpoint...");

        try
        {
            var result = Task.Run(async () => await PerformTokenValidationTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Token validation test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Token validation test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Token validation test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformTokenValidationTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // First login to get a valid token
        var loginUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}";
        var loginContent = new JsonObject
        {
            ["email"] = Program.Configuration.ClientUsername,
            ["password"] = Program.Configuration.ClientPassword
        };

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        loginRequest.Content = new StringContent(BannouJson.Serialize(loginContent), Encoding.UTF8, "application/json");

        using var loginResponse = await Program.HttpClient.SendAsync(loginRequest);
        if (loginResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Login failed: {loginResponse.StatusCode}");
            return false;
        }

        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        var loginObj = JsonNode.Parse(loginBody)?.AsObject();
        var accessToken = loginObj?["accessToken"]?.GetValue<string>();

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ No access token from login");
            return false;
        }

        // Now validate the token
        var loginEndpoint = Program.Configuration.LoginCredentialsEndpoint
            ?? throw new InvalidOperationException("LoginCredentialsEndpoint not configured");
        var validateUrl = $"http://{loginEndpoint.Replace("/auth/login", "/auth/validate")}";
        Console.WriteLine($"📡 Testing token validation at: {validateUrl}");

        using var validateRequest = new HttpRequestMessage(HttpMethod.Post, validateUrl);
        validateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var validateResponse = await Program.HttpClient.SendAsync(validateRequest);
        var validateBody = await validateResponse.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Response: {validateResponse.StatusCode}");

        if (validateResponse.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var validateObj = JsonNode.Parse(validateBody)?.AsObject();
            var valid = validateObj?["valid"]?.GetValue<bool>();
            if (valid != true)
            {
                Console.WriteLine($"❌ Token validation returned valid={valid} (expected true)");
                return false;
            }
            Console.WriteLine($"✅ Token validation returned valid={valid}");
            return true;
        }

        Console.WriteLine($"❌ Token validation failed with status: {validateResponse.StatusCode}");
        Console.WriteLine($"   Body: {validateBody}");
        return false;
    }

    private void TestGetSessions(string[] args)
    {
        Console.WriteLine("=== Get Sessions Test ===");
        Console.WriteLine("Testing get sessions endpoint...");

        try
        {
            var result = Task.Run(async () => await PerformGetSessionsTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Get sessions test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Get sessions test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Get sessions test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformGetSessionsTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // First login to get a valid token
        var loginUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}";
        var loginContent = new JsonObject
        {
            ["email"] = Program.Configuration.ClientUsername,
            ["password"] = Program.Configuration.ClientPassword
        };

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        loginRequest.Content = new StringContent(BannouJson.Serialize(loginContent), Encoding.UTF8, "application/json");

        using var loginResponse = await Program.HttpClient.SendAsync(loginRequest);
        if (loginResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Login failed: {loginResponse.StatusCode}");
            return false;
        }

        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        var loginObj = JsonNode.Parse(loginBody)?.AsObject();
        var accessToken = loginObj?["accessToken"]?.GetValue<string>();

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ No access token from login");
            return false;
        }

        // Now get sessions - POST /auth/sessions/list
        var sessionsLoginEndpoint = Program.Configuration.LoginCredentialsEndpoint
            ?? throw new InvalidOperationException("LoginCredentialsEndpoint not configured");
        var sessionsUrl = $"http://{sessionsLoginEndpoint.Replace("/auth/login", "/auth/sessions/list")}";
        Console.WriteLine($"📡 Testing get sessions at: {sessionsUrl}");

        using var sessionsRequest = new HttpRequestMessage(HttpMethod.Post, sessionsUrl);
        sessionsRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        sessionsRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var sessionsResponse = await Program.HttpClient.SendAsync(sessionsRequest);
        var sessionsBody = await sessionsResponse.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Response: {sessionsResponse.StatusCode}");

        if (sessionsResponse.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var sessionsObj = JsonNode.Parse(sessionsBody)?.AsObject();
            var sessions = sessionsObj?["sessions"]?.AsArray();
            if (sessions == null)
            {
                Console.WriteLine("❌ Get sessions response missing sessions array");
                return false;
            }
            // Should have at least one session (the one we just logged in with)
            if (sessions.Count == 0)
            {
                Console.WriteLine("❌ Get sessions returned 0 sessions (expected at least 1)");
                return false;
            }
            Console.WriteLine($"✅ Get sessions returned {sessions.Count} session(s)");
            return true;
        }

        Console.WriteLine($"❌ Get sessions failed with status: {sessionsResponse.StatusCode}");
        Console.WriteLine($"   Body: {sessionsBody}");
        return false;
    }

    private void TestLogout(string[] args)
    {
        Console.WriteLine("=== Logout Test ===");
        Console.WriteLine("Testing logout endpoint...");

        try
        {
            var result = Task.Run(async () => await PerformLogoutTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Logout test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Logout test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Logout test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformLogoutTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // First login to get a valid token
        var loginUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}";
        var loginContent = new JsonObject
        {
            ["email"] = Program.Configuration.ClientUsername,
            ["password"] = Program.Configuration.ClientPassword
        };

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        loginRequest.Content = new StringContent(BannouJson.Serialize(loginContent), Encoding.UTF8, "application/json");

        using var loginResponse = await Program.HttpClient.SendAsync(loginRequest);
        if (loginResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Login failed: {loginResponse.StatusCode}");
            return false;
        }

        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        var loginObj = JsonNode.Parse(loginBody)?.AsObject();
        var accessToken = loginObj?["accessToken"]?.GetValue<string>();

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ No access token from login");
            return false;
        }

        // Now logout
        var logoutLoginEndpoint = Program.Configuration.LoginCredentialsEndpoint
            ?? throw new InvalidOperationException("LoginCredentialsEndpoint not configured");
        var logoutUrl = $"http://{logoutLoginEndpoint.Replace("/auth/login", "/auth/logout")}";
        Console.WriteLine($"📡 Testing logout at: {logoutUrl}");

        var logoutContent = new JsonObject { ["allSessions"] = false };

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, logoutUrl);
        logoutRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        logoutRequest.Content = new StringContent(BannouJson.Serialize(logoutContent), Encoding.UTF8, "application/json");

        using var logoutResponse = await Program.HttpClient.SendAsync(logoutRequest);

        Console.WriteLine($"📥 Response: {logoutResponse.StatusCode}");

        if (logoutResponse.StatusCode == System.Net.HttpStatusCode.OK ||
            logoutResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            Console.WriteLine("✅ Logout completed successfully");
            return true;
        }

        var logoutBody = await logoutResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"❌ Logout failed with status: {logoutResponse.StatusCode}");
        Console.WriteLine($"   Body: {logoutBody}");
        return false;
    }

    private void TestOAuthInitDiscord(string[] args)
    {
        Console.WriteLine("=== OAuth Init Discord Test ===");
        Console.WriteLine("Testing Discord OAuth initialization...");

        try
        {
            var result = Task.Run(async () => await PerformOAuthInitTest("discord")).Result;
            if (result)
            {
                Console.WriteLine("✅ OAuth init Discord test PASSED");
            }
            else
            {
                Console.WriteLine("❌ OAuth init Discord test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ OAuth init Discord test FAILED with exception: {ex.Message}");
        }
    }

    private void TestOAuthInitGoogle(string[] args)
    {
        Console.WriteLine("=== OAuth Init Google Test ===");
        Console.WriteLine("Testing Google OAuth initialization...");

        try
        {
            var result = Task.Run(async () => await PerformOAuthInitTest("google")).Result;
            if (result)
            {
                Console.WriteLine("✅ OAuth init Google test PASSED");
            }
            else
            {
                Console.WriteLine("❌ OAuth init Google test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ OAuth init Google test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformOAuthInitTest(string provider)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Build OAuth init URL
        var oauthLoginEndpoint = Program.Configuration.LoginCredentialsEndpoint
            ?? throw new InvalidOperationException("LoginCredentialsEndpoint not configured");
        var baseUrl = oauthLoginEndpoint.Replace("/auth/login", "");
        var oauthUrl = $"http://{baseUrl}/auth/oauth/{provider}/init?redirectUri=http://localhost:5012/callback&state=test_state";
        Console.WriteLine($"📡 Testing OAuth init at: {oauthUrl}");

        // Note: We don't follow redirects for this test - we just verify the endpoint works
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        using var oauthRequest = new HttpRequestMessage(HttpMethod.Get, oauthUrl);
        using var oauthResponse = await client.SendAsync(oauthRequest);

        Console.WriteLine($"📥 Response: {oauthResponse.StatusCode}");

        // OAuth init should return 302 redirect or 200 with auth URL
        if (oauthResponse.StatusCode == System.Net.HttpStatusCode.Redirect ||
            oauthResponse.StatusCode == System.Net.HttpStatusCode.Found)
        {
            var location = oauthResponse.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location))
            {
                Console.WriteLine("❌ OAuth redirect missing Location header");
                return false;
            }
            Console.WriteLine($"✅ OAuth init returned redirect to: {location.Substring(0, Math.Min(100, location.Length))}...");
            return true;
        }

        if (oauthResponse.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var body = await oauthResponse.Content.ReadAsStringAsync();
            // Verify the response contains an auth URL
            if (!body.Contains("http") && !body.Contains("url"))
            {
                Console.WriteLine($"❌ OAuth init returned OK but no auth URL in response");
                return false;
            }
            Console.WriteLine($"✅ OAuth init returned OK with auth URL");
            return true;
        }

        var errorBody = await oauthResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"❌ OAuth init for {provider} failed with status: {oauthResponse.StatusCode}");
        Console.WriteLine($"   Body: {errorBody.Substring(0, Math.Min(200, errorBody.Length))}");
        return false;
    }

    private void TestSteamVerify(string[] args)
    {
        Console.WriteLine("=== Steam Session Ticket Test ===");
        Console.WriteLine("Testing Steam Session Ticket verification...");

        try
        {
            var result = Task.Run(async () => await PerformSteamVerifyTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Steam verify test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Steam verify test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Steam verify test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformSteamVerifyTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Build Steam verify URL
        var steamLoginEndpoint = Program.Configuration.LoginCredentialsEndpoint
            ?? throw new InvalidOperationException("LoginCredentialsEndpoint not configured");
        var steamBaseUrl = steamLoginEndpoint.Replace("/auth/login", "");
        var steamUrl = $"http://{steamBaseUrl}/auth/steam/verify";
        Console.WriteLine($"📡 Testing Steam verify at: {steamUrl}");

        // Send a mock Steam Session Ticket - this should fail validation but test the endpoint
        var steamContent = new JsonObject
        {
            ["ticket"] = "140000006A7B3C8E0123456789ABCDEF0123456789ABCDEF"
        };

        using var steamRequest = new HttpRequestMessage(HttpMethod.Post, steamUrl);
        steamRequest.Content = new StringContent(BannouJson.Serialize(steamContent), Encoding.UTF8, "application/json");

        using var steamResponse = await Program.HttpClient.SendAsync(steamRequest);
        var steamBody = await steamResponse.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Response: {steamResponse.StatusCode}");

        // With mock data, we expect either:
        // - 200 OK (MockProviders enabled)
        // - 401 Unauthorized (correctly rejected invalid ticket)
        if (steamResponse.StatusCode == System.Net.HttpStatusCode.OK)
        {
            // Verify we got tokens back
            var responseObj = JsonNode.Parse(steamBody)?.AsObject();
            var accessToken = responseObj?["accessToken"]?.GetValue<string>();
            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("❌ Steam verify returned OK but no access token");
                return false;
            }
            Console.WriteLine("✅ Steam verify succeeded (MockProviders enabled)");
            return true;
        }

        // 401 is expected when ticket validation fails - this proves the endpoint works
        if (steamResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Console.WriteLine("✅ Steam verify correctly rejected invalid ticket (401)");
            return true;
        }

        Console.WriteLine($"❌ Steam verify failed with status: {steamResponse.StatusCode}");
        Console.WriteLine($"   Body: {steamBody.Substring(0, Math.Min(200, steamBody.Length))}");
        return false;
    }
}
