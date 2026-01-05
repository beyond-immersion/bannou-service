using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.GameSession;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for game session service API endpoints.
/// Tests the game session service APIs through the Connect service WebSocket binary protocol.
///
/// IMPORTANT: These tests create dedicated test accounts with their own BannouClient instances.
/// This avoids interfering with Program.Client or Program.AdminClient, and properly tests
/// the user experience from account creation through API usage.
/// </summary>
public class GameSessionWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        // Note: Direct game session endpoints (create/list/join) are now shortcut-only.
        // Users access these through session shortcuts pushed by the game-session service
        // when they have an active subscription. Only the subscription-based test is valid.
        return new ServiceTest[]
        {
            new ServiceTest(TestSubscriptionBasedJoinViaShortcut, "GameSession - Subscription Shortcut Join (WebSocket)", "WebSocket",
                "Test subscription-based join flow: create subscription -> connect -> receive shortcut -> invoke shortcut"),
        };
    }

    #region Helper Methods for Test Account Creation

    /// <summary>
    /// Creates a dedicated test account and returns the access token and connect URL.
    /// Each test should create its own account to ensure isolation.
    /// </summary>
    private async Task<(string accessToken, string connectUrl, string email)?> CreateTestAccountAsync(string testPrefix)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return null;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"{testPrefix}_{uniqueId}@test.local";
        var testPassword = $"{testPrefix}Test123!";

        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new { username = $"{testPrefix}_{uniqueId}", email = testEmail, password = testPassword };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                BannouJson.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (!registerResponse.IsSuccessStatusCode)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"   Failed to create test account: {registerResponse.StatusCode} - {errorBody}");
                return null;
            }

            var responseBody = await registerResponse.Content.ReadAsStringAsync();
            var responseObj = JsonDocument.Parse(responseBody);
            var accessToken = responseObj.RootElement.GetProperty("accessToken").GetString();
            var connectUrl = responseObj.RootElement.GetProperty("connectUrl").GetString();

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("   No accessToken in registration response");
                return null;
            }

            if (string.IsNullOrEmpty(connectUrl))
            {
                Console.WriteLine("   No connectUrl in registration response");
                return null;
            }

            Console.WriteLine($"   Created test account: {testEmail}");
            return (accessToken, connectUrl, testEmail);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a BannouClient connected with the given access token and connect URL.
    /// Returns null if connection fails.
    /// </summary>
    private async Task<BannouClient?> CreateConnectedClientAsync(string accessToken, string connectUrl)
    {
        var client = new BannouClient();

        try
        {
            var connected = await client.ConnectWithTokenAsync(connectUrl, accessToken);
            if (!connected || !client.IsConnected)
            {
                Console.WriteLine("   BannouClient failed to connect");
                await client.DisposeAsync();
                return null;
            }

            Console.WriteLine($"   BannouClient connected, session: {client.SessionId}");
            return client;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   BannouClient connection failed: {ex.Message}");
            await client.DisposeAsync();
            return null;
        }
    }

    #endregion

    // Note: TestCreateGameSessionViaWebSocket, TestListGameSessionsViaWebSocket, and
    // TestCompleteSessionLifecycleViaWebSocket were removed because game session endpoints
    // (create/list/join) are now shortcut-only. Users access these through session shortcuts
    // pushed by the game-session service when they have an active subscription.

    private void TestSubscriptionBasedJoinViaShortcut(string[] args)
    {
        Console.WriteLine("=== GameSession Subscription-Based Shortcut Join Test (WebSocket) ===");
        Console.WriteLine("Testing subscription -> connect -> receive shortcut -> invoke shortcut flow...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Step 1: Create a test service (arcadia type for game-session to recognize)
                    Console.WriteLine("   Step 1: Creating test service 'arcadia'...");
                    var serviceResponse = await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/game-service/services/create",
                        new
                        {
                            stubName = "arcadia",
                            displayName = "Arcadia Game Service",
                            description = "Test game service for shortcut flow",
                            serviceType = "game"
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    string? serviceId = null;
                    if (serviceResponse.IsSuccess)
                    {
                        var json = System.Text.Json.Nodes.JsonNode.Parse(serviceResponse.Result.GetRawText())?.AsObject();
                        serviceId = json?["serviceId"]?.GetValue<string>();
                        Console.WriteLine($"   Created service: {serviceId}");
                    }
                    else if (serviceResponse.Error?.ResponseCode == 409)
                    {
                        // Service already exists - get it
                        Console.WriteLine("   Service 'arcadia' already exists, fetching...");
                        var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/game-service/services/list",
                            new { },
                            timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();
                        var listJson = System.Text.Json.Nodes.JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                        var services = listJson?["services"]?.AsArray();
                        if (services != null)
                        {
                            foreach (var svc in services)
                            {
                                if (svc?["stubName"]?.GetValue<string>() == "arcadia")
                                {
                                    serviceId = svc?["serviceId"]?.GetValue<string>();
                                    break;
                                }
                            }
                        }
                        Console.WriteLine($"   Found existing service: {serviceId}");
                    }
                    else
                    {
                        Console.WriteLine($"   Failed to create service: {serviceResponse.Error?.Message}");
                        return false;
                    }

                    if (string.IsNullOrEmpty(serviceId))
                    {
                        Console.WriteLine("   Could not obtain service ID");
                        return false;
                    }

                    // Step 2: Create user credentials (this creates an account with auth)
                    Console.WriteLine("   Step 2: Registering user account...");
                    var authResult = await CreateTestAccountAsync($"shortcut_{uniqueCode}");
                    if (authResult == null)
                    {
                        Console.WriteLine("   Failed to create WebSocket test user");
                        return false;
                    }

                    // Step 3: Look up the account ID by email (using admin client)
                    Console.WriteLine($"   Step 3: Looking up account ID for {authResult.Value.email}...");
                    var accountLookupResponse = await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/account/by-email",
                        new { email = authResult.Value.email },
                        timeout: TimeSpan.FromSeconds(5));

                    if (!accountLookupResponse.IsSuccess)
                    {
                        Console.WriteLine($"   Failed to look up account: {accountLookupResponse.Error?.Message}");
                        return false;
                    }

                    var accountJson = System.Text.Json.Nodes.JsonNode.Parse(accountLookupResponse.Result.GetRawText())?.AsObject();
                    var accountId = accountJson?["accountId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(accountId))
                    {
                        Console.WriteLine($"   Could not extract accountId from response: {accountLookupResponse.Result.GetRawText()}");
                        return false;
                    }
                    Console.WriteLine($"   Account ID: {accountId}");

                    // Step 4: Create subscription for this account to the arcadia service
                    Console.WriteLine("   Step 4: Creating subscription...");
                    var subResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/create",
                        new
                        {
                            accountId = accountId,
                            serviceId = serviceId,
                            durationDays = 30
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var subJson = System.Text.Json.Nodes.JsonNode.Parse(subResponse.GetRawText())?.AsObject();
                    var subscriptionId = subJson?["subscriptionId"]?.GetValue<string>();
                    Console.WriteLine($"   Created subscription: {subscriptionId}");

                    // Step 5: Connect via WebSocket (subscription now exists for this account)
                    Console.WriteLine("   Step 5: Connecting via WebSocket...");
                    await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                    if (client == null)
                    {
                        Console.WriteLine("   Failed to connect WebSocket client");
                        return false;
                    }

                    // Step 5: Wait for shortcut to appear in available APIs
                    Console.WriteLine("   Step 5: Waiting for shortcut in available APIs...");
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    Guid? shortcutGuid = null;

                    while (DateTime.UtcNow < deadline)
                    {
                        // Check if we have a shortcut for join_game_arcadia
                        shortcutGuid = client.GetServiceGuid("SHORTCUT", "join_game_arcadia");
                        if (shortcutGuid.HasValue)
                        {
                            Console.WriteLine($"   Found shortcut: SHORTCUT:join_game_arcadia -> {shortcutGuid}");
                            break;
                        }
                        await Task.Delay(500);
                    }

                    if (!shortcutGuid.HasValue)
                    {
                        Console.WriteLine("   Shortcut not received within timeout");
                        Console.WriteLine($"   Available APIs: {string.Join(", ", client.AvailableApis.Keys.Take(10))}...");
                        return false;
                    }

                    // Step 6: Invoke the shortcut with empty payload
                    Console.WriteLine("   Step 6: Invoking shortcut with empty payload...");
                    var joinResponse = await client.InvokeAsync<object, JsonElement>(
                        "SHORTCUT",
                        "join_game_arcadia",
                        new { }, // Empty payload - server injects the bound data
                        timeout: TimeSpan.FromSeconds(5));

                    if (!joinResponse.IsSuccess)
                    {
                        Console.WriteLine($"   Shortcut invocation failed: {joinResponse.Error?.Message}");
                        Console.WriteLine($"   Error code: {joinResponse.Error?.ResponseCode} ({joinResponse.Error?.ErrorName})");
                        Console.WriteLine($"   Method/Path: {joinResponse.Error?.Method} {joinResponse.Error?.Path}");
                        return false;
                    }

                    var joinJson = System.Text.Json.Nodes.JsonNode.Parse(joinResponse.Result.GetRawText())?.AsObject();
                    var success = joinJson?["success"]?.GetValue<bool>() ?? false;
                    var sessionId = joinJson?["sessionId"]?.GetValue<string>();

                    Console.WriteLine($"   Join result: success={success}, sessionId={sessionId}");

                    return success && !string.IsNullOrEmpty(sessionId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED GameSession subscription-based shortcut join test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED GameSession subscription-based shortcut join test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED GameSession subscription shortcut test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

}
