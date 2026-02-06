# Bannou TypeScript SDK

TypeScript client SDK for connecting to Bannou services via WebSocket. Provides typed service proxies, binary protocol handling, and event subscriptions for browser and Node.js environments.

## Packages

| Package                          | Description                                    |
| -------------------------------- | ---------------------------------------------- |
| `@beyondimmersion/bannou-core`   | Shared types, JSON helpers, base event types   |
| `@beyondimmersion/bannou-client` | WebSocket client with typed proxies and events |

## Features

- **Binary WebSocket Protocol**: 31-byte request / 16-byte response headers with big-endian encoding
- **Typed Service Proxies**: Generated `AuthProxy`, `CharacterProxy`, etc. for compile-time safety
- **Event Subscriptions**: Type-safe handlers for server-push events
- **Capability Manifest**: Dynamic API discovery based on authentication state
- **Dual Environment**: Works in browser (native WebSocket) and Node.js (ws package)

## Installation

```bash
npm install @beyondimmersion/bannou-core @beyondimmersion/bannou-client
```

For Node.js, also install the WebSocket peer dependency:

```bash
npm install ws
```

## Quick Start

```typescript
import { BannouClient } from '@beyondimmersion/bannou-client';

// Create client
const client = new BannouClient();

// Connect with JWT token
await client.connectAsync('wss://bannou.example.com/connect', accessToken);

// Use typed service proxies
const loginResponse = await client.auth.loginAsync({
  email: 'player@example.com',
  password: 'password',
});

if (loginResponse.isSuccess) {
  console.log('Logged in:', loginResponse.data);
}

// Subscribe to events
client.onEvent('game_session.chat_received', (event) => {
  console.log(`Chat: ${event.message}`);
});

// Clean up
await client.disconnectAsync();
```

## Project Structure

```
sdks/typescript/
├── core/                  # @beyondimmersion/bannou-core
│   └── src/
│       ├── ApiResponse.ts       # Response wrapper type
│       ├── BannouJson.ts        # JSON serialization helpers
│       └── BaseClientEvent.ts   # Base event types
│
├── client/                # @beyondimmersion/bannou-client
│   └── src/
│       ├── BannouClient.ts      # Main client class
│       ├── IBannouClient.ts     # Client interface (for mocking)
│       ├── protocol/            # Binary protocol implementation
│       │   ├── BinaryMessage.ts
│       │   ├── MessageFlags.ts
│       │   ├── NetworkByteOrder.ts
│       │   └── ResponseCodes.ts
│       └── Generated/           # Auto-generated from schemas
│           ├── types/           # OpenAPI types
│           ├── proxies/         # Service proxy classes
│           └── events/          # Event registry
│
└── package.json           # Workspace root
```

## Development

### Prerequisites

- Node.js 18+
- npm or pnpm

### Build Commands

```bash
# Install dependencies
npm install

# Generate types and proxies from schemas
make generate-sdk-ts

# Build packages
make build-sdk-ts

# Run tests
make test-sdk-ts

# Type-check without building
make typecheck-sdk-ts

# Format code
make format-sdk-ts
```

### Code Generation

The SDK uses schema-first development. Types and proxies are generated from OpenAPI schemas:

1. **Consolidated Schema**: `schemas/Generated/bannou-client-api.yaml` - merged from all service schemas, filtered for client-accessible endpoints
2. **Type Generation**: `openapi-typescript` generates TypeScript types
3. **Proxy Generation**: `scripts/generate-client-proxies-ts.py` generates service proxy classes
4. **Event Registry**: `scripts/generate-client-event-registry-ts.py` generates event type mappings

To regenerate after schema changes:

```bash
make generate-sdk-ts
```

## Binary Protocol

The SDK implements Bannou's binary WebSocket protocol:

### Request Header (31 bytes)

| Offset | Size | Field        | Description                             |
| ------ | ---- | ------------ | --------------------------------------- |
| 0      | 1    | Flags        | Message flags (binary, encrypted, etc.) |
| 1      | 2    | Channel      | Message channel for ordering            |
| 3      | 4    | Sequence     | Sequence number within channel          |
| 7      | 16   | Service GUID | Target service endpoint GUID            |
| 23     | 8    | Message ID   | Correlation ID for response matching    |

### Response Header (16 bytes)

| Offset | Size | Field          | Description                 |
| ------ | ---- | -------------- | --------------------------- |
| 0      | 1    | Flags          | Response flags              |
| 1      | 1    | Response Code  | Status code (0 = OK)        |
| 2      | 2    | Reserved       | Reserved for future use     |
| 4      | 4    | Payload Length | JSON payload length         |
| 8      | 8    | Message ID     | Correlation ID from request |

All multi-byte integers use **big-endian** (network) byte order.

## Service Proxies

Generated proxy classes provide type-safe API access:

```typescript
// Auth operations
const response = await client.auth.loginAsync(request);
const validated = await client.auth.validateAsync({ token });

// Character operations
const character = await client.character.getAsync({ characterId });
const characters = await client.character.listAsync({ realmId });

// Game session operations
const session = await client.sessions.createAsync({ gameType });
await client.sessions.joinAsync({ sessionId });
```

Each proxy method returns `Promise<ApiResponse<T>>` with:

- `isSuccess`: Whether the request succeeded
- `data`: Response payload (if successful)
- `error`: Error information (if failed)
- `statusCode`: HTTP-equivalent status code

## Event Handling

Subscribe to server-push events:

```typescript
// Type-safe event subscription
client.onEvent('game_session.player_joined', (event) => {
  console.log(`Player joined: ${event.playerId}`);
});

client.onEvent('matchmaking.match_found', (event) => {
  console.log(`Match found: ${event.matchId}`);
});

// Remove subscription
client.offEvent('game_session.player_joined', handler);
```

## Testing

```bash
# Run all tests
npm test

# Run tests in watch mode
npm run test:watch
```

## Further Reading

- [TypeScript SDK Guide](../../docs/guides/TYPESCRIPT-SDK.md) - Detailed integration guide
- [WebSocket Protocol](../../docs/WEBSOCKET-PROTOCOL.md) - Protocol specification
- [SDKs Overview](../../docs/guides/SDK-OVERVIEW.md) - All Bannou SDKs

## License

MIT
