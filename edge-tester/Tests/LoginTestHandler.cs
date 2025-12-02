using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

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
            new ServiceTest(TestRegistrationFlow, "Auth - Registration Flow", "HTTP",
                "Test user registration through OpenResty gateway returns valid tokens"),
            new ServiceTest(TestLoginWithCredentials, "Auth - Login with Credentials", "HTTP",
                "Test login with email/password returns valid access and refresh tokens"),
            new ServiceTest(TestTokenRefresh, "Auth - Token Refresh", "HTTP",
                "Test refresh token can be used to obtain new access token"),
            new ServiceTest(TestInvalidCredentials, "Auth - Invalid Credentials", "HTTP",
                "Test login with invalid credentials returns proper error")
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

        var registerUrl = $"http://{Program.Configuration.Register_Endpoint}";
        Console.WriteLine($"📡 Testing registration at: {registerUrl}");

        var content = new JObject
        {
            ["username"] = testUsername,
            ["email"] = testEmail, // Explicitly provide email for login consistency
            ["password"] = testPassword
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, registerUrl);
        request.Content = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json");

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
        var responseObj = JObject.Parse(responseBody);
        var accessToken = (string?)responseObj["accessToken"];
        var refreshToken = (string?)responseObj["refreshToken"];

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
        var loginUrl = $"http://{Program.Configuration.Login_Credentials_Endpoint}";
        Console.WriteLine($"📡 Testing login at: {loginUrl}");

        var content = new JObject
        {
            ["email"] = Program.Configuration.Client_Username,
            ["password"] = Program.Configuration.Client_Password
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        request.Content = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json");

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
        var responseObj = JObject.Parse(responseBody);
        var accessToken = (string?)responseObj["accessToken"];
        var refreshToken = (string?)responseObj["refreshToken"];

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
        var loginUrl = $"http://{Program.Configuration.Login_Credentials_Endpoint}";

        var loginContent = new JObject
        {
            ["email"] = Program.Configuration.Client_Username,
            ["password"] = Program.Configuration.Client_Password
        };

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        loginRequest.Content = new StringContent(JsonConvert.SerializeObject(loginContent), Encoding.UTF8, "application/json");

        using var loginResponse = await Program.HttpClient.SendAsync(loginRequest);
        if (loginResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"❌ Initial login failed: {loginResponse.StatusCode}");
            return false;
        }

        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        var loginObj = JObject.Parse(loginBody);
        var refreshToken = (string?)loginObj["refreshToken"];

        if (string.IsNullOrEmpty(refreshToken))
        {
            Console.WriteLine("❌ No refresh token from login");
            return false;
        }

        // Now use refresh token to get new access token
        var refreshUrl = $"http://{Program.Configuration.Login_Token_Endpoint}";
        Console.WriteLine($"📡 Testing token refresh at: {refreshUrl}");

        // Get access token for Authorization header
        var accessToken = (string?)loginObj["accessToken"];
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ No access token from login for refresh test");
            return false;
        }

        // Refresh endpoint expects: Authorization header with JWT + body with refreshToken
        var refreshContent = new JObject
        {
            ["refreshToken"] = refreshToken
        };

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, refreshUrl);
        refreshRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        refreshRequest.Content = new StringContent(JsonConvert.SerializeObject(refreshContent), Encoding.UTF8, "application/json");
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
        var refreshObj = JObject.Parse(refreshBody);
        var newAccessToken = (string?)refreshObj["accessToken"];

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

        var loginUrl = $"http://{Program.Configuration.Login_Credentials_Endpoint}";
        Console.WriteLine($"📡 Testing invalid login at: {loginUrl}");

        var content = new JObject
        {
            ["email"] = "nonexistent-user-12345@invalid.test",
            ["password"] = "WrongPassword123!"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
        request.Content = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json");

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
}
