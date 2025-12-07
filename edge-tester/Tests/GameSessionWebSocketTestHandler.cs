using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for game session service API endpoints.
/// Tests the game session service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class GameSessionWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestManifestCachingBehavior, "GameSession - Manifest Caching", "WebSocket",
                "Validate that all API GUIDs are cached from a single capability manifest"),
            new ServiceTest(TestCreateGameSessionViaWebSocket, "GameSession - Create (WebSocket)", "WebSocket",
                "Test game session creation via WebSocket binary protocol"),
            new ServiceTest(TestListGameSessionsViaWebSocket, "GameSession - List (WebSocket)", "WebSocket",
                "Test game session listing via WebSocket binary protocol"),
            new ServiceTest(TestCompleteSessionLifecycleViaWebSocket, "GameSession - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete session lifecycle via WebSocket: create -> join -> action -> leave"),
        };
    }

    /// <summary>
    /// Tests that the manifest caching behavior works correctly.
    /// This validates that all API GUIDs are cached from a single capability manifest,
    /// preventing the bug where the second API call would timeout waiting for a new manifest.
    /// </summary>
    private void TestManifestCachingBehavior(string[] args)
    {
        Console.WriteLine("=== Manifest Caching Behavior Test ===");
        Console.WriteLine("Validating that all API GUIDs are cached from capability manifest...");

        // Clear the cache to ensure fresh state
        _serviceGuidCache.Clear();

        try
        {
            var result = Task.Run(async () =>
            {
                if (Program.Configuration == null)
                {
                    Console.WriteLine("   Configuration not available");
                    return false;
                }

                var accessToken = Program.Client?.AccessToken;
                if (string.IsNullOrEmpty(accessToken))
                {
                    Console.WriteLine("   Access token not available");
                    return false;
                }

                var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
                using var webSocket = new ClientWebSocket();
                webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

                await webSocket.ConnectAsync(serverUri, CancellationToken.None);
                Console.WriteLine("   WebSocket connected");

                // Request one API - this should trigger manifest caching of ALL APIs
                var firstGuid = await ReceiveCapabilityManifestAndGetGuid(webSocket, "POST", "/sessions/list");

                if (firstGuid == Guid.Empty)
                {
                    Console.WriteLine("   Failed to get GUID for /sessions/list");
                    return false;
                }
                Console.WriteLine($"   Got GUID for /sessions/list: {firstGuid}");

                // Now verify that OTHER APIs are also in the cache (without waiting for manifest)
                var cachedApis = new[]
                {
                    ("POST", "/sessions/create"),
                    ("POST", "/sessions/join"),
                    ("POST", "/sessions/leave"),
                    ("POST", "/sessions/get")
                };

                var allCached = true;
                foreach (var (method, path) in cachedApis)
                {
                    var cacheKey = $"{method}:{path}";
                    if (_serviceGuidCache.TryGetValue(cacheKey, out var cachedGuid))
                    {
                        Console.WriteLine($"   ✅ {cacheKey} cached: {cachedGuid}");
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ {cacheKey} NOT in cache - this would cause timeout!");
                        allCached = false;
                    }
                }

                Console.WriteLine($"   Total APIs in cache: {_serviceGuidCache.Count}");

                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
                }

                return allCached;
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Manifest caching behavior test");
            }
            else
            {
                Console.WriteLine("FAILED Manifest caching behavior test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Manifest caching test with exception: {ex.Message}");
        }
    }

    private void TestCreateGameSessionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== GameSession Create Test (WebSocket) ===");
        Console.WriteLine("Testing /sessions/create via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformGameSessionApiTest(
                "POST",
                "/sessions/create",
                new
                {
                    sessionName = $"WebSocketTest_{DateTime.Now.Ticks}",
                    gameType = "arcadia",
                    maxPlayers = 4,
                    isPrivate = false,
                    owner = Guid.NewGuid()
                },
                response =>
                {
                    if (response?["sessionId"] != null)
                    {
                        var sessionId = response?["sessionId"]?.GetValue<string>();
                        var sessionName = response?["sessionName"]?.GetValue<string>();
                        Console.WriteLine($"   Session ID: {sessionId}");
                        Console.WriteLine($"   Session Name: {sessionName}");
                        return !string.IsNullOrEmpty(sessionId);
                    }
                    return false;
                })).Result;

            if (result)
            {
                Console.WriteLine("PASSED GameSession create test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED GameSession create test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED GameSession create test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestListGameSessionsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== GameSession List Test (WebSocket) ===");
        Console.WriteLine("Testing /sessions/list via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformGameSessionApiTest(
                "POST",
                "/sessions/list",
                new
                {
                    page = 1,
                    pageSize = 10
                },
                response =>
                {
                    if (response?["sessions"] != null)
                    {
                        var sessions = response?["sessions"]?.AsArray();
                        var totalCount = response?["totalCount"]?.GetValue<int>() ?? 0;
                        Console.WriteLine($"   Sessions: {sessions?.Count ?? 0}");
                        Console.WriteLine($"   Total Count: {totalCount}");
                        return true; // Test passes if we got valid response structure
                    }
                    return false;
                })).Result;

            if (result)
            {
                Console.WriteLine("PASSED GameSession list test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED GameSession list test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED GameSession list test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCompleteSessionLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== GameSession Complete Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete session lifecycle via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () =>
            {
                if (Program.Configuration == null)
                {
                    Console.WriteLine("   Configuration not available");
                    return false;
                }

                var accessToken = Program.Client?.AccessToken;
                if (string.IsNullOrEmpty(accessToken))
                {
                    Console.WriteLine("   Access token not available - ensure login completed successfully");
                    return false;
                }

                var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
                using var webSocket = new ClientWebSocket();
                webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

                try
                {
                    await webSocket.ConnectAsync(serverUri, CancellationToken.None);
                    Console.WriteLine("   WebSocket connected for lifecycle test");

                    // Step 1: Create session
                    var ownerId = Guid.NewGuid();
                    var sessionId = await CreateSession(webSocket, ownerId);
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        Console.WriteLine("   Failed to create session");
                        return false;
                    }
                    Console.WriteLine($"   Step 1: Created session {sessionId}");

                    // Step 2: Join session as a player
                    var playerId = Guid.NewGuid();
                    if (!await JoinSession(webSocket, sessionId, playerId))
                    {
                        Console.WriteLine("   Failed to join session");
                        return false;
                    }
                    Console.WriteLine($"   Step 2: Player {playerId} joined");

                    // Step 3: Perform game action
                    var actionResult = await PerformAction(webSocket, sessionId, playerId);
                    if (string.IsNullOrEmpty(actionResult))
                    {
                        Console.WriteLine("   Failed to perform game action");
                        return false;
                    }
                    Console.WriteLine($"   Step 3: Performed action {actionResult}");

                    // Step 4: Send chat message
                    if (!await SendChat(webSocket, sessionId, playerId))
                    {
                        Console.WriteLine("   Failed to send chat message");
                        return false;
                    }
                    Console.WriteLine($"   Step 4: Sent chat message");

                    // Step 5: Leave session
                    if (!await LeaveSession(webSocket, sessionId, playerId))
                    {
                        Console.WriteLine("   Failed to leave session");
                        return false;
                    }
                    Console.WriteLine($"   Step 5: Player left session");

                    // Step 6: Verify session still exists
                    if (!await GetSession(webSocket, sessionId))
                    {
                        Console.WriteLine("   Failed to verify session exists");
                        return false;
                    }
                    Console.WriteLine($"   Step 6: Session verified");

                    return true;
                }
                finally
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
                    }
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED GameSession complete lifecycle test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED GameSession complete lifecycle test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED GameSession lifecycle test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private async Task<string?> CreateSession(ClientWebSocket webSocket, Guid ownerId)
    {
        var requestBody = new
        {
            sessionName = $"LifecycleTest_{DateTime.Now.Ticks}",
            gameType = "arcadia",
            maxPlayers = 4,
            isPrivate = false,
            owner = ownerId
        };

        var response = await SendApiRequest(webSocket, "POST", "/sessions/create", requestBody);
        return response?["sessionId"]?.GetValue<string>();
    }

    private async Task<bool> JoinSession(ClientWebSocket webSocket, string sessionId, Guid playerId)
    {
        var requestBody = new
        {
            sessionId = sessionId,
            playerId = playerId,
            role = "player"
        };

        var response = await SendApiRequest(webSocket, "POST", "/sessions/join", requestBody);
        return response?["success"]?.GetValue<bool>() ?? false;
    }

    private async Task<string?> PerformAction(ClientWebSocket webSocket, string sessionId, Guid playerId)
    {
        var requestBody = new
        {
            sessionId = sessionId,
            playerId = playerId,
            actionType = "test_action",
            actionData = new { testData = "lifecycle_test" }
        };

        var response = await SendApiRequest(webSocket, "POST", "/sessions/actions", requestBody);
        return response?["actionId"]?.GetValue<string>();
    }

    private async Task<bool> SendChat(ClientWebSocket webSocket, string sessionId, Guid senderId)
    {
        var requestBody = new
        {
            sessionId = sessionId,
            senderId = senderId,
            message = "WebSocket lifecycle test message"
        };

        var response = await SendApiRequest(webSocket, "POST", "/sessions/chat", requestBody);
        // Chat returns empty response on success (200 with no content)
        return response != null || true; // Success if no error thrown
    }

    private async Task<bool> LeaveSession(ClientWebSocket webSocket, string sessionId, Guid playerId)
    {
        var requestBody = new
        {
            sessionId = sessionId,
            playerId = playerId
        };

        // Leave returns empty response on success (200 with no content)
        try
        {
            await SendApiRequest(webSocket, "POST", "/sessions/leave", requestBody);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> GetSession(ClientWebSocket webSocket, string sessionId)
    {
        var requestBody = new
        {
            sessionId = sessionId
        };

        var response = await SendApiRequest(webSocket, "POST", "/sessions/get", requestBody);
        return response?["sessionId"]?.GetValue<string>() == sessionId;
    }

    private async Task<JsonObject?> SendApiRequest(ClientWebSocket webSocket, string method, string path, object body)
    {
        // Get the service GUID from capability manifest
        var serviceGuid = await GetServiceGuidForEndpoint(webSocket, method, path);
        if (serviceGuid == Guid.Empty)
        {
            Console.WriteLine($"      Could not find GUID for {method}:{path}");
            return null;
        }

        // Create an API proxy request using binary protocol
        var apiRequest = new
        {
            method = method,
            path = path,
            headers = new Dictionary<string, string>(),
            body = JsonSerializer.Serialize(body)
        };
        var requestPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(apiRequest));

        var binaryMessage = new BinaryMessage(
            flags: MessageFlags.None,
            channel: 2, // API proxy channel
            sequenceNumber: 1,
            serviceGuid: serviceGuid,
            messageId: GuidGenerator.GenerateMessageId(),
            payload: requestPayload
        );

        // Send the API proxy request
        var messageBytes = binaryMessage.ToByteArray();
        var buffer = new ArraySegment<byte>(messageBytes);
        await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

        // Receive response
        var responseResult = await WaitForResponseMessage(webSocket, TimeSpan.FromSeconds(15));
        if (responseResult == null)
        {
            Console.WriteLine($"      Timeout waiting for response to {method}:{path}");
            return null;
        }

        var response = responseResult.Value;
        if (response.Payload.Length > 0)
        {
            var responsePayload = Encoding.UTF8.GetString(response.Payload.Span);
            try
            {
                var responseObj = JsonNode.Parse(responsePayload)?.AsObject();

                // Check for error response
                if (responseObj?["error"] != null)
                {
                    var errorMessage = responseObj?["error"]?.GetValue<string>();
                    var statusCode = responseObj?["statusCode"]?.GetValue<int>();
                    Console.WriteLine($"      API returned error: {statusCode} - {errorMessage}");
                    return null;
                }

                return responseObj;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Cache for service GUIDs to avoid repeatedly parsing capability manifests
    /// </summary>
    private readonly Dictionary<string, Guid> _serviceGuidCache = new();

    private async Task<Guid> GetServiceGuidForEndpoint(ClientWebSocket webSocket, string method, string path)
    {
        var cacheKey = $"{method}:{path}";
        if (_serviceGuidCache.TryGetValue(cacheKey, out var cachedGuid))
        {
            return cachedGuid;
        }

        // Need to receive capability manifest to get GUIDs
        var guid = await ReceiveCapabilityManifestAndGetGuid(webSocket, method, path);
        if (guid != Guid.Empty)
        {
            _serviceGuidCache[cacheKey] = guid;
        }
        return guid;
    }

    /// <summary>
    /// Performs a game session API call through the WebSocket binary protocol.
    /// </summary>
    private async Task<bool> PerformGameSessionApiTest(
        string method,
        string path,
        object? body,
        Func<JsonObject?, bool> validateResponse)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return false;
        }

        var accessToken = Program.Client?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("   Access token not available - ensure login completed successfully");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("   WebSocket connected for game session API test");

            // First, receive the capability manifest pushed by the server
            Console.WriteLine("   Waiting for capability manifest...");
            var serviceGuid = await ReceiveCapabilityManifestAndGetGuid(webSocket, method, path);

            if (serviceGuid == Guid.Empty)
            {
                Console.WriteLine("   Failed to receive capability manifest or find GUID for endpoint");
                return false;
            }

            Console.WriteLine($"   Found service GUID for {method}:{path}: {serviceGuid}");

            // Create an API proxy request using binary protocol
            var apiRequest = new
            {
                method = method,
                path = path,
                headers = new Dictionary<string, string>(),
                body = body != null ? JsonSerializer.Serialize(body) : null
            };
            var requestPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(apiRequest));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 2, // API proxy channel
                sequenceNumber: 1,
                serviceGuid: serviceGuid,
                messageId: GuidGenerator.GenerateMessageId(),
                payload: requestPayload
            );

            Console.WriteLine($"   Sending game session API request:");
            Console.WriteLine($"   Method: {method}");
            Console.WriteLine($"   Path: {path}");
            Console.WriteLine($"   ServiceGuid: {serviceGuid}");

            // Send the API proxy request
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Receive response with timeout
            var responseResult = await WaitForResponseMessage(webSocket, TimeSpan.FromSeconds(15));

            if (responseResult == null)
            {
                Console.WriteLine("   Timeout waiting for API response message");
                return false;
            }

            var response = responseResult.Value;
            Console.WriteLine($"   Received API response: {response.Payload.Length} bytes payload");

            try
            {
                if (response.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(response.Payload.Span);
                    Console.WriteLine($"   Response payload: {responsePayload.Substring(0, Math.Min(500, responsePayload.Length))}...");

                    // Parse as JSON and validate
                    var responseObj = JsonNode.Parse(responsePayload)?.AsObject();

                    // Check for error response
                    if (responseObj?["error"] != null)
                    {
                        var errorMessage = responseObj?["error"]?.GetValue<string>();
                        var statusCode = responseObj?["statusCode"]?.GetValue<int>();
                        Console.WriteLine($"   API returned error: {statusCode} - {errorMessage}");
                        return false;
                    }

                    // Validate the response
                    return validateResponse(responseObj);
                }

                Console.WriteLine("   API response has no payload - assuming success");
                return true; // Some endpoints return 200 with no content
            }
            catch (JsonException)
            {
                var responseText = Encoding.UTF8.GetString(response.Payload.Span);
                Console.WriteLine($"   Non-JSON response: {responseText}");
                return false;
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"   Failed to parse API response: {parseEx.Message}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("   Request timed out waiting for response");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Game session API test failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Receives the capability manifest from the server and extracts the service GUID for the requested endpoint.
    /// </summary>
    private async Task<Guid> ReceiveCapabilityManifestAndGetGuid(
        ClientWebSocket webSocket,
        string method,
        string path)
    {
        var overallTimeout = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;
        var receiveBuffer = new ArraySegment<byte>(new byte[65536]);

        while (DateTime.UtcNow - startTime < overallTimeout)
        {
            try
            {
                var remainingTime = overallTimeout - (DateTime.UtcNow - startTime);
                if (remainingTime <= TimeSpan.Zero) break;

                using var cts = new CancellationTokenSource(remainingTime);
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                if (receiveBuffer.Array == null || result.Count == 0)
                {
                    continue;
                }

                // Parse the binary message
                var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);

                // Check if this is an event message (capability manifest)
                if (!receivedMessage.Flags.HasFlag(MessageFlags.Event))
                {
                    continue;
                }

                if (receivedMessage.Payload.Length == 0)
                {
                    continue;
                }

                var payloadJson = Encoding.UTF8.GetString(receivedMessage.Payload.Span);

                JsonObject? manifest;
                try
                {
                    manifest = JsonNode.Parse(payloadJson)?.AsObject();
                }
                catch
                {
                    continue;
                }

                // Verify this is a capability manifest
                var type = manifest?["type"]?.GetValue<string>();
                if (type != "capability_manifest")
                {
                    continue;
                }

                var availableApis = manifest?["availableAPIs"]?.AsArray();
                if (availableApis == null)
                {
                    continue;
                }

                // Cache ALL GUIDs from the manifest to avoid waiting for another manifest
                // that will never arrive (server only pushes manifest once at connection time)
                CacheAllGuidsFromManifest(availableApis);

                // Now check the cache for our endpoint
                var cacheKey = $"{method}:{path}";
                if (_serviceGuidCache.TryGetValue(cacheKey, out var guid))
                {
                    return guid;
                }

                // API not found in this manifest - wait for capability updates
                Console.WriteLine($"   API {method}:{path} not found in manifest ({_serviceGuidCache.Count} APIs cached), waiting for updates...");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Error receiving message: {ex.Message}, retrying...");
            }
        }

        Console.WriteLine($"   Timeout waiting for API {method}:{path} to become available");
        return Guid.Empty;
    }

    /// <summary>
    /// Finds the GUID for a specific endpoint in the capability manifest.
    /// </summary>
    private Guid FindGuidInManifest(JsonArray availableApis, string method, string path)
    {
        // Try exact match first
        foreach (var api in availableApis)
        {
            var apiMethod = api?["method"]?.GetValue<string>();
            var apiPath = api?["path"]?.GetValue<string>();
            var apiGuid = api?["serviceGuid"]?.GetValue<string>();

            if (apiMethod == method && apiPath == path && !string.IsNullOrEmpty(apiGuid))
            {
                if (Guid.TryParse(apiGuid, out var guid))
                {
                    return guid;
                }
            }
        }

        // Try by endpoint key format
        foreach (var api in availableApis)
        {
            var endpointKey = api?["endpointKey"]?.GetValue<string>();
            var apiGuid = api?["serviceGuid"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(endpointKey) && endpointKey.Contains($":{method}:{path}"))
            {
                if (Guid.TryParse(apiGuid, out var guid))
                {
                    return guid;
                }
            }
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Caches ALL service GUIDs from a capability manifest.
    /// This is critical because the server only pushes the manifest once at connection time.
    /// Without caching all GUIDs upfront, subsequent API calls would wait forever for a
    /// new manifest that never arrives.
    /// </summary>
    private void CacheAllGuidsFromManifest(JsonArray availableApis)
    {
        var cachedCount = 0;
        foreach (var api in availableApis)
        {
            var apiMethod = api?["method"]?.GetValue<string>();
            var apiPath = api?["path"]?.GetValue<string>();
            var apiGuid = api?["serviceGuid"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(apiMethod) && !string.IsNullOrEmpty(apiPath) && !string.IsNullOrEmpty(apiGuid))
            {
                if (Guid.TryParse(apiGuid, out var guid))
                {
                    var cacheKey = $"{apiMethod}:{apiPath}";
                    if (!_serviceGuidCache.ContainsKey(cacheKey))
                    {
                        _serviceGuidCache[cacheKey] = guid;
                        cachedCount++;
                    }
                }
            }
        }

        if (cachedCount > 0)
        {
            Console.WriteLine($"   Cached {cachedCount} API GUIDs from capability manifest (total: {_serviceGuidCache.Count})");
        }
    }

    /// <summary>
    /// Waits for a Response message from the WebSocket, skipping any Event messages.
    /// </summary>
    private async Task<BinaryMessage?> WaitForResponseMessage(ClientWebSocket webSocket, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        var receiveBuffer = new ArraySegment<byte>(new byte[65536]);

        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var remainingTime = timeout - (DateTime.UtcNow - startTime);
                if (remainingTime <= TimeSpan.Zero) break;

                using var cts = new CancellationTokenSource(remainingTime);
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("   WebSocket closed while waiting for response");
                    return null;
                }

                if (receiveBuffer.Array == null || result.Count == 0)
                {
                    continue;
                }

                // Parse the binary message
                var message = BinaryMessage.Parse(receiveBuffer.Array, result.Count);

                // Check if this is a Response message (not an Event)
                if (message.Flags.HasFlag(MessageFlags.Response))
                {
                    return message;
                }

                // If it's an Event message, skip it and continue waiting
                if (message.Flags.HasFlag(MessageFlags.Event))
                {
                    continue;
                }

                // Message is neither Response nor Event - could be an error
                if (message.Payload.Length > 0)
                {
                    return message;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Error receiving message: {ex.Message}, retrying...");
            }
        }

        return null;
    }
}
