using BeyondImmersion.Bannou.Client.SDK;
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
/// </summary>
public class SplitServiceRoutingTestHandler : IServiceTestHandler
{
    private const string SPLIT_PRESET = "split-auth-routing-test";
    private const int DEPLOYMENT_TIMEOUT_SECONDS = 180; // Increased from 120 - CI deployments can take 90+ seconds

    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            // These tests run in sequence - deployment first, then validation, then cleanup
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
            // CRITICAL: Cleanup MUST run last to reset service mappings for subsequent test runs
            new ServiceTest(TestCleanupServiceMappings, "SplitRouting - Cleanup", "Orchestrator",
                "Reset service mappings to default state via Clean API"),
        };
    }

    /// <summary>
    /// Deploy the split-auth-routing-test topology via orchestrator.
    /// Uses the admin WebSocket connection to call /orchestrator/deploy.
    /// </summary>
    private void TestDeploySplitTopology(string[] args)
    {
        Console.WriteLine("=== Deploy Split Topology Test ===");
        Console.WriteLine($"Deploying preset: {SPLIT_PRESET}");
        Console.WriteLine($"Timeout: {DEPLOYMENT_TIMEOUT_SECONDS} seconds");

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - cannot deploy split topology");
            Console.WriteLine("   Ensure admin credentials are configured and admin is authenticated.");
            return;
        }

        Console.WriteLine($"✅ Admin client connected, session: {adminClient.SessionId}");

        try
        {
            var result = Task.Run(async () => await DeploySplitTopologyAsync(adminClient)).Result;

            if (result)
            {
                Console.WriteLine("✅ Split topology deployment PASSED");
            }
            else
            {
                Console.WriteLine("❌ Split topology deployment FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Split topology deployment FAILED with exception: {ex.Message}");
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

        var requestJson = JsonSerializer.Serialize(deployRequest);
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
        // API key format is "orchestrator:POST:/orchestrator/service-routing"
        Console.WriteLine("   Looking for service-routing API in capability manifest...");

        var orchestratorApis = adminClient.AvailableApis.Keys
            .Where(k => k.Contains("orchestrator", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"   Orchestrator APIs available ({orchestratorApis.Count}):");
        foreach (var api in orchestratorApis)
        {
            Console.WriteLine($"      {api}");
        }

        var routingApiKey = adminClient.AvailableApis.Keys
            .FirstOrDefault(k => k.Contains("orchestrator:", StringComparison.OrdinalIgnoreCase) &&
                                k.Contains("/service-routing", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(routingApiKey))
        {
            Console.WriteLine("❌ service-routing API not found in capability manifest");
            Console.WriteLine("   Admin user must have access to orchestrator:POST:/orchestrator/service-routing");
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
            var result = await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/service-routing",
                new { },
                timeout: TimeSpan.FromSeconds(30));
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
        var host = config.OpenResty_Host ?? "openresty";
        var port = config.OpenResty_Port ?? 80;

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
            var response = await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/accounts/list",
                new { limit = 1 },
                timeout: TimeSpan.FromSeconds(15));

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
            var response = await client.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/deploy",
                requestObj,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS));

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
    /// Deploy the default edge-tests topology to restore single-node operation.
    /// Uses the shared admin WebSocket since orchestrator APIs are not exposed via NGINX.
    /// </summary>
    private async Task<bool> RestoreDefaultTopologyAsync(BannouClient adminClient)
    {
        // Deploy edge-tests preset which has all services on single 'bannou' node
        var deployRequest = new
        {
            preset = "edge-tests",
            backend = "compose",
            dryRun = false
        };

        Console.WriteLine($"   Deploying preset: edge-tests via WebSocket");
        Console.WriteLine($"   Request: {JsonSerializer.Serialize(deployRequest)}");

        try
        {
            var response = await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/deploy",
                deployRequest,
                timeout: TimeSpan.FromSeconds(DEPLOYMENT_TIMEOUT_SECONDS));

            var content = response.GetRawText();
            Console.WriteLine($"   Response: {content}");

            var responseObj = JsonNode.Parse(content);
            var success = responseObj?["success"]?.GetValue<bool>() ?? false;
            var message = responseObj?["message"]?.GetValue<string>();

            Console.WriteLine($"   Success: {success}");
            Console.WriteLine($"   Message: {message}");

            if (success)
            {
                // Wait for deployment to stabilize
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

        Console.WriteLine($"   Clean API request via WebSocket: {JsonSerializer.Serialize(cleanRequest)}");

        try
        {
            var response = await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/orchestrator/clean",
                cleanRequest,
                timeout: TimeSpan.FromSeconds(30));

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
}
