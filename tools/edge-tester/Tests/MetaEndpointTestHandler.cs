using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Test handler for Meta Endpoint functionality.
/// Tests the runtime schema introspection feature using the Meta flag (0x80).
/// </summary>
public class MetaEndpointTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestMetaFlagEndpointInfo, "Meta - Endpoint Info via Flag", "WebSocket",
                "Test requesting endpoint info using Meta flag (0x80) with MetaType.EndpointInfo"),
            new ServiceTest(TestMetaFlagRequestSchema, "Meta - Request Schema via Flag", "WebSocket",
                "Test requesting request schema using Meta flag with MetaType.RequestSchema"),
            new ServiceTest(TestMetaFlagResponseSchema, "Meta - Response Schema via Flag", "WebSocket",
                "Test requesting response schema using Meta flag with MetaType.ResponseSchema"),
            new ServiceTest(TestMetaFlagFullSchema, "Meta - Full Schema via Flag", "WebSocket",
                "Test requesting full schema using Meta flag with MetaType.FullSchema"),
            new ServiceTest(TestMetaEndpointsNotExposedViaNginx, "Meta - Not Exposed via NGINX", "HTTP",
                "Verify meta endpoints are NOT accessible via NGINX (security: only via WebSocket Meta flag)"),
            new ServiceTest(TestMetaFlagUnknownGuid, "Meta - Unknown GUID Error", "WebSocket",
                "Test that meta request with unknown GUID returns ServiceNotFound error"),
        };
    }

    #region Helper Methods

    /// <summary>
    /// Creates a dedicated test account and returns the access token.
    /// </summary>
    private async Task<string?> CreateTestAccountAsync(string testPrefix)
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
    /// Establishes WebSocket connection and waits for capability manifest, returning a known API GUID.
    /// </summary>
    private async Task<(ClientWebSocket WebSocket, Guid ApiGuid, string EndpointKey)?> ConnectAndGetApiGuid(string testPrefix)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return null;
        }

        var accessToken = await CreateTestAccountAsync(testPrefix);
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return null;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        ClientWebSocket? webSocket = null;
        try
        {
            webSocket = new ClientWebSocket();
            webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected");

            // Wait for capability manifest
            var receiveBuffer = new byte[65536];
            Guid? apiGuid = null;
            string? endpointKey = null;

            // Receive messages until we get capability manifest or timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (!cts.Token.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("❌ WebSocket closed before receiving capability manifest");
                    webSocket.Dispose();
                    webSocket = null;
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Binary && result.Count >= BinaryMessage.ResponseHeaderSize)
                {
                    var message = BinaryMessage.Parse(receiveBuffer, result.Count);

                    if (message.Flags.HasFlag(MessageFlags.Event) && message.Payload.Length > 0)
                    {
                        var payloadJson = Encoding.UTF8.GetString(message.Payload.Span);
                        using var doc = JsonDocument.Parse(payloadJson);

                        if (doc.RootElement.TryGetProperty("eventName", out var eventName) &&
                            eventName.GetString() == "connect.capability_manifest")
                        {
                            Console.WriteLine("✅ Received capability manifest");

                            // Find a valid API endpoint - look for account/get which should always exist
                            if (doc.RootElement.TryGetProperty("availableApis", out var apis) &&
                                apis.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var api in apis.EnumerateArray())
                                {
                                    var key = api.TryGetProperty("endpoint", out var keyProp) ? keyProp.GetString() : null;
                                    var guidStr = api.TryGetProperty("serviceId", out var guidProp) ? guidProp.GetString() : null;

                                    // Use account/get as our test endpoint
                                    if (key?.Contains("/get") == true && Guid.TryParse(guidStr, out var guid))
                                    {
                                        apiGuid = guid;
                                        endpointKey = key;
                                        Console.WriteLine($"   Found API: {key} -> {guid}");
                                        break;
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }

            if (apiGuid == null || endpointKey == null)
            {
                Console.WriteLine("❌ No suitable API found in capability manifest");
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "No API found", CancellationToken.None);
                webSocket.Dispose();
                webSocket = null;
                return null;
            }

            var resultWebSocket = webSocket;
            webSocket = null; // Transfer ownership to caller
            return (resultWebSocket, apiGuid.Value, endpointKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to connect: {ex.Message}");
            return null;
        }
        finally
        {
            webSocket?.Dispose();
        }
    }

    /// <summary>
    /// Sends a meta request and returns the response.
    /// </summary>
    private async Task<(bool Success, JsonDocument? Response)> SendMetaRequestAsync(
        ClientWebSocket webSocket,
        Guid apiGuid,
        MetaType metaType)
    {
        var messageId = GuidGenerator.GenerateMessageId();

        var metaMessage = new BinaryMessage(
            flags: MessageFlags.Meta,              // Meta flag triggers route transformation
            channel: (ushort)metaType,             // Meta type encoded in Channel field
            sequenceNumber: 1,
            serviceGuid: apiGuid,
            messageId: messageId,
            payload: Array.Empty<byte>()           // Meta requests have empty payload
        );

        Console.WriteLine($"📤 Sending meta request:");
        Console.WriteLine($"   Flags: {metaMessage.Flags} (Meta={metaMessage.IsMeta})");
        Console.WriteLine($"   Channel/MetaType: {metaType} ({(ushort)metaType})");
        Console.WriteLine($"   Service GUID: {apiGuid}");
        Console.WriteLine($"   Message ID: {messageId}");

        await webSocket.SendAsync(
            new ArraySegment<byte>(metaMessage.ToByteArray()),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None);

        Console.WriteLine("✅ Meta request sent");

        // Receive response with timeout
        var receiveBuffer = new byte[65536];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!cts.Token.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts.Token);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine("❌ WebSocket closed while waiting for response");
                return (false, null);
            }

            if (result.MessageType == WebSocketMessageType.Binary && result.Count >= BinaryMessage.ResponseHeaderSize)
            {
                var response = BinaryMessage.Parse(receiveBuffer, result.Count);

                // Skip events - wait for our response
                if (response.Flags.HasFlag(MessageFlags.Event))
                {
                    continue;
                }

                Console.WriteLine($"📥 Received response:");
                Console.WriteLine($"   Flags: {response.Flags}");
                Console.WriteLine($"   IsResponse: {response.IsResponse}");
                Console.WriteLine($"   ResponseCode: {response.ResponseCode}");
                Console.WriteLine($"   Message ID: {response.MessageId}");

                if (response.IsResponse && response.MessageId == messageId)
                {
                    if (response.ResponseCode != 0)
                    {
                        Console.WriteLine($"❌ Response error code: {response.ResponseCode}");
                        return (false, null);
                    }

                    if (response.Payload.Length > 0)
                    {
                        var payloadJson = Encoding.UTF8.GetString(response.Payload.Span);
                        Console.WriteLine($"   Payload length: {response.Payload.Length} bytes");
                        var doc = JsonDocument.Parse(payloadJson);
                        return (true, doc);
                    }
                    else
                    {
                        Console.WriteLine("❌ Empty payload in response");
                        return (false, null);
                    }
                }
            }
        }

        Console.WriteLine("❌ Timeout waiting for response");
        return (false, null);
    }

    #endregion

    #region Test Methods

    private void TestMetaFlagEndpointInfo(string[] args)
    {
        Console.WriteLine("=== Meta Flag Endpoint Info Test ===");
        Console.WriteLine("Testing endpoint info via Meta flag with MetaType.EndpointInfo...");

        try
        {
            var result = Task.Run(async () => await PerformMetaFlagTest(MetaType.EndpointInfo, "endpoint-info")).Result;
            Console.WriteLine(result ? "✅ Test PASSED" : "❌ Test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test FAILED with exception: {ex.Message}");
        }
    }

    private void TestMetaFlagRequestSchema(string[] args)
    {
        Console.WriteLine("=== Meta Flag Request Schema Test ===");
        Console.WriteLine("Testing request schema via Meta flag with MetaType.RequestSchema...");

        try
        {
            var result = Task.Run(async () => await PerformMetaFlagTest(MetaType.RequestSchema, "request-schema")).Result;
            Console.WriteLine(result ? "✅ Test PASSED" : "❌ Test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test FAILED with exception: {ex.Message}");
        }
    }

    private void TestMetaFlagResponseSchema(string[] args)
    {
        Console.WriteLine("=== Meta Flag Response Schema Test ===");
        Console.WriteLine("Testing response schema via Meta flag with MetaType.ResponseSchema...");

        try
        {
            var result = Task.Run(async () => await PerformMetaFlagTest(MetaType.ResponseSchema, "response-schema")).Result;
            Console.WriteLine(result ? "✅ Test PASSED" : "❌ Test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test FAILED with exception: {ex.Message}");
        }
    }

    private void TestMetaFlagFullSchema(string[] args)
    {
        Console.WriteLine("=== Meta Flag Full Schema Test ===");
        Console.WriteLine("Testing full schema via Meta flag with MetaType.FullSchema...");

        try
        {
            var result = Task.Run(async () => await PerformMetaFlagTest(MetaType.FullSchema, "full-schema")).Result;
            Console.WriteLine(result ? "✅ Test PASSED" : "❌ Test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformMetaFlagTest(MetaType metaType, string expectedMetaType)
    {
        var connection = await ConnectAndGetApiGuid($"meta_{metaType.ToString().ToLowerInvariant()}");
        if (connection == null)
        {
            return false;
        }

        var (webSocket, apiGuid, endpointKey) = connection.Value;

        try
        {
            var (success, responseDoc) = await SendMetaRequestAsync(webSocket, apiGuid, metaType);

            if (!success || responseDoc == null)
            {
                return false;
            }

            // Validate response structure
            var root = responseDoc.RootElement;

            // Check metaType
            if (!root.TryGetProperty("metaType", out var metaTypeProp) ||
                metaTypeProp.GetString() != expectedMetaType)
            {
                Console.WriteLine($"❌ Expected metaType '{expectedMetaType}', got '{metaTypeProp.GetString()}'");
                return false;
            }
            Console.WriteLine($"✅ metaType: {expectedMetaType}");

            // Check method exists
            if (!root.TryGetProperty("method", out var methodProp) ||
                string.IsNullOrEmpty(methodProp.GetString()))
            {
                Console.WriteLine("❌ Missing method");
                return false;
            }
            Console.WriteLine($"✅ method: {methodProp.GetString()}");

            // Check path exists
            if (!root.TryGetProperty("path", out var pathProp) ||
                string.IsNullOrEmpty(pathProp.GetString()))
            {
                Console.WriteLine("❌ Missing path");
                return false;
            }
            Console.WriteLine($"✅ path: {pathProp.GetString()}");

            // Check serviceName exists
            if (!root.TryGetProperty("serviceName", out var serviceNameProp) ||
                string.IsNullOrEmpty(serviceNameProp.GetString()))
            {
                Console.WriteLine("❌ Missing serviceName");
                return false;
            }
            Console.WriteLine($"✅ serviceName: {serviceNameProp.GetString()}");

            // Check data exists
            if (!root.TryGetProperty("data", out var dataProp))
            {
                Console.WriteLine("❌ Missing data property");
                return false;
            }
            Console.WriteLine($"✅ data property present (kind: {dataProp.ValueKind})");

            // Check schemaVersion
            if (!root.TryGetProperty("schemaVersion", out var schemaVersionProp) ||
                string.IsNullOrEmpty(schemaVersionProp.GetString()))
            {
                Console.WriteLine("❌ Missing schemaVersion");
                return false;
            }
            Console.WriteLine($"✅ schemaVersion: {schemaVersionProp.GetString()}");

            // Check generatedAt
            if (!root.TryGetProperty("generatedAt", out _))
            {
                Console.WriteLine("❌ Missing generatedAt");
                return false;
            }
            Console.WriteLine("✅ generatedAt present");

            return true;
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
            webSocket.Dispose();
        }
    }

    private void TestMetaEndpointsNotExposedViaNginx(string[] args)
    {
        Console.WriteLine("=== Meta Endpoints Not Exposed via NGINX Test ===");
        Console.WriteLine("Verifying meta endpoints are NOT accessible via NGINX (security test)...");

        try
        {
            var result = Task.Run(async () => await PerformMetaNotExposedTest()).Result;
            Console.WriteLine(result ? "✅ Test PASSED" : "❌ Test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformMetaNotExposedTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;

        // Test that meta endpoints for NGINX-exposed routes are NOT accessible
        // /auth/register is exposed via NGINX, but /auth/register/meta/schema should NOT be
        // This is a security test - meta info should only be accessible via WebSocket Meta flag
        var metaUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register/meta/schema";
        Console.WriteLine($"📡 Testing: {metaUrl}");
        Console.WriteLine("   (Expected: 404 - meta endpoints should NOT be exposed via NGINX)");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, metaUrl);
            using var response = await Program.HttpClient.SendAsync(request);

            Console.WriteLine($"📥 Response status: {response.StatusCode}");

            // We EXPECT 404 - meta endpoints should not be accessible via NGINX
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.WriteLine("✅ Correctly returned 404 - meta endpoints not exposed via NGINX");
                return true;
            }

            // If we got a 200, that's a security issue - meta endpoints shouldn't be exposed
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("❌ SECURITY: Meta endpoint unexpectedly accessible via NGINX!");
                Console.WriteLine("   Meta endpoints should only be accessible via WebSocket Meta flag");
                return false;
            }

            // Other error codes are acceptable (nginx might return 403, 405, etc.)
            Console.WriteLine($"✅ Meta endpoint not accessible (status: {response.StatusCode})");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ HTTP request failed: {ex.Message}");
            return false;
        }
    }

    private void TestMetaFlagUnknownGuid(string[] args)
    {
        Console.WriteLine("=== Meta Flag Unknown GUID Test ===");
        Console.WriteLine("Testing that meta request with unknown GUID returns error...");

        try
        {
            var result = Task.Run(async () => await PerformUnknownGuidTest()).Result;
            Console.WriteLine(result ? "✅ Test PASSED" : "❌ Test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test FAILED with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformUnknownGuidTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var accessToken = await CreateTestAccountAsync("meta_unknown");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected");

            // Wait briefly for capability manifest
            var receiveBuffer = new byte[65536];
            using var manifestCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), manifestCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Manifest timeout is fine for this test
            }

            // Send meta request with unknown GUID
            var unknownGuid = Guid.NewGuid();
            var messageId = GuidGenerator.GenerateMessageId();

            var metaMessage = new BinaryMessage(
                flags: MessageFlags.Meta,
                channel: (ushort)MetaType.FullSchema,
                sequenceNumber: 1,
                serviceGuid: unknownGuid,  // Unknown GUID
                messageId: messageId,
                payload: Array.Empty<byte>()
            );

            Console.WriteLine($"📤 Sending meta request with unknown GUID: {unknownGuid}");

            await webSocket.SendAsync(
                new ArraySegment<byte>(metaMessage.ToByteArray()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            // Receive response
            using var responseCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!responseCts.Token.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), responseCts.Token);

                if (result.MessageType == WebSocketMessageType.Binary && result.Count >= BinaryMessage.ResponseHeaderSize)
                {
                    var response = BinaryMessage.Parse(receiveBuffer, result.Count);

                    // Skip events
                    if (response.Flags.HasFlag(MessageFlags.Event))
                    {
                        continue;
                    }

                    if (response.IsResponse && response.MessageId == messageId)
                    {
                        Console.WriteLine($"📥 Response code: {response.ResponseCode}");

                        // Expect ServiceNotFound error (code 30) for unknown GUID
                        if (response.ResponseCode == (byte)ResponseCodes.ServiceNotFound)
                        {
                            Console.WriteLine("✅ Correctly received ServiceNotFound error for unknown GUID");
                            return true;
                        }
                        else if (response.ResponseCode != 0)
                        {
                            Console.WriteLine($"⚠️ Received unexpected error code {response.ResponseCode} (expected ServiceNotFound={((byte)ResponseCodes.ServiceNotFound)})");
                            return false; // Wrong error code is still a failure
                        }
                        else
                        {
                            Console.WriteLine("❌ Unexpected success response for unknown GUID");
                            return false;
                        }
                    }
                }
            }

            Console.WriteLine("❌ Timeout waiting for response");
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

    #endregion
}
