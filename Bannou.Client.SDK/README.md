# Bannou Client SDK

Lightweight SDK for game clients connecting to Bannou services via WebSocket.

## Overview

The Client SDK provides everything a game client needs to communicate with Bannou:
- **WebSocket client** with automatic reconnection and binary protocol support
- **Request/response messaging** with typed payloads
- **Event reception** for real-time updates pushed from services
- **Capability manifest** for dynamic API discovery
- **Shortcuts** for subscription-based access patterns

## Quick Start

```csharp
using BeyondImmersion.Bannou.Client.SDK;

// Create client and connect
var client = new BannouClient();
await client.ConnectWithTokenAsync(connectUrl, accessToken);

// Invoke an API endpoint
var response = await client.InvokeAsync<MyRequest, MyResponse>(
    "POST",
    "/character/get",
    new MyRequest { CharacterId = "abc123" },
    timeout: TimeSpan.FromSeconds(5));

if (response.IsSuccess)
{
    Console.WriteLine($"Character: {response.Result.Name}");
}

// Handle events pushed from services
client.OnEvent += (sender, eventData) =>
{
    Console.WriteLine($"Event received: {eventData.EventName}");
};

// Clean up
await client.DisposeAsync();
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

The BannouClient handles this automatically - you just use method/path pairs:

```csharp
// SDK looks up the correct GUID for your session
var response = await client.InvokeAsync<Req, Resp>("POST", "/character/get", request);
```

## Shortcuts

Shortcuts are **pre-bound API calls** pushed to clients. They encapsulate endpoint + parameters, allowing users to invoke complex operations with empty payloads.

Example flow:
1. User purchases game subscription
2. GameSession service pushes shortcut: `SHORTCUT:join_game_arcadia`
3. Client receives shortcut in capability manifest
4. Client invokes with empty payload - server fills in bound data

```csharp
// Wait for shortcut to appear
var shortcutGuid = client.GetServiceGuid("SHORTCUT", "join_game_arcadia");
if (shortcutGuid.HasValue)
{
    // Invoke shortcut - server injects subscription/account data
    var response = await client.InvokeAsync<object, JoinGameResponse>(
        "SHORTCUT",
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

## Event Handling

Services push events for real-time updates:

```csharp
client.OnEvent += (sender, eventData) =>
{
    switch (eventData.EventName)
    {
        case "character.updated":
            var update = JsonSerializer.Deserialize<CharacterUpdatedEvent>(eventData.Payload);
            break;

        case "connect.capability_manifest":
            // Capabilities changed - new APIs available
            break;
    }
};
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
| `BinaryMessage` | Binary protocol message handling |
| `ResponseCodes` | Error code enumeration |
| `MetaTypes` | Meta request/response types |
| `*Models.cs` | Generated request/response models |
| `*Events.cs` | Generated event types |
| `Behavior/Runtime/*` | ABML behavior interpreter |

## When NOT to Use This SDK

Use **Bannou.SDK** instead if you need:
- Service-to-service communication (game servers calling Bannou services)
- Generated service clients (`IAuthClient`, `IAccountClient`, etc.)
- Mesh infrastructure integration

## Dependencies

- `System.Net.WebSockets.Client` - WebSocket connectivity

The SDK is intentionally lightweight for game client deployment.

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client.SDK
```

## License

MIT License
