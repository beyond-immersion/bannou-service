# Bannou Client SDK

Lightweight SDK for game clients connecting to Bannou services via WebSocket.

## Overview

The Client SDK provides everything a game client needs to communicate with Bannou:
- **WebSocket client** with automatic reconnection and binary protocol support
- **Request/response messaging** with typed payloads
- **Event reception** for real-time updates pushed from services
- **Capability manifest** for dynamic API discovery
- **Shortcuts** for subscription-based access patterns
- **Typed service proxies** for type-safe API calls (`client.Auth`, `client.Character`, etc.)
- **Typed event subscriptions** with disposable handlers
- **Game transport helpers**: MessagePack DTOs + LiteNetLib client transport for UDP gameplay state

## Quick Start

```csharp
using BeyondImmersion.Bannou.Client;

// Create client and connect
var client = new BannouClient();
await client.ConnectWithTokenAsync(connectUrl, accessToken);

// Use typed service proxies (recommended)
var loginResponse = await client.Auth.LoginAsync(new LoginRequest
{
    Email = "player@example.com",
    Password = "secure-password"
});

if (loginResponse.IsSuccess)
{
    Console.WriteLine($"Logged in as: {loginResponse.Result.AccountId}");
}

// Or use the generic InvokeAsync for custom endpoints
var response = await client.InvokeAsync<MyRequest, MyResponse>(
    "POST",
    "/character/get",
    new MyRequest { CharacterId = "abc123" },
    timeout: TimeSpan.FromSeconds(5));

// Handle typed events with disposable subscriptions
using var subscription = client.OnEvent<ChatMessageReceivedEvent>(evt =>
{
    Console.WriteLine($"Chat from {evt.SenderId}: {evt.Message}");
});

// Or use the generic event handler for all events
client.OnEvent += (sender, eventData) =>
{
    Console.WriteLine($"Event received: {eventData.EventName}");
};

// Clean up
await client.DisposeAsync();
```

### Internal Mode (service token or network trust)

```csharp
var client = new BannouClient();
// Connect directly to an internal Connect node without JWT login
// Provide serviceToken when CONNECT_INTERNAL_AUTH_MODE=service-token; omit for network-trust.
await client.ConnectInternalAsync("ws://bannou-internal/connect", serviceToken: "shared-secret");
```

## Game Transport (UDP) Client
- Envelope: `GameProtocolEnvelope` (version + `GameMessageType`)
- DTOs: snapshots/deltas, combat events, opportunities, connect, input, cinematic extensions
- Client transport: `LiteNetLibClientTransport` (optional fuzz via `TransportFuzzOptions`)

```csharp
var transport = new LiteNetLibClientTransport();
await transport.ConnectAsync("127.0.0.1", 9000, GameProtocolEnvelope.CurrentVersion);
transport.OnServerMessage += (ver, type, payload) =>
{
    if (type == GameMessageType.ArenaStateSnapshot)
    {
        var snap = MessagePackSerializer.Deserialize<ArenaStateSnapshot>(payload, GameProtocolEnvelope.DefaultOptions);
    }
};

// Send input
var input = new PlayerInputMessage { Tick = 1, MoveX = 1, MoveY = 0 };
var bytes = MessagePackSerializer.Serialize(input, GameProtocolEnvelope.DefaultOptions);
await transport.SendAsync(GameMessageType.PlayerInput, bytes, reliable: true);
```

## Architecture

```
┌─────────────────┐     WebSocket      ┌─────────────────┐
│   Game Client   │◄──────────────────►│ Connect Service │
│ (Client SDK)    │   Binary Protocol  │   (Gateway)     │
└─────────────────┘                    └────────┬────────┘
                                                │ mesh
                                       ┌────────┴────────┐
                                       │ Bannou Services │
                                       │ (Auth, Character│
                                       │  GameSession..) │
                                       └─────────────────┘
```

The Connect service is the WebSocket gateway. All client communication flows through it:
1. Client connects via WebSocket with JWT token
2. Connect validates token and creates session
3. Connect pushes capability manifest (available APIs)
4. Client invokes APIs using binary protocol
5. Connect routes requests to appropriate backend services

## Binary Protocol

Messages use a hybrid format: **binary header** (31 bytes) + **JSON payload**.

```
┌─────────────────────────────────────────────────────────┐
│ Binary Header (31 bytes)                                │
├──────────┬─────────┬──────────┬──────────────┬──────────┤
│ Flags    │ Channel │ Sequence │ Service GUID │ Msg ID   │
│ (1 byte) │ (2)     │ (4)      │ (16)         │ (8)      │
├─────────────────────────────────────────────────────────┤
│ JSON Payload (variable length)                          │
│ { "characterId": "abc123", ... }                        │
└─────────────────────────────────────────────────────────┘
```

**Why binary headers?** Zero-copy routing. The Connect service extracts the 16-byte GUID and routes the message without parsing the JSON payload.

**Why JSON payloads?** Developer ergonomics. JSON is readable, debuggable, and works with any serializer.

## Capability Manifest

When you connect, the server pushes a **capability manifest** - a list of APIs you can call:

```json
{
  "eventName": "connect.capability_manifest",
  "sessionId": "abc123...",
  "availableAPIs": [
    {
      "method": "POST",
      "path": "/character/get",
      "guid": "550e8400-e29b-41d4-a716-446655440000",
      "service": "character"
    }
  ]
}
```

The manifest updates dynamically as:
- You authenticate (more APIs become available)
- Permissions change (admin access granted)
- Session state changes (entering game grants in-game APIs)

Use `client.AvailableApis` to see current capabilities:

```csharp
foreach (var api in client.AvailableApis)
{
    Console.WriteLine($"{api.Key}: {api.Value}");
}
```

## Client-Salted GUIDs

Each client receives **unique GUIDs** for the same endpoints. This prevents security exploits where one client could use another client's GUIDs:

```
Client A: POST:/character/get → GUID abc123...
Client B: POST:/character/get → GUID xyz789... (different!)
```

The BannouClient handles this automatically - you just use endpoint paths:

```csharp
// SDK looks up the correct GUID for your session
var response = await client.InvokeAsync<Req, Resp>("/character/get", request);
```

## Shortcuts

Shortcuts are **pre-bound API calls** pushed to clients. They encapsulate endpoint + parameters, allowing users to invoke complex operations with empty payloads.

Example flow:
1. User purchases game subscription
2. GameSession service pushes shortcut: `join_game_arcadia`
3. Client receives shortcut in capability manifest
4. Client invokes with empty payload - server fills in bound data

```csharp
// Wait for shortcut to appear
var shortcutGuid = client.GetServiceGuid("join_game_arcadia");
if (shortcutGuid.HasValue)
{
    // Invoke shortcut - server injects subscription/account data
    var response = await client.InvokeAsync<object, JoinGameResponse>(
        "join_game_arcadia",
        new { }, // Empty payload - server fills in the rest
        timeout: TimeSpan.FromSeconds(5));
}
```

## Response Codes

All responses include a `ResponseCode` indicating success or failure:

| Code | Name | Description |
|------|------|-------------|
| 0 | OK | Request succeeded |
| 10 | RequestError | Malformed message |
| 11 | RequestTooLarge | Payload exceeds limit |
| 12 | TooManyRequests | Rate limited |
| 20 | Unauthorized | Auth required or invalid |
| 30 | ServiceNotFound | Target GUID not in manifest |
| 50-60 | Service_* | Backend service errors |
| 70-72 | Shortcut* | Shortcut expired/revoked |

See `ResponseCodes.cs` for the complete enumeration.

## Typed Service Proxies

Instead of manually specifying HTTP methods and paths, use the generated typed proxies for compile-time safety:

```csharp
// All services have typed proxies accessible as properties
var authResponse = await client.Auth.LoginAsync(new LoginRequest
{
    Email = "player@example.com",
    Password = "password"
});

var characterResponse = await client.Character.GetAsync(new CharacterGetRequest
{
    CharacterId = characterId
});

// Available proxies include:
// client.Account, client.Auth, client.Character, client.GameSession,
// client.Matchmaking, client.Voice, client.Asset, and more...
```

The proxies handle GUID lookup, binary header construction, and response parsing automatically.

## Typed Event Subscriptions

Subscribe to specific event types with full type safety:

```csharp
// Subscribe to a specific event type - returns a disposable handle
using var chatSub = client.OnEvent<ChatMessageReceivedEvent>(evt =>
{
    Console.WriteLine($"[{evt.SenderId}]: {evt.Message}");
});

using var matchSub = client.OnEvent<MatchFoundEvent>(evt =>
{
    Console.WriteLine($"Match found! Players: {evt.PlayerCount}");
});

// Subscriptions are automatically cleaned up when disposed
// Or call subscription.Dispose() manually
```

### Generic Event Handler

For handling all events or custom event routing:

```csharp
client.OnEvent += (sender, eventData) =>
{
    switch (eventData.EventName)
    {
        case "game_session.player_joined":
            var joinEvent = BannouJson.Deserialize<PlayerJoinedEvent>(eventData.Payload);
            HandlePlayerJoined(joinEvent);
            break;

        case "connect.capability_manifest":
            // Capabilities changed - new APIs available
            break;
    }
};
```

### Service-Grouped Event Subscriptions

For better discoverability via IntelliSense, use service-grouped subscriptions:

```csharp
// Organized by service - discoverable via client.Events.{Service}.On{Event}()
using var chatSub = client.Events.GameSession.OnSessionChatReceived(evt =>
{
    Console.WriteLine($"[{evt.SenderId}]: {evt.Message}");
});

using var voiceSub = client.Events.Voice.OnVoicePeerJoined(evt =>
{
    Console.WriteLine($"Peer joined: {evt.PeerId}");
});

using var matchSub = client.Events.Matchmaking.OnMatchFound(evt =>
{
    ShowMatchUI(evt.MatchId, evt.PlayerCount);
});
```

### Event Registry

The `ClientEventRegistry` maps between event types and their string names:

```csharp
// Get the event name for a type
string? name = ClientEventRegistry.GetEventName<ChatMessageReceivedEvent>();
// Returns: "game_session.chat_received"

// Get the type for an event name
Type? type = ClientEventRegistry.GetEventType("matchmaking.match_found");
// Returns: typeof(MatchFoundEvent)

// Check if an event is registered
bool isRegistered = ClientEventRegistry.IsRegistered<VoicePeerJoinedEvent>();
```

### Runtime Type Discovery

The `ClientEndpointMetadata` class provides runtime lookup of request/response types:

```csharp
// Get request type for an endpoint
var requestType = ClientEndpointMetadata.GetRequestType("POST", "/auth/login");
// Returns: typeof(LoginRequest)

// Get response type
var responseType = ClientEndpointMetadata.GetResponseType("POST", "/character/get");
// Returns: typeof(CharacterResponse)

// Get full endpoint info
var info = ClientEndpointMetadata.GetEndpointInfo("POST", "/auth/login");
// Returns: { Method, Path, Service, RequestType, ResponseType, Summary }

// Filter endpoints by service
var authEndpoints = ClientEndpointMetadata.GetEndpointsByService("Auth");

// Check if endpoint is registered
bool exists = ClientEndpointMetadata.IsRegistered("POST", "/account/get");
```

## Meta Requests

Request endpoint metadata instead of executing:

```csharp
// Get JSON Schema for request body
var schema = await client.GetMetaAsync<JsonSchemaData>(
    MetaType.RequestSchema,
    "POST",
    "/character/create");

// Get full endpoint documentation
var fullSchema = await client.GetMetaAsync<FullSchemaData>(
    MetaType.FullSchema,
    "POST",
    "/character/create");
```

## Real-World Example: Edge Tester

The `edge-tester/` project demonstrates comprehensive Client SDK usage:

- **CapabilityFlowTestHandler.cs** - Tests capability manifest reception, unique GUIDs per session, and state-based capability updates
- **GameSessionWebSocketTestHandler.cs** - Tests the complete subscription -> shortcut -> join flow

Key patterns from edge-tester:

```csharp
// Create isolated test account
var accessToken = await CreateTestAccountAsync("test_prefix");

// Connect with token
var client = new BannouClient();
await client.ConnectWithTokenAsync(connectUrl, accessToken);

// Wait for capabilities
var deadline = DateTime.UtcNow.AddSeconds(10);
while (DateTime.UtcNow < deadline)
{
    var guid = client.GetServiceGuid("POST", "/target/endpoint");
    if (guid.HasValue) break;
    await Task.Delay(500);
}

// Invoke and handle response
var response = await client.InvokeAsync<Request, Response>(
    "POST", "/target/endpoint", request,
    timeout: TimeSpan.FromSeconds(5));

if (response.IsSuccess)
{
    // Process response.Result
}
else
{
    // Handle response.Error (ResponseCode, Message, etc.)
}
```

## Package Contents

| Component | Description |
|-----------|-------------|
| `BannouClient` | WebSocket client with connection management |
| `IBannouClient` | Interface for mocking in tests |
| `Generated/Proxies/*` | Typed service proxies (AuthProxy, CharacterProxy, etc.) |
| `Generated/Events/ClientEventRegistry` | Event type ↔ name mapping |
| `Generated/Events/ClientEndpointMetadata` | Runtime endpoint type discovery |
| `Generated/Events/*EventSubscriptions` | Service-grouped event subscriptions |
| `Generated/Events/BannouClientEvents` | Container for `client.Events.{Service}` access |
| `IEventSubscription` | Disposable event subscription handle |
| `BinaryMessage` | Binary protocol message handling |
| `ResponseCodes` | Error code enumeration |
| `MetaTypes` | Meta request/response types |
| `*Models.cs` | Generated request/response models |
| `*Events.cs` | Generated event types |
| `Behavior/Runtime/*` | ABML behavior interpreter |

## Testing with IBannouClient

The `IBannouClient` interface enables mocking for unit tests:

```csharp
// In your game code, depend on the interface
public class GameManager
{
    private readonly IBannouClient _client;

    public GameManager(IBannouClient client)
    {
        _client = client;
    }
}

// In tests, mock the client
var mockClient = new Mock<IBannouClient>();
mockClient.Setup(c => c.Auth.LoginAsync(It.IsAny<LoginRequest>(), ...))
    .ReturnsAsync(new ApiResponse<AuthResponse> { IsSuccess = true, ... });

var gameManager = new GameManager(mockClient.Object);
```

## When NOT to Use This SDK

Use **BeyondImmersion.Bannou.Server** instead if you need:
- Service-to-service communication (game servers calling Bannou services)
- Generated service clients (`IAuthClient`, `IAccountClient`, etc.)
- Mesh infrastructure integration

## Dependencies

- `System.Net.WebSockets.Client` - WebSocket connectivity

The SDK is intentionally lightweight for game client deployment.

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client
```

## License

MIT License
