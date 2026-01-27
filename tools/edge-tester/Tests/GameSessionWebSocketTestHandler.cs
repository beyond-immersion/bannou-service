using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Subscription;
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
public class GameSessionWebSocketTestHandler : BaseWebSocketTestHandler
{
    public override ServiceTest[] GetServiceTests()
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

    /// <summary>
    /// Gets or creates the test-game service using typed proxy.
    /// </summary>
    private async Task<ServiceInfo?> GetOrCreateTestServiceAsync(BannouClient adminClient)
    {
        // Try to create the service first
        var createResponse = await adminClient.GameService.CreateServiceAsync(new CreateServiceRequest
        {
            StubName = "test-game",
            DisplayName = "Test Game Service",
            Description = "Test game service for shortcut flow"
        }, timeout: TimeSpan.FromSeconds(5));

        if (createResponse.IsSuccess && createResponse.Result != null)
        {
            Console.WriteLine($"   Created service: {createResponse.Result.ServiceId}");
            return createResponse.Result;
        }

        // If 409 conflict, service already exists - fetch it
        if (createResponse.Error?.ResponseCode == 409)
        {
            Console.WriteLine("   Service 'test-game' already exists, fetching...");
            var listResponse = await adminClient.GameService.ListServicesAsync(new ListServicesRequest(),
                timeout: TimeSpan.FromSeconds(5));

            if (!listResponse.IsSuccess || listResponse.Result?.Services == null)
            {
                Console.WriteLine($"   Failed to list services: {FormatError(listResponse.Error)}");
                return null;
            }

            var service = listResponse.Result.Services.FirstOrDefault(s => s.StubName == "test-game");
            if (service != null)
            {
                Console.WriteLine($"   Found existing service: {service.ServiceId}");
                return service;
            }

            Console.WriteLine("   Service 'test-game' not found in list");
            return null;
        }

        Console.WriteLine($"   Failed to create service: {FormatError(createResponse.Error)}");
        return null;
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
                    // Step 1: Create a test service (test-game type for game-session to recognize)
                    Console.WriteLine("   Step 1: Creating test service 'test-game'...");
                    var service = await GetOrCreateTestServiceAsync(adminClient);
                    if (service == null)
                    {
                        Console.WriteLine("   Could not obtain service");
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

                    // Step 3: Look up the account ID by email (using admin client typed proxy)
                    Console.WriteLine($"   Step 3: Looking up account ID for {authResult.Value.email}...");
                    var accountLookupResponse = await adminClient.Account.GetAccountByEmailAsync(new GetAccountByEmailRequest
                    {
                        Email = authResult.Value.email
                    }, timeout: TimeSpan.FromSeconds(5));

                    if (!accountLookupResponse.IsSuccess || accountLookupResponse.Result == null)
                    {
                        Console.WriteLine($"   Failed to look up account: {FormatError(accountLookupResponse.Error)}");
                        return false;
                    }

                    var accountId = accountLookupResponse.Result.AccountId;
                    Console.WriteLine($"   Account ID: {accountId}");

                    // Step 4: Create subscription for this account to the test-game service
                    Console.WriteLine("   Step 4: Creating subscription...");
                    var subResponse = await adminClient.Subscription.CreateSubscriptionAsync(new CreateSubscriptionRequest
                    {
                        AccountId = accountId,
                        ServiceId = service.ServiceId,
                        DurationDays = 30
                    }, timeout: TimeSpan.FromSeconds(5));

                    if (!subResponse.IsSuccess || subResponse.Result == null)
                    {
                        Console.WriteLine($"   Failed to create subscription: {FormatError(subResponse.Error)}");
                        return false;
                    }

                    Console.WriteLine($"   Created subscription: {subResponse.Result.SubscriptionId}");

                    // Step 5: Connect via WebSocket (subscription now exists for this account)
                    Console.WriteLine("   Step 5: Connecting via WebSocket...");
                    await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                    if (client == null)
                    {
                        Console.WriteLine("   Failed to connect WebSocket client");
                        return false;
                    }

                    // Step 6: Wait for shortcut to appear in available APIs
                    Console.WriteLine("   Step 6: Waiting for shortcut in available APIs...");
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    Guid? shortcutGuid = null;

                    while (DateTime.UtcNow < deadline)
                    {
                        // Check if we have a shortcut for join_game_test-game
                        shortcutGuid = client.GetServiceGuid("SHORTCUT", "join_game_test-game");
                        if (shortcutGuid.HasValue)
                        {
                            Console.WriteLine($"   Found shortcut: SHORTCUT:join_game_test-game -> {shortcutGuid}");
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

                    // Step 7: Invoke the shortcut with empty payload
                    // Note: Shortcuts use raw InvokeAsync because they're dynamically created
                    Console.WriteLine("   Step 7: Invoking shortcut with empty payload...");
                    var joinResponse = await client.InvokeAsync<object, JsonElement>(
                        "SHORTCUT",
                        "join_game_test-game",
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
                    var sessionId = joinJson?["sessionId"]?.GetValue<string>();

                    Console.WriteLine($"   Join result: sessionId={sessionId}");

                    return !string.IsNullOrEmpty(sessionId);
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
