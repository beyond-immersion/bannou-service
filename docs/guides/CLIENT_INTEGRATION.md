# Client Integration Guide

This guide covers integrating game clients with Bannou services via the WebSocket protocol.

## Overview

Bannou provides two SDKs for different use cases:

| SDK | Use Case | Dependencies |
|-----|----------|--------------|
| **Server SDK** (`BeyondImmersion.Bannou.Server`) | Service-to-service communication | .NET, lib-mesh |
| **Client SDK** (`BeyondImmersion.Bannou.Client`) | Game clients | WebSocket only |

This guide focuses on the **Client SDK** for game engine integration (Unity, Unreal, etc.).

## Authentication Flow

### 1. Register Account

```http
POST /auth/register
Content-Type: application/json

{
  "email": "player@example.com",
  "password": "secure-password",
  "username": "PlayerOne"
}
```

Response:
```json
{
  "account_id": "abc123...",
  "username": "PlayerOne",
  "email": "player@example.com"
}
```

### 2. Login

```http
POST /auth/login
Content-Type: application/json

{
  "email": "player@example.com",
  "password": "secure-password"
}
```

Response:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "connect_url": "ws://server.example.com/connect",
  "expires_at": "2025-01-15T12:00:00Z"
}
```

### 3. OAuth Authentication

For Steam, Discord, Google, or Twitch authentication:

```http
POST /auth/oauth/start
Content-Type: application/json

{
  "provider": "steam",
  "redirect_uri": "myapp://oauth-callback"
}
```

Complete the OAuth flow, then exchange the code:

```http
POST /auth/oauth/complete
Content-Type: application/json

{
  "provider": "steam",
  "code": "oauth-code-from-provider"
}
```

## WebSocket Connection

### Connecting

Use the `connect_url` from login response:

```javascript
// TypeScript example
const ws = new WebSocket(connectUrl);

ws.onopen = () => {
  // Send authentication message
  const authMessage = createAuthMessage(token);
  ws.send(authMessage);
};

ws.onmessage = (event) => {
  const message = parseMessage(event.data);
  handleMessage(message);
};
```

### Internal Mode (Servers / Agents)
- Game servers that need direct access to Event/Character agents via internal Connect nodes can use the server SDK’s `BannouClient`:
  ```csharp
  var client = new BannouClient();
  await client.ConnectInternalAsync("ws://bannou-internal/connect", serviceToken: "shared-secret");
  // subscribe to events (opportunities, extensions) and forward into the game transport
  ```
- This bypasses JWT login and uses either service-token or network-trust, as configured on Connect.

### Game Transport (UDP) Summary
- For gameplay state, use the UDP transport helpers (LiteNetLib) in the SDKs:
  - Envelope: `GameProtocolEnvelope` (version + `GameMessageType`)
  - DTOs: snapshots/deltas, combat events, opportunities, input, cinematic extensions
  - Transports: `LiteNetLibServerTransport` / `LiteNetLibClientTransport`
- Typical flow: WebSocket to Connect for capabilities/events; UDP for 60 Hz state and opportunity prompts to clients.

### Binary Protocol

Messages use a 31-byte binary header + JSON payload:

```
┌─────────────────────────────────────────────────────────┐
│ Header (31 bytes)                                       │
├──────────┬─────────┬──────────┬──────────────┬──────────┤
│ Flags    │ Channel │ Sequence │ Service GUID │ Msg ID   │
│ (1 byte) │ (2)     │ (4)      │ (16)         │ (8)      │
├─────────────────────────────────────────────────────────┤
│ JSON Payload (variable)                                 │
│ {"account_id": "abc123", ...}                           │
└─────────────────────────────────────────────────────────┘
```

**Message Flags**:
| Bit | Flag | Description |
|-----|------|-------------|
| 0 | Binary | Payload is binary (not JSON) |
| 1 | Encrypted | Payload is encrypted |
| 2 | Compressed | Payload is compressed |
| 3 | HighPriority | Message should be prioritized |
| 4 | Event | Fire-and-forget, no response expected |
| 5 | Client | Route to another WebSocket client (P2P) |
| 6 | Response | Response to a request |
| 7 | Reserved | Reserved for future use |

**Channels**: Channels are 16-bit unsigned integers (0-65535) used for message ordering. Channel 0 is the default. Use different channels to ensure independent message sequencing for different types of traffic (e.g., separate channels for gameplay vs. chat).

See [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) for complete specification.

## Capability Manifest

After authentication, you receive a capability manifest listing available APIs:

```json
{
  "session_id": "session-uuid",
  "capabilities": [
    {
      "name": "account/get",
      "guid": "abc123-...",
      "method": "POST",
      "requires_auth": true,
      "roles": ["user", "admin"]
    },
    {
      "name": "game-session/join",
      "guid": "def456-...",
      "method": "POST",
      "requires_auth": true,
      "roles": ["user"]
    }
  ],
  "shortcuts": [],
  "version": 1,
  "generated_at": "2025-01-15T10:00:00Z"
}
```

**Key concepts**:
- Each capability has a unique **GUID** for this session
- GUIDs are **client-salted** - different clients get different GUIDs for the same endpoint
- The manifest updates when permissions change or services deploy updates
- Use the **name** for display, the **GUID** for routing

## Making API Requests

### Building a Request

1. Find the capability GUID from the manifest
2. Construct the binary header
3. Serialize the request as JSON
4. Send header + payload

```javascript
function sendRequest(capabilityName, payload) {
  const capability = findCapability(capabilityName);
  if (!capability) {
    throw new Error(`Capability not found: ${capabilityName}`);
  }

  const header = buildHeader({
    flags: 0x00,
    channel: getChannelForService(capability.name),
    sequence: getNextSequence(),
    serviceGuid: capability.guid,
    messageId: generateMessageId()
  });

  const jsonPayload = JSON.stringify(payload);
  const message = concatenate(header, jsonPayload);

  ws.send(message);
}

// Example usage
sendRequest('account/get', { account_id: 'abc123' });
```

### Handling Responses

Responses include a 16-byte header:

```
┌─────────────────────────────────────────────────────────┐
│ Response Header (16 bytes)                              │
├──────────┬─────────┬──────────┬──────────┬──────────────┤
│ Flags    │ Channel │ Sequence │ Msg ID   │ Status Code  │
│ (1 byte) │ (2)     │ (4)      │ (8)      │ (1)          │
├─────────────────────────────────────────────────────────┤
│ JSON Payload (variable)                                 │
└─────────────────────────────────────────────────────────┘
```

**Status Codes**:
| Code | Meaning |
|------|---------|
| 0x00 | Success |
| 0x01 | Bad Request |
| 0x02 | Unauthorized |
| 0x03 | Forbidden |
| 0x04 | Not Found |
| 0x05 | Internal Error |
| 0x06 | Service Unavailable |

```javascript
ws.onmessage = (event) => {
  const buffer = event.data;
  const header = parseResponseHeader(buffer.slice(0, 16));
  const payload = JSON.parse(buffer.slice(16));

  if (header.statusCode === 0x00) {
    handleSuccess(header.messageId, payload);
  } else {
    handleError(header.messageId, header.statusCode, payload);
  }
};
```

## Session Shortcuts

Services can push pre-configured API calls to simplify common operations:

```json
{
  "shortcuts": [
    {
      "guid": "shortcut-uuid",
      "target_service": "game-session",
      "target_endpoint": "character/get-stats",
      "metadata": {
        "name": "get_my_character_stats",
        "description": "Get current character's stats"
      }
    }
  ]
}
```

**Using shortcuts**:
1. Find the shortcut by name
2. Send a message with the shortcut GUID
3. Use an **empty payload** - the server fills in the bound parameters

```javascript
// Instead of:
sendRequest('game-session/character/get-stats', { character_id: 'luna-001' });

// With shortcuts:
sendShortcut('get_my_character_stats', {});  // Empty payload - server knows the character
```

Shortcuts update dynamically:
- When you possess a character, you get shortcuts for that character
- When you join a realm, you get shortcuts for realm operations
- When permissions change, shortcuts update accordingly

## Server Events

The server can push events to clients:

```javascript
ws.onmessage = (event) => {
  const buffer = event.data;
  const header = parseHeader(buffer);

  if (header.flags & 0x10) {  // Event flag
    handleServerEvent(header, parsePayload(buffer));
  } else if (header.flags & 0x40) {  // Response flag
    handleResponse(header, parsePayload(buffer));
  }
};

function handleServerEvent(header, payload) {
  switch (payload.event_type) {
    case 'capabilities_updated':
      updateCapabilities(payload.capabilities);
      break;
    case 'session_invalidated':
      handleDisconnect(payload.reconnection_token);
      break;
    case 'world_state_update':
      updateWorldState(payload.state);
      break;
  }
}
```

## Reconnection

When disconnected, use the reconnection token to restore session state:

```javascript
ws.onclose = (event) => {
  if (reconnectionToken) {
    reconnect(reconnectionToken);
  } else {
    requireNewLogin();
  }
};

async function reconnect(token) {
  const newWs = new WebSocket(connectUrl);

  newWs.onopen = () => {
    const reconnectMessage = createReconnectMessage(token);
    newWs.send(reconnectMessage);
  };

  newWs.onmessage = (event) => {
    const response = parseMessage(event.data);
    if (response.success) {
      // Session restored - capabilities preserved
      ws = newWs;
    } else {
      requireNewLogin();
    }
  };
}
```

## Error Handling

### Common Errors

| Error | Cause | Solution |
|-------|-------|----------|
| Unauthorized | Invalid/expired token | Re-authenticate |
| Forbidden | Missing permission | Check capabilities |
| Not Found | Invalid GUID | Refresh capabilities |
| Service Unavailable | Service down | Retry with backoff |

### Retry Strategy

```javascript
async function sendWithRetry(capabilityName, payload, maxRetries = 3) {
  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      return await sendRequest(capabilityName, payload);
    } catch (error) {
      if (error.code === 'SERVICE_UNAVAILABLE' && attempt < maxRetries - 1) {
        await sleep(1000 * Math.pow(2, attempt));  // Exponential backoff
        continue;
      }
      throw error;
    }
  }
}
```

## SDK Installation

### NuGet (Unity/.NET)

```bash
dotnet add package BeyondImmersion.Bannou.Client
```

### npm (TypeScript)

```bash
npm install @beyondimmersion/bannou-client
```

The .NET SDK provides:
- **Typed service proxies** - Compile-time safe API calls (`client.Auth.LoginAsync()`)
- **Typed event subscriptions** - Strongly-typed event handlers with disposable patterns
- **IBannouClient interface** - For dependency injection and mocking in tests
- Binary header serialization/deserialization
- Capability manifest management
- Connection state management
- Automatic reconnection

## Using the .NET Client SDK

### Typed Service Proxies (Recommended)

The SDK generates typed proxies for all Bannou services:

```csharp
using BeyondImmersion.Bannou.Client;

var client = new BannouClient();
await client.ConnectWithTokenAsync(connectUrl, accessToken);

// Use typed proxies - no manual method/path specification
var loginResponse = await client.Auth.LoginAsync(new LoginRequest
{
    Email = "player@example.com",
    Password = "password"
});

if (loginResponse.IsSuccess)
{
    var token = loginResponse.Result.Token;
}

// All services are available as properties:
// client.Account, client.Auth, client.Character, client.GameSession,
// client.Matchmaking, client.Voice, client.Asset, etc.
```

### Typed Event Subscriptions

Subscribe to specific event types with full type safety:

```csharp
// Returns a disposable subscription handle
using var chatSub = client.OnEvent<ChatMessageReceivedEvent>(evt =>
{
    Console.WriteLine($"[{evt.SenderId}]: {evt.Message}");
});

using var matchSub = client.OnEvent<MatchFoundEvent>(evt =>
{
    ShowMatchFoundUI(evt.MatchId, evt.PlayerCount);
});

// Subscriptions automatically unsubscribe when disposed
```

### IBannouClient for Testing

The `IBannouClient` interface enables dependency injection and mocking:

```csharp
public class GameManager
{
    private readonly IBannouClient _client;

    public GameManager(IBannouClient client)
    {
        _client = client;
    }

    public async Task JoinGameAsync(string sessionId)
    {
        var response = await _client.GameSession.JoinSessionAsync(
            new JoinSessionRequest { SessionId = sessionId });
        // ...
    }
}
```

## Example: Complete Client Flow

```javascript
import { BannouClient } from '@beyondimmersion/bannou-client';

// Initialize
const client = new BannouClient({
  httpEndpoint: 'https://api.example.com',
  wsEndpoint: 'wss://ws.example.com/connect'
});

// Login
const session = await client.auth.login({
  email: 'player@example.com',
  password: 'password'
});

// Connect WebSocket
await client.connect(session.token);

// Wait for capabilities
await client.waitForCapabilities();

// Make API calls
const account = await client.call('account/get', {
  account_id: session.account_id
});

// Use shortcuts when available
const stats = await client.callShortcut('get_my_character_stats');

// Handle events
client.on('world_state_update', (state) => {
  updateGameWorld(state);
});

// Disconnect
client.disconnect();
```

## Next Steps

- [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) - Complete protocol specification
- [Testing Guide](TESTING.md) - Test client integration
- [NuGet Setup](../operations/NUGET_SETUP.md) - SDK package details
