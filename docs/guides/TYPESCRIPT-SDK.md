# TypeScript SDK Integration Guide

This guide covers integrating the Bannou TypeScript SDK into browser and Node.js applications.

## Overview

The TypeScript SDK provides:
- WebSocket client with binary protocol support
- Typed service proxies for all 309 client-accessible endpoints
- Event subscriptions for 35 server-push event types
- Request correlation with timeout handling
- Automatic capability manifest management

## Installation

### npm

```bash
npm install @beyondimmersion/bannou-core @beyondimmersion/bannou-client
```

### For Node.js

The SDK uses the `ws` package as an optional peer dependency for Node.js:

```bash
npm install ws
```

In browser environments, the native WebSocket API is used automatically.

## Basic Usage

### Connecting

```typescript
import { BannouClient } from '@beyondimmersion/bannou-client';

const client = new BannouClient();

// Connect with an access token (obtained via HTTP login first)
await client.connectAsync('wss://bannou.example.com/connect', accessToken);

// Connection state
console.log(client.isConnected);  // true
console.log(client.sessionId);    // Server-assigned session ID
```

### Authentication Flow

The typical authentication flow for web applications:

```typescript
// 1. Login via HTTP to get tokens
const loginResponse = await fetch('https://bannou.example.com/auth/login', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, password })
});
const { accessToken, refreshToken } = await loginResponse.json();

// 2. Connect WebSocket with access token
const client = new BannouClient();
await client.connectAsync('wss://bannou.example.com/connect', accessToken);

// 3. Use typed proxies for subsequent API calls
const profile = await client.account.getAsync({ accountId });
```

### Using Service Proxies

Service proxies provide type-safe access to all API endpoints:

```typescript
// Authentication
const loginResult = await client.auth.loginAsync({
  email: 'player@example.com',
  password: 'password'
});

if (loginResult.isSuccess) {
  console.log('Login successful:', loginResult.data.accountId);
} else {
  console.error('Login failed:', loginResult.error);
}

// Character operations
const characters = await client.character.listAsync({
  realmId: 'my-realm',
  limit: 10
});

// Game sessions
const session = await client.sessions.createAsync({
  gameType: 'my-game',
  maxPlayers: 4,
  isPrivate: false
});

await client.sessions.joinAsync({
  sessionId: session.data.sessionId
});
```

### Handling Events

Subscribe to server-push events:

```typescript
// Game session events
client.onEvent('game_session.player_joined', (event) => {
  console.log(`Player ${event.playerId} joined the session`);
  updatePlayerList(event.players);
});

client.onEvent('game_session.chat_received', (event) => {
  addChatMessage(event.senderId, event.message);
});

client.onEvent('game_session.state_updated', (event) => {
  updateGameState(event.state);
});

// Matchmaking events
client.onEvent('matchmaking.match_found', (event) => {
  showMatchFoundDialog(event.matchId, event.players);
});

client.onEvent('matchmaking.queue_joined', (event) => {
  updateQueueStatus(event.position, event.estimatedWait);
});

// Voice events
client.onEvent('voice.peer_joined', (event) => {
  initializeVoiceForPeer(event.peerId, event.sdpOffer);
});
```

### Error Handling

```typescript
const response = await client.character.getAsync({ characterId: 'invalid' });

if (!response.isSuccess) {
  switch (response.statusCode) {
    case 404:
      console.log('Character not found');
      break;
    case 401:
      console.log('Not authenticated');
      await refreshTokenAndRetry();
      break;
    case 403:
      console.log('Access denied');
      break;
    default:
      console.error('Request failed:', response.error);
  }
}
```

### Disconnecting

```typescript
// Graceful disconnect
await client.disconnectAsync();

// Connection state events
client.onDisconnect(() => {
  showReconnectDialog();
});

client.onReconnect(() => {
  hideReconnectDialog();
  refreshGameState();
});
```

## Advanced Usage

### Request Timeouts

```typescript
// Set default timeout (milliseconds)
const client = new BannouClient({ defaultTimeout: 30000 });

// Per-request timeout
const response = await client.character.getAsync(
  { characterId },
  { timeout: 5000 }
);
```

### Message Channels

Channels ensure message ordering within groups:

```typescript
// Default channel (0) - general requests
await client.auth.validateAsync({ token });

// Channel 1 - game state updates (ordered separately)
await client.sessions.actionsAsync({ action: 'move', data }, { channel: 1 });

// Channel 2 - chat messages (won't block game actions)
await client.sessions.chatAsync({ message: 'Hello!' }, { channel: 2 });
```

### Capability Manifest

The client receives a capability manifest on connect, describing available APIs:

```typescript
// Access the capability manifest
const capabilities = client.capabilities;

// Check if an endpoint is available
const canCreateSession = capabilities.has('sessions/create');

// Capabilities update after authentication changes
client.onCapabilitiesUpdated((newCapabilities) => {
  updateAvailableFeatures(newCapabilities);
});
```

### Custom Request Headers

```typescript
// Add custom correlation headers for debugging
const response = await client.character.getAsync(
  { characterId },
  {
    headers: {
      'X-Request-Id': generateRequestId(),
      'X-Client-Version': '1.0.0'
    }
  }
);
```

## Browser Integration

### React Example

```tsx
import { useState, useEffect } from 'react';
import { BannouClient } from '@beyondimmersion/bannou-client';

function useBannouClient(serverUrl: string, token: string | null) {
  const [client, setClient] = useState<BannouClient | null>(null);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (!token) return;

    const bannouClient = new BannouClient();

    bannouClient.connectAsync(serverUrl, token)
      .then(() => {
        setClient(bannouClient);
        setConnected(true);
      })
      .catch(console.error);

    return () => {
      bannouClient.disconnectAsync();
    };
  }, [serverUrl, token]);

  return { client, connected };
}

function GameLobby({ token }: { token: string }) {
  const { client, connected } = useBannouClient(
    'wss://bannou.example.com/connect',
    token
  );
  const [sessions, setSessions] = useState([]);

  useEffect(() => {
    if (!client || !connected) return;

    // Load available sessions
    client.sessions.listAsync({ limit: 20 })
      .then(response => {
        if (response.isSuccess) {
          setSessions(response.data.sessions);
        }
      });

    // Subscribe to session updates
    client.onEvent('game_session.state_changed', (event) => {
      // Refresh session list
    });
  }, [client, connected]);

  return (
    <div>
      {sessions.map(session => (
        <SessionCard key={session.id} session={session} />
      ))}
    </div>
  );
}
```

### Vue Example

```vue
<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { BannouClient } from '@beyondimmersion/bannou-client';

const props = defineProps<{ token: string }>();

const client = ref<BannouClient | null>(null);
const connected = ref(false);
const characters = ref([]);

onMounted(async () => {
  const bannouClient = new BannouClient();
  await bannouClient.connectAsync('wss://bannou.example.com/connect', props.token);

  client.value = bannouClient;
  connected.value = true;

  // Load characters
  const response = await bannouClient.character.listAsync({ realmId: 'my-realm' });
  if (response.isSuccess) {
    characters.value = response.data.characters;
  }
});

onUnmounted(() => {
  client.value?.disconnectAsync();
});
</script>

<template>
  <div v-if="connected">
    <CharacterList :characters="characters" />
  </div>
  <div v-else>
    Connecting...
  </div>
</template>
```

## Node.js Integration

### Server-Side Usage

```typescript
import { BannouClient } from '@beyondimmersion/bannou-client';

// For server-side applications (game servers, bots, etc.)
async function createBotClient(botToken: string) {
  const client = new BannouClient();
  await client.connectAsync('wss://bannou.example.com/connect', botToken);

  // Bot joins a game session
  await client.sessions.joinAsync({ sessionId: 'target-session' });

  // Listen for game events
  client.onEvent('game_session.action_result', (event) => {
    // Process game action results
    handleBotResponse(event);
  });

  return client;
}
```

## Testing

### Mocking the Client

The SDK provides an interface for easy mocking:

```typescript
import { IBannouClient } from '@beyondimmersion/bannou-client';
import { vi } from 'vitest';

// Create a mock client
const mockClient: IBannouClient = {
  isConnected: true,
  connectAsync: vi.fn().mockResolvedValue(undefined),
  disconnectAsync: vi.fn().mockResolvedValue(undefined),
  auth: {
    loginAsync: vi.fn().mockResolvedValue({
      isSuccess: true,
      data: { accountId: 'test-account', accessToken: 'token' }
    })
  },
  // ... other proxies
};

// Use in tests
test('login flow', async () => {
  const result = await mockClient.auth.loginAsync({
    email: 'test@example.com',
    password: 'password'
  });

  expect(result.isSuccess).toBe(true);
  expect(mockClient.auth.loginAsync).toHaveBeenCalled();
});
```

## Troubleshooting

### Connection Issues

```typescript
client.onError((error) => {
  if (error.code === 'CONNECTION_TIMEOUT') {
    console.log('Connection timed out, retrying...');
  } else if (error.code === 'AUTH_FAILED') {
    console.log('Authentication failed, token may be expired');
  }
});
```

### Debug Logging

```typescript
// Enable debug logging
const client = new BannouClient({
  debug: true,
  logger: console
});

// Custom logger
const client = new BannouClient({
  logger: {
    debug: (msg) => myLogger.debug('[Bannou]', msg),
    info: (msg) => myLogger.info('[Bannou]', msg),
    warn: (msg) => myLogger.warn('[Bannou]', msg),
    error: (msg) => myLogger.error('[Bannou]', msg),
  }
});
```

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| `WebSocket is not defined` | Node.js without ws package | `npm install ws` |
| Connection closes immediately | Invalid or expired token | Refresh token and reconnect |
| Requests timeout | Server overloaded or network issues | Check network, increase timeout |
| Events not received | Not subscribed or wrong event name | Check event name spelling |

## Related Documentation

- [TypeScript SDK README](../../sdks/typescript/README.md) - Package overview
- [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) - Binary protocol specification
- [SDKs Overview](SDK-OVERVIEW.md) - All Bannou SDKs
- [Client Integration](CLIENT-INTEGRATION.md) - General client integration patterns
