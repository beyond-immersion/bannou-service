# Connect Plugin Deep Dive

> **Plugin**: lib-connect
> **Schema**: schemas/connect-api.yaml
> **Version**: 2.0.0
> **State Stores**: connect-statestore (Redis)

---

## Overview

WebSocket-first edge gateway service providing zero-copy binary message routing between game clients and backend Bannou services. Manages persistent WebSocket connections with a 31-byte binary protocol header for request routing and a 16-byte response header. Implements client-salted GUID generation (SHA256-based, version 5/6/7 UUIDs) to prevent cross-session security exploits. Supports three connection modes (external, relayed, internal) with per-mode behavior differences for broadcast, auth, and capability handling. Features session shortcuts (pre-bound payload routing for game-specific flows), reconnection windows with token-based session restoration, per-session RabbitMQ subscriptions for server-to-client event delivery, rate limiting, meta endpoint introspection, peer-to-peer client routing, broadcast messaging, internal proxy for stateless HTTP forwarding, and admin notification forwarding for service error events. Registered as Singleton lifetime (unusual for Bannou services) because it maintains in-memory WebSocket connection state across all requests.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for session state, mappings, heartbeats, reconnection tokens, account indexes |
| lib-messaging (`IMessageBus`) | Publishing session lifecycle events, error events, permission recompile triggers, RPC response forwarding |
| lib-messaging (`IMessageSubscriber`) | Per-session dynamic RabbitMQ subscriptions for client event delivery |
| lib-mesh (`IMeshInvocationClient`) | Service invocation for routing WebSocket messages to backend services via ServiceNavigator |
| lib-auth (`IAuthClient`) | Token validation for WebSocket connection authentication |
| Permission service (via events) | Receives `SessionCapabilitiesEvent` containing permissions; no direct API call |
| Auth service (via events) | Receives `session.invalidated` events to force-disconnect sessions |
| `IServiceAppMappingResolver` | Dynamic app-id resolution for distributed service routing |
| `IHttpClientFactory` | Named HTTP client ("ConnectMeshProxy") for internal proxy requests |
| `IServiceScopeFactory` | Creates scoped DI containers for per-request ServiceNavigator resolution |
| `ICapabilityManifestBuilder` | Builds capability manifest JSON from service mappings for client delivery |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-auth | Publishes `session.invalidated` events consumed by Connect to disconnect clients |
| lib-permission | Pushes `SessionCapabilitiesEvent` via per-session RabbitMQ queue to update client capabilities |
| lib-game-session | Publishes session shortcuts and subscribes to `session.connected`/`session.disconnected` |
| lib-actor | Subscribes to `session.connected`/`session.disconnected` events for actor lifecycle |
| lib-matchmaking | Subscribes to `session.connected`/`session.disconnected` events for queue management |
| All services (via IClientEventPublisher) | Send server-to-client events through per-session RabbitMQ queues consumed by Connect |

---

## State Storage

**Stores**: 1 state store (connect-statestore, Redis, prefix: `connect`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ws-session:{sessionId}` | `ConnectionStateData` | Full connection state (account, timestamps, reconnection info, roles, authorizations) |
| `ws-mappings:{sessionId}` | `Dictionary<string, Guid>` | Service endpoint to client-salted GUID mappings |
| `heartbeat:{sessionId}` | `SessionHeartbeat` | Connection liveness tracking (instance ID, last seen, connection count) |
| `reconnect:{token}` | `string` (sessionId) | Reconnection token to session ID mapping (TTL = reconnection window) |
| `account-sessions:{accountId:N}` | `HashSet<string>` | All active session IDs for an account (for GetAccountSessions endpoint) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `session.connected` | `SessionConnectedEvent` | WebSocket client successfully connects and authenticates |
| `session.disconnected` | `SessionDisconnectedEvent` | WebSocket client disconnects (graceful or unexpected) |
| `session.reconnected` | `SessionReconnectedEvent` | Client reconnects within grace period using reconnection token |
| `connect.session-events` | `SessionEvent` | Internal session lifecycle events for cross-instance communication |
| `bannou.permission-recompile` | `PermissionRecompileEvent` | Triggered when a new service registers (notifies Permission to recompile) |
| `service.error` | (via `TryPublishErrorAsync`) | Published on internal failures (state access, routing, WebSocket errors) |
| `{pendingRPC.ResponseChannel}` | `ClientRPCResponseEvent` | Forwards client RPC responses back to originating service |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `session.invalidated` | `SessionInvalidatedEvent` | `HandleSessionInvalidatedAsync` - Disconnects affected WebSocket clients when Auth invalidates sessions |
| `service.error` | `ServiceErrorEvent` | `HandleServiceErrorAsync` - Forwards error events to connected admin WebSocket clients as binary messages |
| Per-session queue (`CONNECT_SESSION_{sessionId}`) | Raw bytes | `HandleClientEventAsync` - Routes client events from RabbitMQ to WebSocket; handles internal events (capabilities, shortcuts) |
| `/events/auth-events` (HTTP) | `AuthEvent` | `ProcessAuthEventAsync` - Handles login/logout/token refresh from Auth service |
| `/events/service-registered` (HTTP) | `ServiceRegistrationEvent` | `ProcessServiceRegistrationAsync` - Triggers permission recompilation on new service |
| `/events/client-messages` (HTTP) | `ClientMessageEvent` | `ProcessClientMessageEventAsync` - Server-to-client push messaging |
| `/events/client-rpc` (HTTP) | `ClientRPCEvent` | `ProcessClientRPCEventAsync` - Bidirectional RPC (service calls client, expects response) |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxConcurrentConnections` | `CONNECT_MAX_CONCURRENT_CONNECTIONS` | `10000` | Maximum concurrent WebSocket connections |
| `HeartbeatIntervalSeconds` | `CONNECT_HEARTBEAT_INTERVAL_SECONDS` | `30` | Interval between heartbeat messages |
| `MessageQueueSize` | `CONNECT_MESSAGE_QUEUE_SIZE` | `1000` | Maximum queued messages per connection |
| `BinaryProtocolVersion` | `CONNECT_BINARY_PROTOCOL_VERSION` | `"2.0"` | Binary protocol version identifier |
| `BufferSize` | `CONNECT_BUFFER_SIZE` | `65536` | Size of message receive buffers (bytes) |
| `DefaultServices` | `CONNECT_DEFAULT_SERVICES` | `["auth", "website"]` | Services available to unauthenticated connections |
| `AuthenticatedServices` | `CONNECT_AUTHENTICATED_SERVICES` | `["account", "behavior", "permission", "gamesession"]` | Additional services for authenticated connections |
| `EnableClientToClientRouting` | `CONNECT_ENABLE_CLIENT_TO_CLIENT_ROUTING` | `true` | Enable peer-to-peer message routing |
| `MaxMessagesPerMinute` | `CONNECT_MAX_MESSAGES_PER_MINUTE` | `1000` | Rate limit per client per window |
| `RateLimitWindowMinutes` | `CONNECT_RATE_LIMIT_WINDOW_MINUTES` | `1` | Rate limit window duration |
| `RabbitMqConnectionString` | `CONNECT_RABBITMQ_CONNECTION_STRING` | `"amqp://guest:guest@rabbitmq:5672"` | RabbitMQ connection for client event subscriptions |
| `ServerSalt` | `CONNECT_SERVER_SALT` | `"bannou-dev-connect-salt-change-in-production"` | Server salt for GUID generation (REQUIRED, shared across instances) |
| `ConnectUrl` | `CONNECT_URL` | `null` | WebSocket URL for reconnection (defaults to `wss://{domain}/connect`) |
| `ConnectionMode` | `CONNECT_CONNECTION_MODE` | `"external"` | Connection mode: external/relayed/internal |
| `InternalAuthMode` | `CONNECT_INTERNAL_AUTH_MODE` | `"service-token"` | Auth mode for internal: service-token or network-trust |
| `InternalServiceToken` | `CONNECT_INTERNAL_SERVICE_TOKEN` | `null` | Secret for X-Service-Token validation (required for internal+service-token) |
| `SessionTtlSeconds` | `CONNECT_SESSION_TTL_SECONDS` | `86400` | Session TTL in Redis (24 hours) |
| `HeartbeatTtlSeconds` | `CONNECT_HEARTBEAT_TTL_SECONDS` | `300` | Heartbeat data TTL (5 minutes) |
| `ReconnectionWindowSeconds` | `CONNECT_RECONNECTION_WINDOW_SECONDS` | `300` | Reconnection grace period (5 minutes) |
| `HttpClientTimeoutSeconds` | `CONNECT_HTTP_CLIENT_TIMEOUT_SECONDS` | `120` | HTTP client timeout for backend service calls |
| `ConnectionShutdownTimeoutSeconds` | `CONNECT_CONNECTION_SHUTDOWN_TIMEOUT_SECONDS` | `5` | WebSocket close timeout during shutdown |
| `ReconnectionWindowExtensionMinutes` | `CONNECT_RECONNECTION_WINDOW_EXTENSION_MINUTES` | `1` | Extra minutes added to reconnection window state TTL |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `IAuthClient` | Scoped | Token validation for WebSocket connection auth |
| `IMeshInvocationClient` | Singleton | HTTP request creation and service invocation |
| `IMessageBus` | Scoped | Event publishing (session lifecycle, error, permission recompile) |
| `IMessageSubscriber` | Singleton | Per-session dynamic RabbitMQ subscription management |
| `IHttpClientFactory` | Singleton | Named HTTP client for internal proxy |
| `IServiceAppMappingResolver` | Singleton | Dynamic app-id resolution for distributed routing |
| `IServiceScopeFactory` | Singleton | Creates scoped DI containers for ServiceNavigator |
| `ConnectServiceConfiguration` | Singleton | All 22 configuration properties |
| `ILogger<ConnectService>` | Singleton | Structured logging |
| `ILoggerFactory` | Singleton | Logger creation for child components |
| `IEventConsumer` | Singleton | Event consumer registration for pub/sub handlers |
| `ISessionManager` (`BannouSessionManager`) | Singleton | Distributed session state management (Redis-backed) |
| `ICapabilityManifestBuilder` (`CapabilityManifestBuilder`) | Singleton | Builds API lists and shortcut lists for capability manifests |
| `WebSocketConnectionManager` | Owned (internal) | In-memory WebSocket connection tracking, send operations, peer GUID registry |

Service lifetime is **Singleton** (unique among Bannou services). This is required because the service maintains in-memory WebSocket connection state (`ConcurrentDictionary` of active connections, session mappings, pending RPCs) that must persist across HTTP requests.

---

## API Endpoints (Implementation Notes)

### WebSocket Connection (2 endpoints)

- **ConnectWebSocket** (`GET /connect`): Accepts WebSocket upgrade. Validates token via `IAuthClient`. Generates session ID. Creates `ConnectionState` with peer GUID. Stores `ConnectionStateData` in Redis. Adds session to account index. Creates per-session RabbitMQ subscription (`CONNECT_SESSION_{sessionId}` on `bannou-client-events` exchange). Publishes `session.connected` event with roles/authorizations. Sends initial capability manifest (empty for new sessions - capabilities arrive via event from Permission). Enters message receive loop. On disconnect: initiates reconnection window (generates token, stores in Redis), unsubscribes RabbitMQ queue, publishes `session.disconnected`, removes from connection manager.
- **ConnectWebSocketPost** (`POST /connect`): POST variant of the WebSocket endpoint for clients that cannot use GET for WebSocket upgrade. Identical implementation to GET variant.

### Client Capabilities (1 endpoint)

- **ClientCapabilities** (`POST /client-capabilities`): HTTP endpoint that returns the capability manifest for a session. Uses `ICapabilityManifestBuilder.BuildApiList()` to extract POST-only, non-template endpoints from session service mappings. Includes session shortcuts as pseudo-API entries with "SHORTCUT:" prefix.

### Session Management (1 endpoint)

- **GetAccountSessions** (`POST /connect/get-account-sessions`): Admin-only endpoint. Queries the `account-sessions:{accountId}` Redis key via `ISessionManager.GetSessionsForAccountAsync()`. Returns set of active session IDs for the specified account.

### Internal Proxy (1 endpoint)

- **ProxyInternalRequest** (`POST /internal/proxy`): Stateless HTTP proxy for internal service-to-service calls through Connect. Validates session has access via local capability mappings (checks `_sessionServiceMappings`). Resolves app-id via `IServiceAppMappingResolver`. Builds HTTP request with path/query parameters. Routes through `IMeshInvocationClient`. Supports GET/POST/PUT/DELETE/PATCH. Returns raw response body and status code.

---

## Visual Aid

```
Binary Protocol Header Layout
=================================

  REQUEST MESSAGE (31 bytes + payload):
  ┌──────────┬─────────┬──────────┬──────────────────┬──────────────┬─────────────┐
  │ Flags    │ Channel │ Sequence │ Service GUID     │ Message ID   │ JSON Payload│
  │ (1 byte) │ (2)     │ (4)      │ (16)             │ (8)          │ (variable)  │
  └──────────┴─────────┴──────────┴──────────────────┴──────────────┴─────────────┘
  Byte:  0      1-2       3-6         7-22               23-30          31+

  RESPONSE MESSAGE (16 bytes + payload):
  ┌──────────┬─────────┬──────────┬──────────────┬──────────────┬─────────────┐
  │ Flags    │ Channel │ Sequence │ Message ID   │ ResponseCode │ JSON Payload│
  │ (1 byte) │ (2)     │ (4)      │ (8)          │ (1)          │ (variable)  │
  └──────────┴─────────┴──────────┴──────────────┴──────────────┴─────────────┘
  Byte:  0      1-2       3-6         7-14            15              16+


Message Flags (byte 0, bit field)
====================================

  0x00 = None      (JSON, service request, standard priority, expects response)
  0x01 = Binary    (Binary payload, not JSON)
  0x02 = Encrypted (Payload is encrypted)
  0x04 = Compressed (Payload is gzip compressed)
  0x08 = HighPriority (Skip to front of queues)
  0x10 = Event     (Fire-and-forget, no response expected)
  0x20 = Client    (Route to another WebSocket client, not a service)
  0x40 = Response  (Response to an RPC, not a new request)
  0x80 = Meta      (Request metadata about endpoint, Channel encodes MetaType)


GUID Salting Security Model
==============================

  Client A connects:
       │
       ├── GenerateServiceGuid("session-A", "account:POST:/account/get", "server-salt")
       │   = SHA256("service:account:POST:/account/get|session:session-A|salt:server-salt")
       │   = GUID abc123... (version 5 UUID)
       │
       └── Client A uses GUID abc123 to call /account/get

  Client B connects:
       │
       ├── GenerateServiceGuid("session-B", "account:POST:/account/get", "server-salt")
       │   = SHA256("service:account:POST:/account/get|session:session-B|salt:server-salt")
       │   = GUID xyz789... (DIFFERENT from Client A!)
       │
       └── Client B uses GUID xyz789 to call /account/get

  Security: Client B cannot use abc123 to impersonate Client A's session.

  UUID Version Encoding:
    Version 5 = Service endpoint GUIDs
    Version 6 = Client-to-client routing GUIDs (bidirectional, order-independent)
    Version 7 = Session shortcut GUIDs


WebSocket Connection Lifecycle
================================

  Client                    Connect Service                  Auth      Permission
    │                            │                            │            │
    ├──WebSocket Upgrade────────►│                            │            │
    │                            ├──ValidateToken────────────►│            │
    │                            │◄──Token Valid──────────────┤            │
    │                            │                            │            │
    │                            ├──Store ConnectionState (Redis)          │
    │                            ├──Subscribe RabbitMQ (CONNECT_SESSION_X) │
    │                            ├──Publish session.connected─────────────►│
    │                            │                            │            │
    │◄──Capability Manifest──────┤ (empty initially)          │            │
    │                            │                            │            │
    │                            │◄──SessionCapabilitiesEvent──────────────┤
    │                            │   (permissions dict)       │            │
    │                            ├──Generate client-salted GUIDs           │
    │◄──Updated Manifest─────────┤ (with all available APIs)  │            │
    │                            │                            │            │
    │──Binary Message (GUID)────►│                            │            │
    │                            ├──Lookup GUID in mappings                │
    │                            ├──Route to target service (via mesh)     │
    │◄──Binary Response──────────┤                            │            │
    │                            │                            │            │
    │──Disconnect────────────────│                            │            │
    │                            ├──Generate reconnection token            │
    │                            ├──Store token in Redis (5 min TTL)       │
    │                            ├──Publish session.disconnected           │
    │                            │                            │            │


Message Routing Decision Tree
================================

  Receive Binary Message
       │
       ├── Parse 31-byte header
       │
       ├── Is Response flag set? ─── Yes ──► Check _pendingRPCs[MessageId]
       │                                          │
       │                                          ├── Found → ForwardRPCResponseAsync (publish to service)
       │                                          └── Not found → discard
       │
       ├── Is Meta flag set? ─── Yes ──► Transform path to "/meta/{suffix}"
       │                                  Route as GET to companion endpoint
       │
       ├── MessageRouter.AnalyzeMessage()
       │        │
       │        ├── Invalid → Send error response
       │        └── Valid → Check rate limit
       │                       │
       │                       ├── Exceeded → Send TooManyRequests
       │                       └── Allowed → Route by type:
       │
       ├── RouteType.Service ──► RouteToServiceAsync()
       │        │                    ├── Parse endpoint key (service:METHOD:/path)
       │        │                    ├── Create scope, get IServiceNavigator
       │        │                    ├── ExecuteRawApiAsync (zero-copy byte forwarding)
       │        │                    └── Send binary response to client
       │
       ├── RouteType.SessionShortcut ──► Rewrite GUID + inject payload
       │        │                         └── Then RouteToServiceAsync()
       │
       ├── RouteType.Client ──► RouteToClientAsync()
       │        │                    ├── Check EnableClientToClientRouting
       │        │                    ├── Lookup target peer via _connectionManager
       │        │                    └── Forward message zero-copy
       │
       └── RouteType.Broadcast ──► RouteToBroadcastAsync()
                │                    ├── Reject in External mode
                │                    ├── Get all sessions except sender
                │                    └── Send to all in parallel


Reconnection Window Flow
===========================

  Client disconnects (unexpected):
       │
       ├── Generate reconnection token (GUID)
       ├── Store in Redis: reconnect:{token} -> sessionId (TTL=300s)
       ├── Update ConnectionStateData with:
       │      DisconnectedAt, ReconnectionExpiresAt, ReconnectionToken, UserRoles
       ├── Keep service mappings alive in Redis (extended TTL)
       ├── Publish session.disconnected (with reconnectionToken)
       │
       └── RabbitMQ messages queue up (session subscription still active)

  Client reconnects within window:
       │
       ├── Send reconnection token in WebSocket upgrade request
       ├── ValidateReconnectionTokenAsync → get sessionId
       ├── RestoreSessionFromReconnectionAsync:
       │      Clear DisconnectedAt, ReconnectionExpiresAt
       │      Restore active session state
       ├── Reload service mappings from Redis
       ├── Resubscribe RabbitMQ queue
       ├── Publish session.reconnected
       ├── Deliver buffered RabbitMQ messages
       │
       └── Client receives updated capability manifest


Session Shortcut Architecture
================================

  Game Session creates shortcut for player:
       │
       ├── Service publishes ShortcutPublishedEvent to CONNECT_SESSION_{sessionId}
       │   (via IClientEventPublisher)
       │
       ├── Connect receives event → HandleShortcutPublishedAsync()
       │      ├── Generate shortcut GUID: version 7, SHA256(name+session+source+salt)
       │      ├── Store in ConnectionState.SessionShortcuts with:
       │      │      RouteGuid, TargetService, TargetEndpoint, Payload (pre-bound), TTL
       │      └── Rebuild & send capability manifest (shortcut appears as API entry)
       │
       └── Client sends message with shortcut GUID:
              │
              ├── MessageRouter detects version 7 GUID → RouteType.SessionShortcut
              ├── Rewrite message: replace GUID with target service GUID
              ├── Inject pre-bound payload (ignoring client payload)
              └── Route to target service as normal


Client Event Delivery Pipeline
=================================

  ┌──────────────────────────────────────────────────────────────────┐
  │ Any Service                                                      │
  │   await _clientEventPublisher.PublishAsync(sessionId, event)     │
  └─────────────────────────────┬────────────────────────────────────┘
                                │
  ┌─────────────────────────────▼────────────────────────────────────┐
  │ RabbitMQ (bannou-client-events exchange)                         │
  │   Queue: CONNECT_SESSION_{sessionId}                             │
  └─────────────────────────────┬────────────────────────────────────┘
                                │
  ┌─────────────────────────────▼────────────────────────────────────┐
  │ Connect Service (HandleClientEventAsync)                         │
  │   ├── TryHandleInternalEventAsync?                               │
  │   │      ├── permission.session_capabilities → ProcessCapabilities│
  │   │      ├── session.shortcut_published → Add shortcut           │
  │   │      └── session.shortcut_revoked → Remove shortcut          │
  │   │                                                              │
  │   ├── ClientEventNormalizer.NormalizeEventPayload()              │
  │   │      ├── Validate event_name against whitelist               │
  │   │      └── Fix name mangling (NSwag "_" → "." normalization)   │
  │   │                                                              │
  │   └── Create BinaryMessage(Event flag) → SendMessageAsync        │
  │         ├── Client connected → deliver                           │
  │         └── Client disconnected → throw → NACK → requeue        │
  └──────────────────────────────────────────────────────────────────┘


Connection Mode Behavior Matrix
==================================

  ┌─────────────────┬───────────┬───────────┬───────────────────────────┐
  │ Behavior        │ External  │ Relayed   │ Internal                  │
  ├─────────────────┼───────────┼───────────┼───────────────────────────┤
  │ Authentication  │ JWT token │ JWT token │ service-token or none     │
  │ Broadcast       │ Blocked   │ Allowed   │ Allowed                   │
  │ Capabilities    │ Full flow │ Full flow │ Minimal (no manifest)     │
  │ Client events   │ Full      │ Full      │ Binary protocol only      │
  │ Message loop    │ Full      │ Full      │ Simplified (binary only)  │
  │ Text messages   │ Echo      │ Echo      │ Ignored                   │
  │ Reconnection    │ Full      │ Full      │ No reconnection window    │
  └─────────────────┴───────────┴───────────┴───────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. **Encrypted flag (0x02)**: The `MessageFlags.Encrypted` bit is defined but no encryption/decryption logic exists. Messages with this flag are processed as-is without any payload transformation.

2. **Compressed flag (0x04)**: The `MessageFlags.Compressed` bit is defined but no gzip decompression is performed. Compressed payloads are forwarded raw to backend services.

3. **Text message handling**: The `HandleTextMessageFallbackAsync` method simply echoes text messages back. No meaningful text protocol exists - it is a placeholder for clients not yet using binary protocol.

4. **Heartbeat sending**: `HeartbeatIntervalSeconds` is configured but no server-to-client heartbeat sending loop is implemented. The `UpdateSessionHeartbeatAsync` method exists for recording liveness but no periodic invocation mechanism is present.

5. **Rate limit window enforcement**: `RateLimitWindowMinutes` is configured but the actual sliding window implementation in `MessageRouter.CheckRateLimit` is simplified - the window tracking in `ConnectionState` may not precisely enforce the configured window.

6. **HighPriority flag (0x08)**: Defined but no priority queue or ordering is implemented. High-priority messages are routed the same as standard messages.

7. **DefaultServices/AuthenticatedServices config**: These arrays are defined in configuration but capability determination is entirely event-driven from Permission service. The config values are not used in the current implementation.

---

## Potential Extensions

1. **Connection count enforcement**: Add a semaphore or gate to `WebSocketConnectionManager` that rejects connections when `MaxConcurrentConnections` is reached, returning a 503 Service Unavailable.

2. **Server-initiated heartbeats**: Implement a background timer that sends periodic binary ping messages to clients, using `HeartbeatIntervalSeconds` configuration. Detect dead connections via missed pong responses.

3. **Payload encryption**: Implement AES-GCM encryption/decryption when `MessageFlags.Encrypted` is set. Key exchange could use per-session ephemeral keys established during WebSocket handshake.

4. **Payload compression**: Implement gzip decompression when `MessageFlags.Compressed` is set, decompress before routing to backend. Compress responses when client indicates support.

5. **Multi-instance broadcast**: Current broadcast only reaches clients connected to the same Connect instance. Extend via RabbitMQ fanout exchange to broadcast across all Connect instances.

6. **Pending RPC timeout cleanup**: The `_pendingRPCs` dictionary grows without cleanup. Add a background timer that removes expired entries (where `TimeoutAt` has passed).

7. **Graceful shutdown**: On application shutdown, send close frames to all connected clients with a "server_shutting_down" reason, wait for `ConnectionShutdownTimeoutSeconds`, then force-close remaining connections.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Singleton lifetime**: Unlike all other Bannou services (which are Scoped), Connect is Singleton because it maintains in-memory WebSocket connection state. This means injected Scoped services (like `IServiceNavigator`) must be resolved via `IServiceScopeFactory.CreateAsyncScope()` per request.

2. **ServiceRequestContext thread-static abuse**: The `ServiceRequestContext.SessionId` is set before each ServiceNavigator call and cleared in a finally block. This relies on single-threaded async execution and could be incorrect if the continuation runs on a different thread.

3. **Empty initial capability manifest**: When a new session connects, the initial capability manifest sent to the client contains zero APIs. Real capabilities arrive asynchronously via `SessionCapabilitiesEvent` from Permission service. Clients must handle a follow-up manifest update.

4. **Reconnection token as security boundary**: Reconnection tokens are plain GUIDs stored in Redis. Anyone with the token can resume the session. The token is transmitted in the disconnect event; the client must securely store it.

5. **Session shortcuts ignore client payload**: When a shortcut is triggered, the pre-bound payload from `SessionShortcutData.Payload` completely replaces whatever payload the client sent. The client's payload is discarded.

6. **Client event NACK-on-disconnect**: When `HandleClientEventAsync` finds a disconnected session, it throws an exception to trigger RabbitMQ NACK with requeue. This is intentional - messages buffer in RabbitMQ during disconnect windows and deliver on reconnect.

7. **POST-only WebSocket routing**: Only POST endpoints are exposed in capability manifests. GET and other HTTP methods are filtered out because the WebSocket binary protocol encodes all request data in the JSON body (no URL path parameters possible with GUID-based routing).

8. **Meta requests always routed as GET**: When the Meta flag is set, the request is transformed to `GET {path}/meta/{suffix}` regardless of the original endpoint's HTTP method. This relies on companion meta endpoints being registered as GET.

9. **Admin notification uses generated GUID**: The `HandleServiceErrorAsync` method generates a GUID via `GuidGenerator.GenerateServiceGuid("system", "admin-notification", _serverSalt)` for routing admin events. This is not a session-salted GUID but a fixed server-salted one.

10. **Broadcast excludes sender**: `RouteToBroadcastAsync` always excludes the sending session from recipients. There is no option to include self in broadcast.

11. **Anonymous object in HandleServiceErrorAsync**: The admin notification payload is constructed as an anonymous object, which technically violates the typed event pattern from QUALITY TENETS. This is an in-process notification, not a cross-service event.

---

## Tenet Violations (Fix Immediately)

*Fixed/Removed Violations:*
- *#1: FIXED - Removed dead configuration properties (DefaultServices, AuthenticatedServices, BinaryProtocolVersion, RabbitMqConnectionString, ConnectUrl) from connect-configuration.yaml*
- *#2: FIXED - Added configuration properties (RpcCleanupIntervalSeconds, DefaultRpcTimeoutSeconds, ConnectionCleanupIntervalSeconds, InactiveConnectionTimeoutMinutes, PendingMessageTimeoutSeconds) and updated code to use them*
- *#13: FIXED - Added warning log when ServiceName is null in RPC event handling*

1. **[IMPLEMENTATION TENETS - T23 Async Pattern] `await Task.CompletedTask` anti-pattern** - Four occurrences of `await Task.CompletedTask` used to satisfy async signatures on synchronous methods.
   - **File**: `plugins/lib-connect/ConnectService.cs`
   - **Lines**: 312 (`GetClientCapabilitiesAsync`), 1610 (`OnStartAsync`), 2201 (`ValidateSessionAsync`), 2227 (`InitializeSessionCapabilitiesAsync`)
   - **Fix**: Either remove `async` and return `Task.FromResult` (if truly sync), or refactor to perform actual async work.

2. **[IMPLEMENTATION TENETS - T23 Async Pattern] `.Wait()` synchronous blocking on Task** - The `WebSocketConnectionManager.Dispose()` method uses `.Wait()` to synchronously block on an async WebSocket close operation.
   - **File**: `plugins/lib-connect/WebSocketConnectionManager.cs`
   - **Fix**: Implement `IAsyncDisposable` with `DisposeAsync()` and use `await` instead of `.Wait()`.

3. **[IMPLEMENTATION TENETS - T9 Multi-Instance Safety] Non-thread-safe Dictionary used for concurrent access** - `ConnectionState` uses plain `Dictionary<ushort, uint>` for `ChannelSequences` and `Dictionary<ulong, PendingMessageInfo>` for `PendingMessages`.
   - **File**: `plugins/lib-connect/Protocol/ConnectionState.cs`
   - **Fix**: Replace with `ConcurrentDictionary` or add lock protection.

4. **[QUALITY TENETS - T19 XML Documentation] Missing XML documentation on public properties** - Multiple public properties lack `<summary>` documentation in `MessageRouter.cs`, `SessionModels.cs`, `ConnectionState.cs`.
   - **Fix**: Add `/// <summary>` documentation to all public properties.

5. **[QUALITY TENETS - T19 XML Documentation] Missing XML documentation on `MessagePriority` enum members** - The `Normal` and `High` enum members lack `<summary>` tags.
   - **File**: `plugins/lib-connect/Protocol/MessageRouter.cs`
   - **Fix**: Add `/// <summary>` documentation to each enum member.

6. **[IMPLEMENTATION TENETS - T5 Event-Driven Architecture] Anonymous objects returned from event handlers** - Multiple methods return anonymous objects instead of typed response classes.
   - **File**: `plugins/lib-connect/ConnectService.cs`
   - **Fix**: Define typed response classes (e.g., `EventProcessingResult`) and return those instead of anonymous objects.

7. **[IMPLEMENTATION TENETS - T5 Event-Driven Architecture] Anonymous object used for admin notification payload** - The `HandleServiceErrorAsync` method constructs an anonymous object for WebSocket payload.
   - **File**: `plugins/lib-connect/ConnectServiceEvents.cs`
   - **Fix**: Define a typed `AdminNotificationPayload` class.

8. **[IMPLEMENTATION TENETS - T5 Event-Driven Architecture] Anonymous objects in capability manifest construction** - Multiple locations construct anonymous objects for capability manifest entries.
   - **File**: `plugins/lib-connect/ConnectService.cs`
   - **Fix**: Define typed classes for `CapabilityManifestEntry`, `ShortcutManifestEntry`, `CapabilityManifest`, and `InternalModeResponse`.

9. **[QUALITY TENETS - T19 XML Documentation] Missing XML documentation on `WebSocketConnection.Metadata` purpose** - The `Metadata` property lacks documentation of known keys.
   - **File**: `plugins/lib-connect/WebSocketConnectionManager.cs`
   - **Fix**: Add `<remarks>` documenting known metadata keys and their semantics.

10. **[IMPLEMENTATION TENETS] `string.Empty` default without validation** - Several properties use `= string.Empty` as defaults without validation that they are set to meaningful values.
    - **Files**: `plugins/lib-connect/Protocol/ConnectionState.cs`, `plugins/lib-connect/SessionModels.cs`
    - **Fix**: Either make these nullable or add validation.

11. **[QUALITY TENETS - T10/T7 Error Handling] Empty catch blocks suppress errors silently** - Two empty catch blocks in `WebSocketConnectionManager` swallow exceptions without logging.
    - **File**: `plugins/lib-connect/WebSocketConnectionManager.cs`
    - **Fix**: Add at minimum a debug-level log message.

---

### Design Considerations (Requires Planning)

1. **Single-instance limitation for P2P**: Peer-to-peer routing (`RouteToClientAsync`) only works when both clients are connected to the same Connect instance. The `_connectionManager.TryGetSessionIdByPeerGuid()` lookup is in-memory only. Distributed P2P requires cross-instance peer registry.

2. **Session mappings dual storage**: Service mappings are stored both in-memory (`_sessionServiceMappings` ConcurrentDictionary) and in Redis (via `ISessionManager`). These can drift if Redis writes fail silently or if multiple instances serve the same session.

3. **No backpressure on message queue**: The `MessageQueueSize` config exists but there is no explicit backpressure mechanism. If a client is slow to consume messages, the WebSocket send buffer grows unbounded.

4. **RabbitMQ subscription lifecycle**: Per-session RabbitMQ subscriptions are created on connect and should be cleaned up on disconnect. If the Connect instance crashes, orphaned queues remain in RabbitMQ until they TTL-expire.

5. **Account session index staleness**: The `account-sessions:{accountId}` HashSet is updated on connect/disconnect but uses the session TTL. If a session crashes without cleanup, the index contains stale session IDs until Redis TTL expires.

6. **ServerSalt shared requirement**: All Connect instances MUST use the same `CONNECT_SERVER_SALT` value. If different instances use different salts, session shortcuts and GUID validation will fail across instances. This is enforced by a fail-fast check in the constructor.

7. **Instance ID non-deterministic**: `_instanceId` is generated as `MachineName-{random8chars}`. This means the same physical machine generates different instance IDs on restart, which could affect heartbeat tracking in distributed scenarios.

8. **No graceful shutdown**: When the application shuts down, WebSocket connections are abruptly terminated. There is no mechanism to send close frames or drain pending messages before exit (only `ConnectionShutdownTimeoutSeconds` for the WebSocketConnectionManager's internal cleanup).

9. **Session subsumed skips account index removal**: Lines 866-870 - when a session is "subsumed" by a new connection (same session ID), the old connection's cleanup skips RemoveSessionFromAccountAsync. This is intentional (session is still active) but means account index removal only happens once per unique session lifecycle.

10. **Disconnect event published before account index removal**: Lines 816-846 - the `session.disconnected` event is published before the session is removed from the account index. Race condition: consumers receiving the event might still see the session in GetAccountSessions.

11. **RabbitMQ subscription retained during reconnection window**: Lines 871-889 - for non-forced disconnects, the RabbitMQ subscription is NOT immediately unsubscribed. Messages queue up during the reconnection window. Only forced disconnects unsubscribe immediately.

12. **Internal mode skips capability initialization entirely**: Lines 603-652 - internal mode connections skip all service mapping, capability manifest, and RabbitMQ subscription setup. They only get peer routing capability.

13. **Connection state created before auth validation**: Lines 590-591 - a new ConnectionState is allocated before checking connection limits. On high load, rejected connections still allocate and GC these objects.
