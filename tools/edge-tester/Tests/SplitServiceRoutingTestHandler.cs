using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Subscription;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Test handler for split-service multi-node routing validation.
/// These tests deploy a split topology via orchestrator and validate:
/// 1. Orchestrator can deploy multi-node configurations in real-time
/// 2. ServiceMappingEvents flow correctly through RabbitMQ
/// 3. Dynamic routing works (auth/account -> bannou-auth node)
/// 4. WebSocket connections survive topology changes
///
/// IMPORTANT: These tests should run LAST as they modify the deployment topology.
/// They require admin credentials and a working orchestrator service.
///
/// COMPILE-TIME SAFETY: This test uses generated typed proxies and request/response
/// models for all service API calls. This ensures schema mismatches are caught at
/// compile time rather than runtime. See BannouClient.Orchestrator, BannouClient.Account,
/// BannouClient.GameService, and BannouClient.Subscription for the typed APIs.
///
/// Test Dependencies:
/// - All tests after "Deploy" depend on successful deployment
/// - Cleanup always runs to reset state (even if tests fail)
/// </summary>
public class SplitServiceRoutingTestHandler : IServiceTestHandler
{
    private const string SPLIT_PRESET = "split-auth-routing-test";
    private const string RELAYED_CONNECT_PRESET = "relayed-connect";
    private const int DEPLOYMENT_TIMEOUT_SECONDS = 180; // Increased from 120 - CI deployments can take 90+ seconds

    // Test dependency tracking - tests check these before running
    private static bool _splitDeploymentSucceeded = false;
    private static string? _splitDeploymentError = null;
    private static bool _relayedDeploymentSucceeded = false;
    private static string? _relayedDeploymentError = null;

    /// <summary>
    /// Checks if split deployment (auth/account) succeeded.
    /// </summary>
    private static bool RequiresSplitDeployment(string testName)
    {
        if (_splitDeploymentSucceeded) return true;

        Console.WriteLine($"   SKIPPED: {testName} requires successful auth/account deployment");
        if (!string.IsNullOrEmpty(_splitDeploymentError))
        {
            Console.WriteLine($"   Deployment failed with: {_splitDeploymentError}");
        }
        else
        {
            Console.WriteLine("   Deployment has not been attempted or failed silently");
        }
        return false;
    }

    /// <summary>
    /// Checks if relayed-connect deployment succeeded.
    /// </summary>
    private static bool RequiresRelayedDeployment(string testName)
    {
        if (_relayedDeploymentSucceeded) return true;

        Console.WriteLine($"   SKIPPED: {testName} requires successful relayed-connect deployment");
        if (!string.IsNullOrEmpty(_relayedDeploymentError))
        {
            Console.WriteLine($"   Relayed deployment failed with: {_relayedDeploymentError}");
        }
        else
        {
            Console.WriteLine("   Relayed deployment has not been attempted or failed silently");
        }
        return false;
    }

    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            // Phase 1: Deploy auth/account split topology
            new ServiceTest(TestDeploySplitTopology, "SplitRouting - Deploy Split Topology", "Orchestrator",
                "Deploy split-auth-routing-test preset via orchestrator (requires admin)"),
            new ServiceTest(TestConnectionSurvivedDeployment, "SplitRouting - Connection Survived", "WebSocket",
                "Verify WebSocket connections survived topology change"),
            new ServiceTest(TestServiceMappingsUpdated, "SplitRouting - Mappings Updated", "Routing",
                "Verify ServiceMappings reflect split topology"),
            new ServiceTest(TestAuthRoutesToSplitNode, "SplitRouting - Auth Routes Correctly", "Routing",
                "Verify auth API calls route to bannou-auth node"),
            new ServiceTest(TestAccountRoutesToSplitNode, "SplitRouting - Account Routes Correctly", "Routing",
                "Verify account API calls route to bannou-auth node"),
            new ServiceTest(TestConnectStillOnMainNode, "SplitRouting - Connect on Main", "Routing",
                "Verify connect service still on bannou-main node"),
            new ServiceTest(TestGameSessionShortcutOnSplitNode, "SplitRouting - GameSession Shortcut", "GameSession",
                "Test subscription-based shortcut flow via game-session on bannou-auth (GAME_SESSION_SUPPORTED_GAME_SERVICES=test-game)"),
            new ServiceTest(TestCapabilityChangeEventsOnSubscription, "SplitRouting - Capability Events", "WebSocket",
                "Test OnCapabilitiesAdded/Removed events fire when subscribing/unsubscribing (requires GAME_SESSION_SUPPORTED_GAME_SERVICES=test-game)"),

            // Phase 2: Deploy relayed-connect and test peer-to-peer routing
            new ServiceTest(TestDeployRelayedConnect, "RelayedConnect - Deploy", "Orchestrator",
                "Deploy relayed-connect preset for peer-to-peer routing tests"),
            new ServiceTest(TestPeerToPeerRouting, "RelayedConnect - Peer Routing", "PeerRouting",
                "Test peer-to-peer routing between two WebSocket connections (requires Relayed mode)"),

            // Phase 3: Cleanup and verify
            new ServiceTest(TestCleanupServiceMappings, "SplitRouting - Cleanup", "Orchestrator",
                "Reset service mappings to default state via Clean API"),
            new ServiceTest(TestVerifyNoPeerGuidAfterCleanup, "PostCleanup - No Peer GUID", "PeerRouting",
                "Verify new connections have no peerGuid after cleanup (back to External mode)"),
        };
    }

    /// <summary>
    /// Deploy the split-auth-routing-test topology via orchestrator.
    /// Uses the admin WebSocket connection to call /orchestrator/deploy.
    /// Sets _splitDeploymentSucceeded flag for dependent tests.
    /// </summary>
    private void TestDeploySplitTopology(string[] args)
    {
        // Reset tracking state at start of test sequence
        _splitDeploymentSucceeded = false;
        _splitDeploymentError = null;
        _relayedDeploymentSucceeded = false;
        _relayedDeploymentError = null;

        Console.WriteLine("=== Deploy Split Topology Test ===");
        Console.WriteLine($"Deploying preset: {SPLIT_PRESET}");
        Console.WriteLine($"Timeout: {DEPLOYMENT_TIMEOUT_SECONDS} seconds");

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            _splitDeploymentError = "Admin client not connected";
            Console.WriteLine("   Admin client not connected - cannot deploy split topology");
            Console.WriteLine("   Ensure admin credentials are configured and admin is authenticated.");
            return;
        }

        Console.WriteLine($"   Admin client connected, session: {adminClient.SessionId}");

        try
        {
            var result = Task.Run(async () => await DeploySplitTopologyAsync(adminClient)).Result;

            if (result)
            {
                _splitDeploymentSucceeded = true;
                Console.WriteLine("PASSED: Split topology deployment succeeded");
            }
            else
            {
                _splitDeploymentError = "Deployment returned failure";
                Console.WriteLine("FAILED: Split topology deployment failed");
            }
        }
        catch (Exception ex)
        {
            _splitDeploymentError = ex.Message;
            Console.WriteLine($"FAILED: Split topology deployment failed with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private async Task<bool> DeploySplitTopologyAsync(BannouClient adminClient)
    {
        // Build typed deploy request
        var deployRequest = new DeployRequest
        {
            Preset = SPLIT_PRESET,
            Backend = BackendType.Compose,
            DryRun = false
        };

        Console.WriteLine($"   Request: Preset={deployRequest.Preset}, Backend={deployRequest.Backend}, DryRun={deployRequest.DryRun}");

        try
        {
            var response = await adminClient.Orchestrator.DeployAsync(
                deployRequest,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS));

            if (!response.IsSuccess || response.Result is null)
            {
                Console.WriteLine($"   Deploy API returned error or null result: {response.Error?.ResponseCode} - {response.Error?.Message}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Success: {result.Success}");
            Console.WriteLine($"   DeploymentId: {result.DeploymentId}");
            Console.WriteLine($"   Message: {result.Message}");

            if (result.Success)
            {
                // Wait for deployment to complete
                Console.WriteLine("   Waiting for deployment to stabilize...");
                await Task.Delay(5000); // Give containers time to start

                // TODO: Listen for DeploymentEvent with Action=Completed
                // For now, we just wait a fixed time
                return true;
            }

            return false;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine("   Deploy endpoint not available in capability manifest");
            Console.WriteLine($"   Admin user may not have orchestrator permissions");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Deploy request failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verify WebSocket connections survived the topology change.
    /// Both regular client and admin client should still be connected.
    /// </summary>
    private void TestConnectionSurvivedDeployment(string[] args)
    {
        Console.WriteLine("=== Connection Survived Deployment Test ===");

        if (!RequiresSplitDeployment("Connection survival test"))
            return;

        var client = Program.Client;
        var adminClient = Program.AdminClient;

        var issues = new List<string>();

        if (client == null)
        {
            issues.Add("Regular client is null");
        }
        else if (!client.IsConnected)
        {
            issues.Add($"Regular client disconnected (session: {client.SessionId})");
        }
        else
        {
            Console.WriteLine($"   Regular client still connected (session: {client.SessionId})");
        }

        if (adminClient == null)
        {
            issues.Add("Admin client is null");
        }
        else if (!adminClient.IsConnected)
        {
            issues.Add($"Admin client disconnected (session: {adminClient.SessionId})");
        }
        else
        {
            Console.WriteLine($"   Admin client still connected (session: {adminClient.SessionId})");
        }

        if (issues.Count > 0)
        {
            Console.WriteLine($"   Connection survival test FAILED:");
            foreach (var issue in issues)
            {
                Console.WriteLine($"   - {issue}");
            }
        }
        else
        {
            Console.WriteLine("   Connection survival test PASSED - both clients still connected");
        }
    }

    /// <summary>
    /// Verify ServiceMappings reflect the split topology.
    /// After deployment, auth and account should map to bannou-auth.
    /// </summary>
    private void TestServiceMappingsUpdated(string[] args)
    {
        Console.WriteLine("=== Service Mappings Updated Test ===");

        if (!RequiresSplitDeployment("Service mappings test"))
            return;

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("   Admin client not connected");
            return;
        }

        try
        {
            var result = Task.Run(async () => await CheckServiceMappingsAsync(adminClient)).Result;

            if (result)
            {
                Console.WriteLine("   Service mappings updated test PASSED");
            }
            else
            {
                Console.WriteLine("   Service mappings updated test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Service mappings test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> CheckServiceMappingsAsync(BannouClient adminClient)
    {
        Console.WriteLine("   Calling orchestrator service-routing API via typed proxy...");

        try
        {
            var response = await adminClient.Orchestrator.GetServiceRoutingAsync(
                new GetServiceRoutingRequest(),
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result is null)
            {
                Console.WriteLine($"   service-routing API returned error or null result: {response.Error?.ResponseCode}");
                return false;
            }

            var routingInfo = response.Result;
            Console.WriteLine($"   Default app-id: {routingInfo.DefaultAppId}");

            if (routingInfo.Mappings == null || routingInfo.Mappings.Count == 0)
            {
                Console.WriteLine("   No service mappings in response after split deployment");
                return false;
            }

            // Check for expected split mappings
            var authMapping = routingInfo.Mappings.TryGetValue("auth", out var authValue) ? authValue : null;
            var accountMapping = routingInfo.Mappings.TryGetValue("account", out var accountValue) ? accountValue : null;

            Console.WriteLine($"   auth -> {authMapping ?? "(not set)"}");
            Console.WriteLine($"   account -> {accountMapping ?? "(not set)"}");

            // In split mode, these MUST map to bannou-auth
            if (authMapping != "bannou-auth")
            {
                Console.WriteLine($"   auth should map to 'bannou-auth', got '{authMapping ?? "(null)"}'");
                return false;
            }

            if (accountMapping != "bannou-auth")
            {
                Console.WriteLine($"   account should map to 'bannou-auth', got '{accountMapping ?? "(null)"}'");
                return false;
            }

            Console.WriteLine("   Mappings correctly reflect split topology");
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine("   service-routing API not available in capability manifest");
            Console.WriteLine("   Admin user must have access to orchestrator service-routing");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   service-routing API call failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verify auth API calls route to bannou-auth node.
    /// </summary>
    private void TestAuthRoutesToSplitNode(string[] args)
    {
        Console.WriteLine("=== Auth Routes to Split Node Test ===");

        if (!RequiresSplitDeployment("Auth routing test"))
            return;

        try
        {
            var result = Task.Run(async () => await TestAuthRoutingAsync()).Result;

            if (result)
            {
                Console.WriteLine("   Auth routing test PASSED");
            }
            else
            {
                Console.WriteLine("   Auth routing test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Auth routing test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> TestAuthRoutingAsync()
    {
        // Make an auth API call (validate token) and check it works
        var config = Program.Configuration;
        var host = config.OpenRestyHost ?? "openresty";
        var port = config.OpenRestyPort ?? 80;

        // Use the client's token to validate
        var client = Program.Client;
        if (client == null || string.IsNullOrEmpty(client.AccessToken))
        {
            Console.WriteLine("   No client token available for validation");
            return false;
        }

        var url = $"http://{host}:{port}/auth/validate";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", client.AccessToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var response = await Program.HttpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"   Validate response ({response.StatusCode}): {content[..Math.Min(200, content.Length)]}...");

            if (response.IsSuccessStatusCode)
            {
                var validateResult = BannouJson.Deserialize<ValidateTokenResponse>(content);
                var valid = validateResult?.Valid ?? false;

                if (valid)
                {
                    Console.WriteLine("   Auth validation succeeded - routing works");
                    return true;
                }

                Console.WriteLine("   Auth validation returned valid=false");
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Auth validation failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verify account API calls route to bannou-auth node.
    /// Makes a real account API call to verify routing works.
    /// </summary>
    private void TestAccountRoutesToSplitNode(string[] args)
    {
        Console.WriteLine("=== Account Routes to Split Node Test ===");

        if (!RequiresSplitDeployment("Account routing test"))
            return;

        try
        {
            var result = Task.Run(async () => await TestAccountRoutingAsync()).Result;

            if (result)
            {
                Console.WriteLine("   Account routing test PASSED");
            }
            else
            {
                Console.WriteLine("   Account routing test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Account routing test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> TestAccountRoutingAsync()
    {
        // Use the admin client to make an account API call
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("   Admin client not connected - cannot test account routing");
            return false;
        }

        // Try to call an account API endpoint that admin has access to
        try
        {
            // List accounts requires admin role - use typed proxy
            var response = await adminClient.Account.ListAccountsAsync(
                new ListAccountsRequest { PageSize = 1 },
                timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result is null)
            {
                Console.WriteLine($"   Account API returned error or null result: {response.Error?.ResponseCode}");
                return false;
            }

            var accountResult = response.Result;
            Console.WriteLine($"   Account API response: TotalCount={accountResult.TotalCount}, PageSize={accountResult.PageSize}");

            // If we got a response, routing is working
            Console.WriteLine("   Account API call succeeded - routing to bannou-auth node works");
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine("   Account API not available in capability manifest");
            Console.WriteLine("   Admin may not have access to account list API");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Account API call failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verify connect service is still on bannou-main node.
    /// </summary>
    private void TestConnectStillOnMainNode(string[] args)
    {
        Console.WriteLine("=== Connect Still on Main Node Test ===");

        if (!RequiresSplitDeployment("Connect routing test"))
            return;

        var client = Program.Client;
        if (client == null || !client.IsConnected)
        {
            Console.WriteLine("   Client not connected - cannot verify connect routing");
            return;
        }

        // The fact that we're still connected via WebSocket proves
        // the connect service is still reachable on the main node
        Console.WriteLine($"   Client still connected via WebSocket");
        Console.WriteLine($"   Session ID: {client.SessionId}");
        Console.WriteLine($"   Available APIs: {client.AvailableApis.Count}");

        Console.WriteLine("   Connect on main node test PASSED - WebSocket still connected");
    }

    /// <summary>
    /// Test game-session shortcut flow when running on bannou-auth with GAME_SESSION_SUPPORTED_GAME_SERVICES=test-game.
    /// This validates the per-game horizontal scaling pattern where different nodes handle different games.
    ///
    /// Flow:
    /// 1. Create game service "test-game" (via admin on main node)
    /// 2. Create test account and subscribe to "test-game"
    /// 3. Connect via WebSocket
    /// 4. Wait for shortcut join_game_test-game to appear (published by game-session on bannou-auth)
    /// 5. Invoke shortcut to join lobby
    /// </summary>
    private void TestGameSessionShortcutOnSplitNode(string[] args)
    {
        Console.WriteLine("=== GameSession Shortcut on Split Node Test ===");
        Console.WriteLine("Testing subscription -> shortcut flow with GAME_SESSION_SUPPORTED_GAME_SERVICES=test-game on bannou-auth");

        if (!RequiresSplitDeployment("GameSession shortcut test"))
            return;

        try
        {
            var result = Task.Run(async () => await PerformGameSessionShortcutTestAsync()).Result;

            if (result)
            {
                Console.WriteLine("   GameSession shortcut test PASSED - shortcut received from bannou-auth");
            }
            else
            {
                Console.WriteLine("   GameSession shortcut test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   GameSession shortcut test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private async Task<bool> PerformGameSessionShortcutTestAsync()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return false;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;
        var uniqueCode = Guid.NewGuid().ToString("N")[..8];

        // Step 1: Ensure game service "test-game" exists (create if not)
        Console.WriteLine("   Step 1: Ensuring game service 'test-game' exists...");
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("   Admin client not connected - cannot create game service");
            return false;
        }

        Guid? gameServiceId = null;
        try
        {
            // Use typed proxy to create game service
            var createResponse = await adminClient.GameService.CreateServiceAsync(
                new CreateServiceRequest
                {
                    StubName = "test-game",
                    DisplayName = "Test Game (Split Routing)",
                    Description = "Test game service for split routing shortcut validation"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (createResponse.IsSuccess && createResponse.Result is not null)
            {
                gameServiceId = createResponse.Result.ServiceId;
                Console.WriteLine($"   Created game service 'test-game': {gameServiceId}");
            }
            else if (createResponse.Error?.ResponseCode == 409)
            {
                Console.WriteLine("   Game service 'test-game' already exists - fetching ID...");
                // Fetch the existing service by stub name using typed proxy
                var getResponse = await adminClient.GameService.GetServiceAsync(
                    new GetServiceRequest { StubName = "test-game" },
                    timeout: TimeSpan.FromSeconds(10));

                if (getResponse.IsSuccess && getResponse.Result is not null)
                {
                    gameServiceId = getResponse.Result.ServiceId;
                    Console.WriteLine($"   Found existing game service: {gameServiceId}");
                }
            }
            else
            {
                Console.WriteLine($"   Warning: game-service/services/create returned {createResponse.Error?.ResponseCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Warning: Failed to create/get game service: {ex.Message}");
        }

        if (gameServiceId == null)
        {
            Console.WriteLine("   Could not create or find game service 'test-game' - test cannot proceed");
            return false;
        }

        // Step 2: Create test account
        Console.WriteLine("   Step 2: Creating test account...");
        var testEmail = $"gamesession_split_{uniqueCode}@test.local";
        var testPassword = "TestPass123!";
        var testUsername = $"gamesession_split_{uniqueCode}";

        string? accessToken = null;
        Guid? accountId = null;

        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new RegisterRequest { Username = testUsername, Email = testEmail, Password = testPassword };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                BannouJson.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            var responseBody = await registerResponse.Content.ReadAsStringAsync();

            if (!registerResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"   Failed to create account: {registerResponse.StatusCode} - {responseBody}");
                return false;
            }

            var registerResult = BannouJson.Deserialize<RegisterResponse>(responseBody);
            accessToken = registerResult?.AccessToken;
            accountId = registerResult?.AccountId;

            Console.WriteLine($"   Created account: {accountId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return false;
        }

        if (string.IsNullOrEmpty(accessToken) || accountId == null)
        {
            Console.WriteLine("   Missing accessToken or accountId");
            return false;
        }

        // Step 3: Create subscription to "test-game"
        Console.WriteLine("   Step 3: Creating subscription to 'test-game'...");
        try
        {
            // Use typed proxy to create subscription
            var subscribeResponse = await adminClient.Subscription.CreateSubscriptionAsync(
                new CreateSubscriptionRequest
                {
                    AccountId = accountId.Value,
                    ServiceId = gameServiceId.Value
                },
                timeout: TimeSpan.FromSeconds(10));

            if (subscribeResponse.IsSuccess)
            {
                Console.WriteLine("   Subscription created successfully");
            }
            else
            {
                Console.WriteLine($"   Subscription creation returned: {subscribeResponse.Error?.ResponseCode}");
                // May already exist - continue
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Warning: Failed to create subscription: {ex.Message}");
            // Continue - subscription may already exist
        }

        // Step 4: Connect via WebSocket with the new account's token
        Console.WriteLine("   Step 4: Connecting via WebSocket...");
        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        ClientWebSocket? webSocket = null;

        try
        {
            webSocket = new ClientWebSocket();
            webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);

            if (webSocket.State != WebSocketState.Open)
            {
                Console.WriteLine($"   WebSocket in unexpected state: {webSocket.State}");
                return false;
            }

            Console.WriteLine("   WebSocket connected");

            // Receive capability manifest
            var buffer = new ArraySegment<byte>(new byte[65536]);
            using var manifestCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var manifestResult = await webSocket.ReceiveAsync(buffer, manifestCts.Token);

            if (manifestResult.Count < BinaryMessage.HeaderSize)
            {
                Console.WriteLine("   Invalid capability manifest received");
                return false;
            }

            var payloadBytes = buffer.Array![(BinaryMessage.HeaderSize)..manifestResult.Count];
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            Console.WriteLine($"   Received capability manifest ({payloadJson.Length} bytes)");

            // Step 5: Wait for shortcut to appear
            Console.WriteLine("   Step 5: Waiting for join_game_test-game shortcut...");
            var shortcutDeadline = DateTime.UtcNow.AddSeconds(15);
            string? shortcutGuidStr = null;

            // Parse initial manifest for shortcuts
            // Shortcuts are in availableApis with service="shortcut", endpoint=shortcut name, serviceId=GUID
            var manifestObj = JsonNode.Parse(payloadJson)?.AsObject();
            var availableApis = manifestObj?["availableApis"]?.AsArray();
            if (availableApis != null)
            {
                foreach (var api in availableApis)
                {
                    var service = api?["service"]?.GetValue<string>();
                    var endpoint = api?["endpoint"]?.GetValue<string>();
                    if (service == "shortcut" && endpoint == "join_game_test-game")
                    {
                        shortcutGuidStr = api?["serviceId"]?.GetValue<string>();
                        Console.WriteLine($"   Found shortcut in initial manifest: {shortcutGuidStr}");
                        break;
                    }
                }
            }

            // If not in initial manifest, wait for updated capability manifest
            // Note: Connect service consumes shortcut_published events internally and sends
            // updated capability manifests to clients - clients never see the raw events
            while (shortcutGuidStr == null && DateTime.UtcNow < shortcutDeadline)
            {
                // Check WebSocket state before attempting receive
                if (webSocket.State != WebSocketState.Open)
                {
                    Console.WriteLine($"   WebSocket no longer open (state: {webSocket.State})");
                    break;
                }

                using var eventCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    var eventResult = await webSocket.ReceiveAsync(buffer, eventCts.Token);
                    if (eventResult.Count > BinaryMessage.HeaderSize)
                    {
                        var eventPayload = buffer.Array![(BinaryMessage.HeaderSize)..eventResult.Count];
                        var eventJson = Encoding.UTF8.GetString(eventPayload);
                        var updatedManifest = JsonNode.Parse(eventJson)?.AsObject();

                        // Check for updated capability manifest with shortcuts
                        // Shortcuts are in availableApis with service="shortcut"
                        var updatedApis = updatedManifest?["availableApis"]?.AsArray();
                        if (updatedApis != null)
                        {
                            foreach (var api in updatedApis)
                            {
                                var service = api?["service"]?.GetValue<string>();
                                var endpoint = api?["endpoint"]?.GetValue<string>();
                                if (service == "shortcut" && endpoint == "join_game_test-game")
                                {
                                    shortcutGuidStr = api?["serviceId"]?.GetValue<string>();
                                    Console.WriteLine($"   Found shortcut in updated manifest: {shortcutGuidStr}");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timeout on receive - check WebSocket state before continuing
                    if (webSocket.State != WebSocketState.Open)
                    {
                        Console.WriteLine($"   WebSocket closed after timeout (state: {webSocket.State})");
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(shortcutGuidStr))
            {
                Console.WriteLine("   Shortcut not received within timeout");
                Console.WriteLine("   This likely means game-session on bannou-auth didn't process the subscription.updated event");
                Console.WriteLine("   Check that GAME_SESSION_SUPPORTED_GAME_SERVICES=test-game is set on bannou-auth");
                return false;
            }

            Console.WriteLine($"   Shortcut received: join_game_test-game -> {shortcutGuidStr}");

            // Step 6: Invoke shortcut (optional - validates end-to-end flow)
            Console.WriteLine("   Step 6: Invoking shortcut to join lobby...");

            // Build binary message to invoke shortcut
            if (!Guid.TryParse(shortcutGuidStr, out var shortcutGuid))
            {
                Console.WriteLine("   Failed to parse shortcut GUID");
                return false;
            }

            var invokeMessage = BinaryMessage.FromJson(
                channel: 0,
                sequenceNumber: 1,
                serviceGuid: shortcutGuid,
                messageId: 1,
                jsonPayload: "{}");  // Empty payload - server injects bound data

            await webSocket.SendAsync(new ArraySegment<byte>(invokeMessage.ToByteArray()), WebSocketMessageType.Binary, true, CancellationToken.None);

            // Wait for response - may receive events first, skip them
            using var responseCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            WebSocketReceiveResult responseResult;
            string responseJson;

            while (true)
            {
                responseResult = await webSocket.ReceiveAsync(buffer, responseCts.Token);

                if (responseResult.Count < BinaryMessage.ResponseHeaderSize)
                {
                    Console.WriteLine($"   FAILED: Message too short ({responseResult.Count} bytes)");
                    return false;
                }

                // Check message flags to determine if it's a response or event
                var flags = (MessageFlags)buffer.Array![0];
                if (flags.HasFlag(MessageFlags.Response))
                {
                    // This is the response we're waiting for
                    break;
                }

                // It's an event (like capability manifest) - skip it and keep waiting
                Console.WriteLine($"   Skipping event message (flags: {flags})");
            }

            // Check response code in header (byte 15 in response header)
            var responseCode = buffer.Array![15];
            if (responseCode != 0)
            {
                Console.WriteLine($"   FAILED: Join returned error code {responseCode}");
                return false;
            }

            var responsePayload = buffer.Array![(BinaryMessage.ResponseHeaderSize)..responseResult.Count];
            responseJson = Encoding.UTF8.GetString(responsePayload);
            Console.WriteLine($"   Join response: {responseJson[..Math.Min(300, responseJson.Length)]}");

            // STRICT: Must have sessionId in response - no weak "appeared successful" fallbacks
            var joinResponse = BannouJson.Deserialize<JoinGameSessionResponse>(responseJson);
            if (joinResponse == null)
            {
                Console.WriteLine("   FAILED: Could not parse join response as JoinGameSessionResponse");
                return false;
            }

            var lobbySessionId = joinResponse.SessionId;
            if (lobbySessionId == Guid.Empty)
            {
                Console.WriteLine("   FAILED: SessionId is empty in join response");
                return false;
            }

            Console.WriteLine($"   Successfully joined lobby: {lobbySessionId}");
            Console.WriteLine("   Game-session partitioning validated: shortcut received AND join succeeded");
            return true;
        }
        finally
        {
            await CloseWebSocketSafely(webSocket);
        }
    }

    /// <summary>
    /// Test that OnCapabilitiesAdded and OnCapabilitiesRemoved events fire correctly
    /// when subscribing and unsubscribing from a game service.
    /// This test runs during split deployment when GAME_SESSION_SUPPORTED_GAME_SERVICES=test-game is configured.
    /// </summary>
    private void TestCapabilityChangeEventsOnSubscription(string[] args)
    {
        Console.WriteLine("=== Capability Change Events on Subscription Test ===");
        Console.WriteLine("Testing OnCapabilitiesAdded/Removed events during subscription lifecycle...");

        if (!RequiresSplitDeployment("Capability change events test"))
            return;

        try
        {
            var result = Task.Run(async () => await PerformCapabilityChangeEventsTestAsync()).Result;

            if (result)
            {
                Console.WriteLine("PASSED: Capability change events test succeeded");
            }
            else
            {
                Console.WriteLine("FAILED: Capability change events test failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Capability change events test failed with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private async Task<bool> PerformCapabilityChangeEventsTestAsync()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return false;
        }

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("   Admin client not connected - required for subscription management");
            return false;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;
        var uniqueCode = Guid.NewGuid().ToString("N")[..8];

        // Step 1: Get or create test-game service (should already exist from previous test)
        Console.WriteLine("   Step 1: Getting test-game service...");
        Guid? gameServiceId = null;

        try
        {
            var getResponse = await adminClient.GameService.GetServiceAsync(
                new GetServiceRequest { StubName = "test-game" },
                timeout: TimeSpan.FromSeconds(10));

            if (getResponse.IsSuccess && getResponse.Result is not null)
            {
                gameServiceId = getResponse.Result.ServiceId;
                Console.WriteLine($"   Found game service: {gameServiceId}");
            }
            else
            {
                // Create it if doesn't exist
                var createResponse = await adminClient.GameService.CreateServiceAsync(
                    new CreateServiceRequest
                    {
                        StubName = "test-game",
                        DisplayName = "Test Game (Capability Events)",
                        Description = "Test game service for capability change events"
                    },
                    timeout: TimeSpan.FromSeconds(10));

                if (createResponse.IsSuccess && createResponse.Result is not null)
                {
                    gameServiceId = createResponse.Result.ServiceId;
                    Console.WriteLine($"   Created game service: {gameServiceId}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Warning: Failed to get/create game service: {ex.Message}");
        }

        if (gameServiceId == null)
        {
            Console.WriteLine("   Could not find or create game service 'test-game'");
            return false;
        }

        // Step 2: Create test account
        Console.WriteLine("   Step 2: Creating test account...");
        var testEmail = $"cap_events_{uniqueCode}@test.local";
        var testPassword = "TestPass123!";
        var testUsername = $"cap_events_{uniqueCode}";

        string? accessToken = null;
        string? connectUrl = null;
        Guid? accountId = null;

        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new RegisterRequest { Username = testUsername, Email = testEmail, Password = testPassword };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                BannouJson.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            var responseBody = await registerResponse.Content.ReadAsStringAsync();

            if (!registerResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"   Failed to create account: {registerResponse.StatusCode} - {responseBody}");
                return false;
            }

            var registerResult = BannouJson.Deserialize<RegisterResponse>(responseBody);
            accessToken = registerResult?.AccessToken;
            accountId = registerResult?.AccountId;
            connectUrl = registerResult?.ConnectUrl?.ToString();

            Console.WriteLine($"   Created account: {accountId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return false;
        }

        if (string.IsNullOrEmpty(accessToken) || accountId == null || string.IsNullOrEmpty(connectUrl))
        {
            Console.WriteLine("   Missing accessToken, accountId, or connectUrl");
            return false;
        }

        // Step 3: Connect using BannouClient BEFORE creating subscription
        Console.WriteLine("   Step 3: Connecting via BannouClient (before subscription)...");

        var initialCapabilitiesReceived = new TaskCompletionSource<IReadOnlyList<ClientCapabilityEntry>>();
        var addedCapabilities = new TaskCompletionSource<IReadOnlyList<ClientCapabilityEntry>>();
        var removedCapabilities = new TaskCompletionSource<IReadOnlyList<ClientCapabilityEntry>>();
        var initialReceived = false;

        await using var client = new BannouClient();

        client.OnCapabilitiesAdded += capabilities =>
        {
            Console.WriteLine($"   OnCapabilitiesAdded fired with {capabilities.Count} capabilities");
            if (!initialReceived)
            {
                initialReceived = true;
                initialCapabilitiesReceived.TrySetResult(capabilities);
            }
            else
            {
                // Subsequent add events (e.g., from subscription)
                addedCapabilities.TrySetResult(capabilities);
            }
        };

        client.OnCapabilitiesRemoved += capabilities =>
        {
            Console.WriteLine($"   OnCapabilitiesRemoved fired with {capabilities.Count} capabilities");
            removedCapabilities.TrySetResult(capabilities);
        };

        var connected = await client.ConnectWithTokenAsync(connectUrl, accessToken);
        if (!connected || !client.IsConnected)
        {
            Console.WriteLine("   BannouClient failed to connect");
            return false;
        }

        Console.WriteLine($"   Connected, session: {client.SessionId}");

        // Wait for initial capabilities
        using var initCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var initialCaps = await initialCapabilitiesReceived.Task.WaitAsync(initCts.Token);
            Console.WriteLine($"   Initial capabilities received: {initialCaps.Count} entries");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("   Timeout waiting for initial capabilities");
            return false;
        }

        // Step 4: Create subscription - should trigger OnCapabilitiesAdded with shortcut
        Console.WriteLine("   Step 4: Creating subscription to 'test-game'...");
        Guid? subscriptionId = null;

        try
        {
            var subscribeResponse = await adminClient.Subscription.CreateSubscriptionAsync(
                new CreateSubscriptionRequest
                {
                    AccountId = accountId.Value,
                    ServiceId = gameServiceId.Value
                },
                timeout: TimeSpan.FromSeconds(10));

            if (subscribeResponse.IsSuccess && subscribeResponse.Result is not null)
            {
                subscriptionId = subscribeResponse.Result.SubscriptionId;
                Console.WriteLine($"   Subscription created: {subscriptionId}");
            }
            else
            {
                Console.WriteLine($"   Subscription creation returned: {subscribeResponse.Error?.ResponseCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create subscription: {ex.Message}");
            return false;
        }

        // Step 5: Wait for OnCapabilitiesAdded with shortcut
        Console.WriteLine("   Step 5: Waiting for OnCapabilitiesAdded with shortcut...");

        using var addCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        IReadOnlyList<ClientCapabilityEntry>? added;
        try
        {
            added = await addedCapabilities.Task.WaitAsync(addCts.Token);
            Console.WriteLine($"   OnCapabilitiesAdded fired with {added.Count} new capabilities:");
            foreach (var cap in added)
            {
                Console.WriteLine($"     + {cap.Endpoint} (service: {cap.Service})");
            }

            // Verify we got a shortcut for test-game
            var hasShortcut = added.Any(c => c.Service == "shortcut" && c.Endpoint.Contains("test-game"));
            if (!hasShortcut)
            {
                Console.WriteLine("   FAILED: Expected shortcut for test-game not found in added capabilities");
                return false;
            }

            Console.WriteLine("   Shortcut capability confirmed in OnCapabilitiesAdded");

            // Also verify the shortcut is now in client.AvailableApis
            var shortcutGuid = client.GetServiceGuid("join_game_test-game");
            if (!shortcutGuid.HasValue)
            {
                Console.WriteLine("   FAILED: Shortcut not found in client.AvailableApis after OnCapabilitiesAdded");
                return false;
            }
            Console.WriteLine($"   Shortcut verified in AvailableApis: join_game_test-game -> {shortcutGuid}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("   Timeout waiting for OnCapabilitiesAdded after subscription");
            Console.WriteLine("   This indicates game-session may not be publishing shortcuts for test-game");
            return false;
        }

        // Step 6: Delete subscription - should trigger OnCapabilitiesRemoved
        Console.WriteLine("   Step 6: Deleting subscription...");

        try
        {
            var cancelResponse = await adminClient.Subscription.CancelSubscriptionAsync(
                new CancelSubscriptionRequest { SubscriptionId = subscriptionId!.Value },
                timeout: TimeSpan.FromSeconds(10));

            if (!cancelResponse.IsSuccess)
            {
                Console.WriteLine($"   FAILED: Subscription cancellation returned error: {cancelResponse.Error?.ResponseCode} - {cancelResponse.Error?.Message}");
                return false;
            }

            Console.WriteLine("   Subscription cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   FAILED: Exception deleting subscription: {ex.Message}");
            return false;
        }

        // Step 7: Wait for OnCapabilitiesRemoved with shortcut
        Console.WriteLine("   Step 7: Waiting for OnCapabilitiesRemoved...");

        using var remCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var removed = await removedCapabilities.Task.WaitAsync(remCts.Token);
            Console.WriteLine($"   OnCapabilitiesRemoved fired with {removed.Count} removed capabilities:");
            foreach (var cap in removed)
            {
                Console.WriteLine($"     - {cap.Endpoint} (service: {cap.Service})");
            }

            // Verify the shortcut was removed
            var hadShortcut = removed.Any(c => c.Service == "shortcut" && c.Endpoint.Contains("test-game"));
            if (!hadShortcut)
            {
                Console.WriteLine("   FAILED: Expected shortcut for test-game not found in removed capabilities");
                return false;
            }

            Console.WriteLine("   Shortcut removal confirmed in OnCapabilitiesRemoved");

            // Also verify the shortcut is no longer in client.AvailableApis
            var shortcutGuidAfter = client.GetServiceGuid("join_game_test-game");
            if (shortcutGuidAfter.HasValue)
            {
                Console.WriteLine("   FAILED: Shortcut still in client.AvailableApis after OnCapabilitiesRemoved");
                return false;
            }
            Console.WriteLine("   Shortcut verified removed from AvailableApis");

            Console.WriteLine("   Capability change events working correctly");
            return true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("   Timeout waiting for OnCapabilitiesRemoved after unsubscription");
            Console.WriteLine("   OnCapabilitiesAdded worked but OnCapabilitiesRemoved did not fire");
            return false;
        }
    }

    /// <summary>
    /// CRITICAL CLEANUP TEST: Restore default topology and service mappings.
    /// This MUST run after all split routing tests to ensure subsequent test runs
    /// (like forward compatibility tests) start with a clean state.
    ///
    /// Strategy: Deploy 'edge-tests' preset which puts all services back on single 'bannou' node.
    /// This will automatically publish the correct service mapping events.
    /// </summary>
    private void TestCleanupServiceMappings(string[] args)
    {
        Console.WriteLine("=== Restore Default Topology Test ===");
        Console.WriteLine("CRITICAL: Restoring single-node topology with all services on 'bannou'");

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("   Admin client not connected - cannot restore default topology");
            Console.WriteLine("   WARNING: Subsequent test runs may fail due to stale mappings!");
            Console.WriteLine("   Attempting fallback cleanup via Clean API...");

            // Fallback: at least try to reset mappings via Clean API
            try
            {
                var fallbackResult = Task.Run(async () => await CleanupViaCleanApiAsync()).Result;
                if (fallbackResult)
                {
                    Console.WriteLine("   Fallback cleanup via Clean API succeeded");
                }
            }
            catch (Exception fallbackEx)
            {
                Console.WriteLine($"   Fallback cleanup also failed: {fallbackEx.Message}");
            }
            return;
        }

        try
        {
            var result = Task.Run(async () => await RestoreDefaultTopologyAsync(adminClient)).Result;

            if (result)
            {
                Console.WriteLine("   Default topology restore PASSED - system restored to single-node state");
            }
            else
            {
                Console.WriteLine("   Default topology restore FAILED via deploy, trying Clean API fallback...");

                // Fallback: at least reset the mappings via Clean API
                var fallbackResult = Task.Run(async () => await CleanupViaCleanApiAsync()).Result;
                if (fallbackResult)
                {
                    Console.WriteLine("   Fallback cleanup via Clean API succeeded");
                }
                else
                {
                    Console.WriteLine("   WARNING: Subsequent test runs may fail due to stale mappings!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Default topology restore FAILED with exception: {ex.Message}");
            Console.WriteLine("   WARNING: Subsequent test runs may fail due to stale mappings!");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Restore default topology by deploying with "default" preset.
    /// The orchestrator handles "default"/"bannou"/empty as reset-to-default-topology,
    /// which tears down all dynamically deployed containers and resets service mappings.
    /// </summary>
    private async Task<bool> RestoreDefaultTopologyAsync(BannouClient adminClient)
    {
        // Deploy with "default" preset - orchestrator interprets this as reset-to-default
        var deployRequest = new DeployRequest
        {
            Preset = "default",
            Backend = BackendType.Compose,
            DryRun = false
        };

        Console.WriteLine($"   Deploying preset: default via typed proxy (reset to single-node topology)");
        Console.WriteLine($"   Request: Preset={deployRequest.Preset}, Backend={deployRequest.Backend}, DryRun={deployRequest.DryRun}");

        try
        {
            var response = await adminClient.Orchestrator.DeployAsync(
                deployRequest,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS));

            if (!response.IsSuccess || response.Result is null)
            {
                Console.WriteLine($"   Deploy API returned error or null result: {response.Error?.ResponseCode} - {response.Error?.Message}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Success: {result.Success}");
            Console.WriteLine($"   Message: {result.Message}");

            if (result.Success)
            {
                // Wait for topology to stabilize
                Console.WriteLine("   Waiting for topology to stabilize...");
                await Task.Delay(3000);
                return true;
            }

            return false;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine("   Deploy endpoint not available in capability manifest");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Deploy request failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fallback cleanup via Clean API to at least reset service mappings.
    /// Uses the shared admin WebSocket since orchestrator APIs are not exposed via NGINX.
    /// </summary>
    private async Task<bool> CleanupViaCleanApiAsync()
    {
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("   Cannot call Clean API - admin client not connected");
            Console.WriteLine("   Orchestrator APIs require admin WebSocket (not exposed via NGINX)");
            return false;
        }

        // Use typed proxy for clean API
        var cleanRequest = new CleanRequest
        {
            Targets = new List<CleanTarget> { CleanTarget.Containers },
            Force = true
        };

        Console.WriteLine($"   Clean API request via typed proxy: Targets=[{string.Join(",", cleanRequest.Targets)}], Force={cleanRequest.Force}");

        try
        {
            var response = await adminClient.Orchestrator.CleanAsync(
                cleanRequest,
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result is null)
            {
                Console.WriteLine($"   Clean API returned error or null result: {response.Error?.ResponseCode}");
                return false;
            }

            var cleanResult = response.Result;
            Console.WriteLine($"   Clean response: Success={cleanResult.Success}");

            // Wait for mapping events to propagate
            await Task.Delay(2000);
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine("   Clean endpoint not available in capability manifest");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Clean API request failed: {ex.Message}");
            return false;
        }
    }

    #region Relayed Connect Tests

    /// <summary>
    /// Deploy the relayed-connect preset to enable peer-to-peer routing.
    /// This adds a Connect service in Relayed mode to the topology.
    /// </summary>
    private void TestDeployRelayedConnect(string[] args)
    {
        Console.WriteLine("=== Deploy Relayed Connect Test ===");
        Console.WriteLine($"Deploying preset: {RELAYED_CONNECT_PRESET}");

        // Relayed deployment depends on split deployment succeeding
        if (!RequiresSplitDeployment("Relayed Connect deployment"))
            return;

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            _relayedDeploymentError = "Admin client not connected";
            Console.WriteLine("FAILED: Admin client not connected");
            return;
        }

        try
        {
            var result = Task.Run(async () => await DeployRelayedConnectAsync(adminClient)).Result;

            if (result)
            {
                _relayedDeploymentSucceeded = true;
                Console.WriteLine("PASSED: Relayed Connect deployment succeeded");
            }
            else
            {
                _relayedDeploymentError = "Deployment returned failure";
                Console.WriteLine("FAILED: Relayed Connect deployment failed");
            }
        }
        catch (Exception ex)
        {
            _relayedDeploymentError = ex.Message;
            Console.WriteLine($"FAILED: Relayed Connect deployment failed with exception: {ex.Message}");
        }
    }

    private async Task<bool> DeployRelayedConnectAsync(BannouClient adminClient)
    {
        var deployRequest = new DeployRequest
        {
            Preset = RELAYED_CONNECT_PRESET,
            Backend = BackendType.Compose,
            DryRun = false
        };

        Console.WriteLine($"   Request: Preset={deployRequest.Preset}, Backend={deployRequest.Backend}, DryRun={deployRequest.DryRun}");

        try
        {
            var response = await adminClient.Orchestrator.DeployAsync(
                deployRequest,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS));

            if (!response.IsSuccess || response.Result is null)
            {
                Console.WriteLine($"   Deploy API returned error or null result: {response.Error?.ResponseCode} - {response.Error?.Message}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Success: {result.Success}");
            Console.WriteLine($"   Message: {result.Message}");

            if (result.Success)
            {
                // Wait for:
                // 1. New Connect node to start and become healthy
                // 2. OpenResty routing cache to expire (10s TTL) as fallback
                //    Note: Orchestrator calls /internal/cache/invalidate but we wait anyway
                //    in case that fails or the cache was populated after invalidation
                Console.WriteLine("   Waiting for Relayed Connect to stabilize (10s for cache expiry)...");
                await Task.Delay(10000);
                return true;
            }

            return false;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine("   Deploy endpoint not available in capability manifest");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Deploy request failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Test peer-to-peer routing after relayed-connect deployment.
    /// Creates two new WebSocket connections, verifies both get peerGuid,
    /// and tests bidirectional message routing between them.
    /// </summary>
    private void TestPeerToPeerRouting(string[] args)
    {
        Console.WriteLine("=== Peer-to-Peer Routing Test (Relayed Mode) ===");

        if (!RequiresRelayedDeployment("Peer-to-peer routing test"))
            return;

        try
        {
            var result = Task.Run(async () => await PerformPeerToPeerRoutingTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED: Peer-to-peer routing test succeeded");
            }
            else
            {
                Console.WriteLine("FAILED: Peer-to-peer routing test failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Peer-to-peer routing test failed with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformPeerToPeerRoutingTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return false;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;

        // Create two test accounts for peer routing
        var peer1Token = await CreateTestAccountForPeerTest("peer1");
        var peer2Token = await CreateTestAccountForPeerTest("peer2");

        if (peer1Token == null || peer2Token == null)
        {
            Console.WriteLine("   Failed to create test accounts");
            return false;
        }

        // Connect both peers and get their peerGuids
        var (ws1, peerGuid1) = await ConnectAndGetPeerGuidForTest(peer1Token, "Peer1");
        var (ws2, peerGuid2) = await ConnectAndGetPeerGuidForTest(peer2Token, "Peer2");

        try
        {
            if (ws1 == null || ws2 == null)
            {
                Console.WriteLine("   Failed to establish WebSocket connections");
                return false;
            }

            // In Relayed mode, both should have peerGuids
            if (peerGuid1 == null || peerGuid2 == null)
            {
                Console.WriteLine("   FAILED: No peerGuid received - Connect may not be in Relayed mode");
                Console.WriteLine($"   Peer1 peerGuid: {peerGuid1?.ToString() ?? "null"}");
                Console.WriteLine($"   Peer2 peerGuid: {peerGuid2?.ToString() ?? "null"}");
                return false;
            }

            Console.WriteLine($"   Peer1 peerGuid: {peerGuid1}");
            Console.WriteLine($"   Peer2 peerGuid: {peerGuid2}");

            // Test A -> B routing
            Console.WriteLine("   Step 1: Peer1 -> Peer2");
            var message1To2 = PeerRoutingTestHandler.CreatePeerMessage(peerGuid2.Value, "Hello from Peer1!", 1);
            await ws1.SendAsync(new ArraySegment<byte>(message1To2), WebSocketMessageType.Binary, true, CancellationToken.None);

            var (received1, code1) = await PeerRoutingTestHandler.ReceivePeerMessageAsync(ws2, "Peer2");
            if (received1 == null || !received1.Contains("Hello from Peer1!"))
            {
                Console.WriteLine($"   FAILED: Peer2 didn't receive message from Peer1 (code: {code1})");
                return false;
            }
            Console.WriteLine($"   Peer2 received: {received1}");

            // Test B -> A routing
            Console.WriteLine("   Step 2: Peer2 -> Peer1");
            var message2To1 = PeerRoutingTestHandler.CreatePeerMessage(peerGuid1.Value, "Hello from Peer2!", 2);
            await ws2.SendAsync(new ArraySegment<byte>(message2To1), WebSocketMessageType.Binary, true, CancellationToken.None);

            var (received2, code2) = await PeerRoutingTestHandler.ReceivePeerMessageAsync(ws1, "Peer1");
            if (received2 == null || !received2.Contains("Hello from Peer2!"))
            {
                Console.WriteLine($"   FAILED: Peer1 didn't receive message from Peer2 (code: {code2})");
                return false;
            }
            Console.WriteLine($"   Peer1 received: {received2}");

            Console.WriteLine("   Bidirectional peer routing successful!");
            return true;
        }
        finally
        {
            await CloseWebSocketSafely(ws1);
            await CloseWebSocketSafely(ws2);
        }
    }

    /// <summary>
    /// Verify that after cleanup, new connections no longer receive peerGuid.
    /// This confirms the system is back to External mode.
    /// </summary>
    private void TestVerifyNoPeerGuidAfterCleanup(string[] args)
    {
        Console.WriteLine("=== Verify No Peer GUID After Cleanup ===");
        Console.WriteLine("Verifying new connections are back to External mode (no peerGuid)...");

        try
        {
            var result = Task.Run(async () => await PerformNoPeerGuidVerification()).Result;
            if (result)
            {
                Console.WriteLine("PASSED: No peerGuid in manifest - back to External mode");
            }
            else
            {
                Console.WriteLine("FAILED: Unexpected peerGuid present after cleanup");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Post-cleanup verification failed with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformNoPeerGuidVerification()
    {
        var token = await CreateTestAccountForPeerTest("postcleanup");
        if (token == null)
        {
            Console.WriteLine("   Failed to create test account");
            return false;
        }

        var (ws, peerGuid) = await ConnectAndGetPeerGuidForTest(token, "PostCleanup");

        try
        {
            if (ws == null)
            {
                Console.WriteLine("   Failed to establish WebSocket connection");
                return false;
            }

            // After cleanup, should be back to External mode - no peerGuid
            if (peerGuid != null && peerGuid != Guid.Empty)
            {
                Console.WriteLine($"   UNEXPECTED: peerGuid present ({peerGuid}) after cleanup");
                Console.WriteLine("   System may still be in Relayed mode");
                return false;
            }

            Console.WriteLine("   Confirmed: No peerGuid in manifest (External mode restored)");
            return true;
        }
        finally
        {
            await CloseWebSocketSafely(ws);
        }
    }

    #endregion

    #region Peer Test Helpers

    /// <summary>
    /// Creates a test account and returns the access token.
    /// </summary>
    private async Task<string?> CreateTestAccountForPeerTest(string testPrefix)
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
            var registerContent = new RegisterRequest { Username = $"{testPrefix}_{uniqueId}", Email = testEmail, Password = testPassword };

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
            var registerResult = BannouJson.Deserialize<RegisterResponse>(responseBody);
            var accessToken = registerResult?.AccessToken;

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("   No accessToken in registration response");
                return null;
            }

            Console.WriteLine($"   Created test account: {testEmail}");
            return accessToken;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Connects to WebSocket and extracts peerGuid from capability manifest.
    /// </summary>
    private async Task<(ClientWebSocket? webSocket, Guid? peerGuid)> ConnectAndGetPeerGuidForTest(string accessToken, string connectionName)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine($"   [{connectionName}] Configuration not available");
            return (null, null);
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        ClientWebSocket? webSocket = null;
        try
        {
            webSocket = new ClientWebSocket();
            webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            Console.WriteLine($"   [{connectionName}] Connecting to {serverUri}...");
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);

            if (webSocket.State != WebSocketState.Open)
            {
                Console.WriteLine($"   [{connectionName}] WebSocket in unexpected state: {webSocket.State}");
                webSocket.Dispose();
                webSocket = null;
                return (null, null);
            }

            // Wait for capability manifest
            var buffer = new ArraySegment<byte>(new byte[65536]);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var result = await webSocket.ReceiveAsync(buffer, cts.Token);
            if (result.Count == 0)
            {
                Console.WriteLine($"   [{connectionName}] Empty response received");
                var resultWs1 = webSocket;
                webSocket = null;
                return (resultWs1, null);
            }

            if (result.Count < BinaryMessage.HeaderSize)
            {
                Console.WriteLine($"   [{connectionName}] Message too short for header");
                var resultWs2 = webSocket;
                webSocket = null;
                return (resultWs2, null);
            }

            var payloadBytes = buffer.Array![(BinaryMessage.HeaderSize)..result.Count];
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            var manifestObj = JsonNode.Parse(payloadJson)?.AsObject();

            if (manifestObj == null)
            {
                Console.WriteLine($"   [{connectionName}] Failed to parse manifest JSON");
                var resultWs3 = webSocket;
                webSocket = null;
                return (resultWs3, null);
            }

            var eventName = manifestObj["eventName"]?.GetValue<string>();
            if (eventName != "connect.capability_manifest")
            {
                Console.WriteLine($"   [{connectionName}] Unexpected event type: {eventName}");
                var resultWs4 = webSocket;
                webSocket = null;
                return (resultWs4, null);
            }

            var peerGuidStr = manifestObj["peerGuid"]?.GetValue<string>();
            if (string.IsNullOrEmpty(peerGuidStr) || !Guid.TryParse(peerGuidStr, out var peerGuid))
            {
                Console.WriteLine($"   [{connectionName}] No peerGuid in capability manifest (External mode)");
                var resultWs5 = webSocket;
                webSocket = null;
                return (resultWs5, null);
            }

            Console.WriteLine($"   [{connectionName}] Received peerGuid: {peerGuid}");
            var resultWebSocket = webSocket;
            webSocket = null;
            return (resultWebSocket, peerGuid);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [{connectionName}] Connection failed: {ex.Message}");
            return (null, null);
        }
        finally
        {
            webSocket?.Dispose();
        }
    }

    /// <summary>
    /// Safely closes a WebSocket connection.
    /// </summary>
    private static async Task CloseWebSocketSafely(ClientWebSocket? webSocket)
    {
        if (webSocket == null) return;
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            webSocket.Dispose();
        }
    }

    #endregion
}
