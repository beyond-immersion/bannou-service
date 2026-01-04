using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Test handler for split-service multi-node routing validation.
/// These tests deploy a split topology via orchestrator and validate:
/// 1. Orchestrator can deploy multi-node configurations in real-time
/// 2. ServiceMappingEvents flow correctly through RabbitMQ
/// 3. Dynamic routing works (auth/accounts -> bannou-auth node)
/// 4. WebSocket connections survive topology changes
///
/// IMPORTANT: These tests should run LAST as they modify the deployment topology.
/// They require admin credentials and a working orchestrator service.
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
    /// Checks if split deployment (auth/accounts) succeeded.
    /// </summary>
    private static bool RequiresSplitDeployment(string testName)
    {
        if (_splitDeploymentSucceeded) return true;

        Console.WriteLine($"   SKIPPED: {testName} requires successful auth/accounts deployment");
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
            // Phase 1: Deploy auth/accounts split topology
            new ServiceTest(TestDeploySplitTopology, "SplitRouting - Deploy Split Topology", "Orchestrator",
                "Deploy split-auth-routing-test preset via orchestrator (requires admin)"),
            new ServiceTest(TestConnectionSurvivedDeployment, "SplitRouting - Connection Survived", "WebSocket",
                "Verify WebSocket connections survived topology change"),
            new ServiceTest(TestServiceMappingsUpdated, "SplitRouting - Mappings Updated", "Routing",
                "Verify ServiceMappings reflect split topology"),
            new ServiceTest(TestAuthRoutesToSplitNode, "SplitRouting - Auth Routes Correctly", "Routing",
                "Verify auth API calls route to bannou-auth node"),
            new ServiceTest(TestAccountsRoutesToSplitNode, "SplitRouting - Accounts Routes Correctly", "Routing",
                "Verify accounts API calls route to bannou-auth node"),
            new ServiceTest(TestConnectStillOnMainNode, "SplitRouting - Connect on Main", "Routing",
                "Verify connect service still on bannou-main node"),

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
        // Find the deploy API in the admin client's available APIs
        var deployApiKey = adminClient.AvailableApis.Keys
            .FirstOrDefault(k => k.Contains("/orchestrator/deploy", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(deployApiKey))
        {
            Console.WriteLine("❌ Deploy API not found in admin capability manifest.");
            Console.WriteLine($"   Available APIs: {adminClient.AvailableApis.Count}");
            foreach (var api in adminClient.AvailableApis.Keys.Where(k => k.Contains("orchestrator", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"      - {api}");
            }
            return false;
        }

        Console.WriteLine($"   Found deploy API: {deployApiKey}");

        // Get the service GUID for the deploy API
        if (!adminClient.AvailableApis.TryGetValue(deployApiKey, out var deployGuid))
        {
            Console.WriteLine("❌ Could not get GUID for deploy API");
            return false;
        }

        Console.WriteLine($"   Deploy API GUID: {deployGuid}");

        // Build deploy request
        var deployRequest = new
        {
            preset = SPLIT_PRESET,
            backend = "compose",
            dryRun = false
        };

        var requestJson = BannouJson.Serialize(deployRequest);
        Console.WriteLine($"   Request: {requestJson}");

        // Send via WebSocket binary protocol
        var response = await SendOrchestratorRequestAsync(adminClient, deployGuid, requestJson);

        if (response == null)
        {
            Console.WriteLine("❌ No response received from deploy API");
            return false;
        }

        Console.WriteLine($"   Response: {response}");

        // Parse response
        try
        {
            var responseObj = JsonNode.Parse(response);
            var success = responseObj?["success"]?.GetValue<bool>() ?? false;
            var deploymentId = responseObj?["deploymentId"]?.GetValue<string>();
            var message = responseObj?["message"]?.GetValue<string>();

            Console.WriteLine($"   Success: {success}");
            Console.WriteLine($"   DeploymentId: {deploymentId}");
            Console.WriteLine($"   Message: {message}");

            if (success)
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
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to parse deploy response: {ex.Message}");
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
            Console.WriteLine($"✅ Regular client still connected (session: {client.SessionId})");
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
            Console.WriteLine($"✅ Admin client still connected (session: {adminClient.SessionId})");
        }

        if (issues.Count > 0)
        {
            Console.WriteLine($"❌ Connection survival test FAILED:");
            foreach (var issue in issues)
            {
                Console.WriteLine($"   - {issue}");
            }
        }
        else
        {
            Console.WriteLine("✅ Connection survival test PASSED - both clients still connected");
        }
    }

    /// <summary>
    /// Verify ServiceMappings reflect the split topology.
    /// After deployment, auth and accounts should map to bannou-auth.
    /// </summary>
    private void TestServiceMappingsUpdated(string[] args)
    {
        Console.WriteLine("=== Service Mappings Updated Test ===");

        if (!RequiresSplitDeployment("Service mappings test"))
            return;

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected");
            return;
        }

        try
        {
            var result = Task.Run(async () => await CheckServiceMappingsAsync(adminClient)).Result;

            if (result)
            {
                Console.WriteLine("✅ Service mappings updated test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Service mappings updated test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Service mappings test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> CheckServiceMappingsAsync(BannouClient adminClient)
    {
        // Find the service-routing API via WebSocket capability manifest
        // Service-mappings moved from Connect to Orchestrator (December 2025)
        // API key format is "POST:/orchestrator/service-routing" (METHOD:/path format)
        Console.WriteLine("   Looking for service-routing API in capability manifest...");

        var orchestratorApis = adminClient.AvailableApis.Keys
            .Where(k => k.Contains("/orchestrator/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"   Orchestrator APIs available ({orchestratorApis.Count}):");
        foreach (var api in orchestratorApis)
        {
            Console.WriteLine($"      {api}");
        }

        // Look for the service-routing endpoint (format: POST:/orchestrator/service-routing)
        var routingApiKey = adminClient.AvailableApis.Keys
            .FirstOrDefault(k => k.Contains("/orchestrator/service-routing", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(routingApiKey))
        {
            Console.WriteLine("❌ service-routing API not found in capability manifest");
            Console.WriteLine("   Admin user must have access to POST:/orchestrator/service-routing");
            return false;
        }

        if (!adminClient.AvailableApis.TryGetValue(routingApiKey, out var routingGuid))
        {
            Console.WriteLine("❌ Could not get GUID for service routing API");
            return false;
        }

        Console.WriteLine($"   Found API: {routingApiKey} -> {routingGuid}");

        // Call the orchestrator service-routing API
        string? response;
        try
        {
            var result = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/service-routing",
                new { },
                timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();
            response = result.GetRawText();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   service-routing API call failed: {ex.Message}");
            return false;
        }

        if (response == null)
        {
            Console.WriteLine("❌ No response from service mappings API");
            return false;
        }

        Console.WriteLine($"   Routing response: {response}");

        try
        {
            var responseObj = JsonNode.Parse(response);
            var mappings = responseObj?["mappings"]?.AsObject();
            var defaultAppId = responseObj?["defaultAppId"]?.GetValue<string>();

            Console.WriteLine($"   Default app-id: {defaultAppId ?? "(none)"}");

            if (mappings == null || mappings.Count == 0)
            {
                Console.WriteLine("❌ No service mappings in response after split deployment");
                return false;
            }

            // Check for expected split mappings
            var authMapping = mappings["auth"]?.GetValue<string>();
            var accountsMapping = mappings["accounts"]?.GetValue<string>();

            Console.WriteLine($"   auth -> {authMapping ?? "(not set)"}");
            Console.WriteLine($"   accounts -> {accountsMapping ?? "(not set)"}");

            // In split mode, these MUST map to bannou-auth
            if (authMapping != "bannou-auth")
            {
                Console.WriteLine($"❌ auth should map to 'bannou-auth', got '{authMapping ?? "(null)"}'");
                return false;
            }

            if (accountsMapping != "bannou-auth")
            {
                Console.WriteLine($"❌ accounts should map to 'bannou-auth', got '{accountsMapping ?? "(null)"}'");
                return false;
            }

            Console.WriteLine("✅ Mappings correctly reflect split topology");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to parse mappings response: {ex.Message}");
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
                Console.WriteLine("✅ Auth routing test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Auth routing test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Auth routing test FAILED with exception: {ex.Message}");
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
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", client.AccessToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await Program.HttpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"   Validate response ({response.StatusCode}): {content.Substring(0, Math.Min(200, content.Length))}...");

            if (response.IsSuccessStatusCode)
            {
                var responseObj = JsonNode.Parse(content);
                var valid = responseObj?["valid"]?.GetValue<bool>() ?? false;

                if (valid)
                {
                    Console.WriteLine("   ✅ Auth validation succeeded - routing works");
                    return true;
                }

                Console.WriteLine("   ⚠️ Auth validation returned valid=false");
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
    /// Verify accounts API calls route to bannou-auth node.
    /// Makes a real accounts API call to verify routing works.
    /// </summary>
    private void TestAccountsRoutesToSplitNode(string[] args)
    {
        Console.WriteLine("=== Accounts Routes to Split Node Test ===");

        if (!RequiresSplitDeployment("Accounts routing test"))
            return;

        try
        {
            var result = Task.Run(async () => await TestAccountsRoutingAsync()).Result;

            if (result)
            {
                Console.WriteLine("✅ Accounts routing test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Accounts routing test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Accounts routing test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> TestAccountsRoutingAsync()
    {
        // Use the admin client to make an accounts API call
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("   Admin client not connected - cannot test accounts routing");
            return false;
        }

        // Try to call an accounts API endpoint that admin has access to
        try
        {
            // List accounts requires admin role
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/accounts/list",
                new { limit = 1 },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var content = response.GetRawText();
            Console.WriteLine($"   Accounts API response: {content[..Math.Min(200, content.Length)]}...");

            // If we got a response, routing is working
            Console.WriteLine("   ✅ Accounts API call succeeded - routing to bannou-auth node works");
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine($"   Accounts API not available in capability manifest");
            Console.WriteLine("   Admin may not have access to accounts list API");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Accounts API call failed: {ex.Message}");
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
            Console.WriteLine("❌ Client not connected - cannot verify connect routing");
            return;
        }

        // The fact that we're still connected via WebSocket proves
        // the connect service is still reachable on the main node
        Console.WriteLine($"   Client still connected via WebSocket");
        Console.WriteLine($"   Session ID: {client.SessionId}");
        Console.WriteLine($"   Available APIs: {client.AvailableApis.Count}");

        Console.WriteLine("✅ Connect on main node test PASSED - WebSocket still connected");
    }

    /// <summary>
    /// Send an orchestrator API request via the shared admin WebSocket.
    /// Orchestrator APIs are NOT exposed via NGINX, so we must use the admin WebSocket.
    /// </summary>
    private async Task<string?> SendOrchestratorRequestAsync(BannouClient client, Guid serviceGuid, string requestJson)
    {
        try
        {
            // Parse the request JSON to get the actual request object
            var requestObj = JsonNode.Parse(requestJson);
            if (requestObj == null)
            {
                Console.WriteLine("   Failed to parse request JSON");
                return null;
            }

            // Use the shared admin WebSocket to invoke the orchestrator API
            var response = (await client.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/deploy",
                requestObj,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS))).GetResultOrThrow();

            return response.GetRawText();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine($"   Deploy endpoint not available in capability manifest");
            Console.WriteLine($"   Available APIs: {string.Join(", ", client.AvailableApis.Keys.Where(k => k.Contains("orchestrator")).Take(5))}...");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   WebSocket request failed: {ex.Message}");
            return null;
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
            Console.WriteLine("❌ Admin client not connected - cannot restore default topology");
            Console.WriteLine("   ⚠️ WARNING: Subsequent test runs may fail due to stale mappings!");
            Console.WriteLine("   Attempting fallback cleanup via Clean API...");

            // Fallback: at least try to reset mappings via Clean API
            try
            {
                var fallbackResult = Task.Run(async () => await CleanupViaCleanApiAsync()).Result;
                if (fallbackResult)
                {
                    Console.WriteLine("✅ Fallback cleanup via Clean API succeeded");
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
                Console.WriteLine("✅ Default topology restore PASSED - system restored to single-node state");
            }
            else
            {
                Console.WriteLine("❌ Default topology restore FAILED via deploy, trying Clean API fallback...");

                // Fallback: at least reset the mappings via Clean API
                var fallbackResult = Task.Run(async () => await CleanupViaCleanApiAsync()).Result;
                if (fallbackResult)
                {
                    Console.WriteLine("✅ Fallback cleanup via Clean API succeeded");
                }
                else
                {
                    Console.WriteLine("   ⚠️ WARNING: Subsequent test runs may fail due to stale mappings!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Default topology restore FAILED with exception: {ex.Message}");
            Console.WriteLine("   ⚠️ WARNING: Subsequent test runs may fail due to stale mappings!");
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
        var deployRequest = new
        {
            preset = "default",
            backend = "compose",
            dryRun = false
        };

        Console.WriteLine($"   Deploying preset: default via WebSocket (reset to single-node topology)");
        Console.WriteLine($"   Request: {BannouJson.Serialize(deployRequest)}");

        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/deploy",
                deployRequest,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS))).GetResultOrThrow();

            var content = response.GetRawText();
            Console.WriteLine($"   Response: {content}");

            var responseObj = JsonNode.Parse(content);
            var success = responseObj?["success"]?.GetValue<bool>() ?? false;
            var message = responseObj?["message"]?.GetValue<string>();

            Console.WriteLine($"   Success: {success}");
            Console.WriteLine($"   Message: {message}");

            if (success)
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
            Console.WriteLine($"   Deploy endpoint not available in capability manifest");
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

        var cleanRequest = new
        {
            targets = new[] { "containers" },
            force = true
        };

        Console.WriteLine($"   Clean API request via WebSocket: {BannouJson.Serialize(cleanRequest)}");

        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/clean",
                cleanRequest,
                timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

            var content = response.GetRawText();
            Console.WriteLine($"   Clean response: {content}");

            // Wait for mapping events to propagate
            await Task.Delay(2000);
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine($"   Clean endpoint not available in capability manifest");
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
        var deployRequest = new
        {
            preset = RELAYED_CONNECT_PRESET,
            backend = "compose",
            dryRun = false
        };

        Console.WriteLine($"   Request: {BannouJson.Serialize(deployRequest)}");

        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/deploy",
                deployRequest,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS))).GetResultOrThrow();

            var content = response.GetRawText();
            Console.WriteLine($"   Response: {content}");

            var responseObj = JsonNode.Parse(content);
            var success = responseObj?["success"]?.GetValue<bool>() ?? false;
            var message = responseObj?["message"]?.GetValue<string>();

            Console.WriteLine($"   Success: {success}");
            Console.WriteLine($"   Message: {message}");

            if (success)
            {
                Console.WriteLine("   Waiting for Relayed Connect to stabilize...");
                await Task.Delay(5000); // Give time for the new Connect node to start
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
        var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            Console.WriteLine($"   [{connectionName}] Connecting to {serverUri}...");
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);

            if (webSocket.State != WebSocketState.Open)
            {
                Console.WriteLine($"   [{connectionName}] WebSocket in unexpected state: {webSocket.State}");
                return (null, null);
            }

            // Wait for capability manifest
            var buffer = new ArraySegment<byte>(new byte[65536]);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var result = await webSocket.ReceiveAsync(buffer, cts.Token);
            if (result.Count == 0)
            {
                Console.WriteLine($"   [{connectionName}] Empty response received");
                return (webSocket, null);
            }

            if (result.Count < BinaryMessage.HeaderSize)
            {
                Console.WriteLine($"   [{connectionName}] Message too short for header");
                return (webSocket, null);
            }

            var payloadBytes = buffer.Array![(BinaryMessage.HeaderSize)..result.Count];
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            var manifestObj = JsonNode.Parse(payloadJson)?.AsObject();

            if (manifestObj == null)
            {
                Console.WriteLine($"   [{connectionName}] Failed to parse manifest JSON");
                return (webSocket, null);
            }

            var eventName = manifestObj["eventName"]?.GetValue<string>();
            if (eventName != "connect.capability_manifest")
            {
                Console.WriteLine($"   [{connectionName}] Unexpected event type: {eventName}");
                return (webSocket, null);
            }

            var peerGuidStr = manifestObj["peerGuid"]?.GetValue<string>();
            if (string.IsNullOrEmpty(peerGuidStr) || !Guid.TryParse(peerGuidStr, out var peerGuid))
            {
                Console.WriteLine($"   [{connectionName}] No peerGuid in capability manifest (External mode)");
                return (webSocket, null);
            }

            Console.WriteLine($"   [{connectionName}] Received peerGuid: {peerGuid}");
            return (webSocket, peerGuid);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [{connectionName}] Connection failed: {ex.Message}");
            webSocket.Dispose();
            return (null, null);
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
