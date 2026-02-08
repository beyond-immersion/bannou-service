# Connect Plugin Deep Dive

> **Plugin**: lib-connect
> **Schema**: schemas/connect-api.yaml
> **Version**: 2.0.0
> **State Stores**: connect-statestore (Redis)

---

## Overview

WebSocket-first edge gateway (L1 AppFoundation) providing zero-copy binary message routing between game clients and backend services. Manages persistent connections with client-salted GUID generation for cross-session security, three connection modes (external, relayed, internal), session shortcuts for game-specific flows, reconnection windows, and per-session RabbitMQ subscriptions for server-to-client event delivery. Internet-facing (the primary client entry point alongside Auth). Registered as Singleton (unusual for Bannou) because it maintains in-memory connection state.

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
| lib-permission | Subscribes to `session.connected`/`session.disconnected`; pushes `SessionCapabilitiesEvent` via per-session RabbitMQ queue to update client capabilities |
| lib-game-session | Publishes session shortcuts and subscribes to `session.connected`/`session.disconnected`/`session.reconnected` |
| lib-actor | Subscribes to `session.disconnected` events for actor lifecycle |
| lib-matchmaking | Subscribes to `session.connected`/`session.disconnected`/`session.reconnected` events for queue management |
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
| `BufferSize` | `CONNECT_BUFFER_SIZE` | `65536` | Size of message receive buffers (bytes) |
| `EnableClientToClientRouting` | `CONNECT_ENABLE_CLIENT_TO_CLIENT_ROUTING` | `true` | Enable peer-to-peer message routing |
| `MaxMessagesPerMinute` | `CONNECT_MAX_MESSAGES_PER_MINUTE` | `1000` | Rate limit per client per window |
| `RateLimitWindowMinutes` | `CONNECT_RATE_LIMIT_WINDOW_MINUTES` | `1` | Rate limit window duration |
| `ServerSalt` | `CONNECT_SERVER_SALT` | `"bannou-dev-connect-salt-change-in-production"` | Server salt for GUID generation (REQUIRED, shared across instances) |
| `ConnectionMode` | `CONNECT_CONNECTION_MODE` | `External` | Connection mode enum: External/Relayed/Internal |
| `InternalAuthMode` | `CONNECT_INTERNAL_AUTH_MODE` | `ServiceToken` | Auth mode enum for internal: ServiceToken or NetworkTrust |
| `InternalServiceToken` | `CONNECT_INTERNAL_SERVICE_TOKEN` | `null` | Secret for X-Service-Token validation (required for internal+service-token) |
| `SessionTtlSeconds` | `CONNECT_SESSION_TTL_SECONDS` | `86400` | Session TTL in Redis (24 hours) |
| `HeartbeatTtlSeconds` | `CONNECT_HEARTBEAT_TTL_SECONDS` | `300` | Heartbeat data TTL (5 minutes) |
| `ReconnectionWindowSeconds` | `CONNECT_RECONNECTION_WINDOW_SECONDS` | `300` | Reconnection grace period (5 minutes) |
| `HttpClientTimeoutSeconds` | `CONNECT_HTTP_CLIENT_TIMEOUT_SECONDS` | `120` | HTTP client timeout for backend service calls |
| `RpcCleanupIntervalSeconds` | `CONNECT_RPC_CLEANUP_INTERVAL_SECONDS` | `30` | Interval between pending RPC cleanup runs |
| `DefaultRpcTimeoutSeconds` | `CONNECT_DEFAULT_RPC_TIMEOUT_SECONDS` | `30` | Default timeout for RPC calls when not specified |
| `ConnectionCleanupIntervalSeconds` | `CONNECT_CONNECTION_CLEANUP_INTERVAL_SECONDS` | `30` | Interval between connection cleanup runs |
| `InactiveConnectionTimeoutMinutes` | `CONNECT_INACTIVE_CONNECTION_TIMEOUT_MINUTES` | `30` | Timeout after which inactive connections are cleaned up |
| `PendingMessageTimeoutSeconds` | `CONNECT_PENDING_MESSAGE_TIMEOUT_SECONDS` | `30` | Timeout for pending messages awaiting acknowledgment |
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

- **ConnectWebSocket** (`GET /connect`): Accepts WebSocket upgrade. Validates token via `IAuthClient` (in controller). Generates session ID. Creates `ConnectionState` with peer GUID. Stores `ConnectionStateData` in Redis. Creates per-session RabbitMQ subscription (`CONNECT_SESSION_{sessionId}` on `bannou-client-events` exchange) with deterministic queue name (`session.events.{sessionId}`) and TTL matching reconnection window. Publishes `session.connected` event with roles/authorizations AFTER RabbitMQ subscription (prevents race condition). Adds session to account index. Does NOT send initial capability manifest - capabilities arrive asynchronously via `SessionCapabilitiesEvent` from Permission. Enters message receive loop. On disconnect: publishes `session.disconnected`, removes from account index, cancels RabbitMQ consumer (queue persists to buffer messages), then either initiates reconnection window (non-forced) or removes session from Redis (forced).
- **ConnectWebSocketPost** (`POST /connect`): POST variant of the WebSocket endpoint for clients that cannot use GET for WebSocket upgrade. Identical implementation to GET variant.

### Client Capabilities (1 endpoint)

- **ClientCapabilities** (`POST /client-capabilities`): HTTP endpoint that returns the capability manifest for a session. Uses `ICapabilityManifestBuilder.BuildApiList()` to extract POST-only, non-template endpoints from session service mappings. Includes session shortcuts as pseudo-API entries.

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
    │                            │  (no manifest sent yet)    │            │
    │                            │                            │            │
    │                            │◄──SessionCapabilitiesEvent──────────────┤
    │                            │   (permissions dict)       │            │
    │                            ├──Generate client-salted GUIDs           │
    │◄──Capability Manifest──────┤ (with all available APIs)  │            │
    │                            │                            │            │
    │──Binary Message (GUID)────►│                            │            │
    │                            ├──Lookup GUID in mappings                │
    │                            ├──Route to target service (via mesh)     │
    │◄──Binary Response──────────┤                            │            │
    │                            │                            │            │
    │──Disconnect────────────────│                            │            │
    │                            ├──Publish session.disconnected           │
    │                            ├──Remove from account index              │
    │                            ├──Cancel RabbitMQ consumer               │
    │                            ├──Generate reconnection token            │
    │                            ├──Store token in Redis (5 min TTL)       │
    │◄──disconnect_notification──┤ (with reconnection token)  │            │
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
       ├── Publish session.disconnected (Reconnectable=true)
       ├── Remove session from account index
       ├── Remove connection from manager (subsume-safe check)
       ├── Cancel RabbitMQ consumer (queue persists, buffers messages)
       ├── Generate reconnection token (GUID)
       ├── Store in Redis: reconnect:{token} -> sessionId (TTL=300s)
       ├── InitiateReconnectionWindowAsync (preserve ConnectionStateData)
       ├── Send disconnect_notification to client (with reconnectionToken)
       │
       └── Close WebSocket gracefully

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
  │ Text messages   │ Error(14) │ Error(14) │ Ignored                   │
  │ Reconnection    │ Full      │ Full      │ No reconnection window    │
  └─────────────────┴───────────┴───────────┴───────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. **Encrypted flag (0x02)**: The `MessageFlags.Encrypted` bit is defined but no encryption/decryption logic exists. Messages with this flag are processed as-is without any payload transformation.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/171 -->

2. **Compressed flag (0x04)**: The `MessageFlags.Compressed` bit is defined but no gzip decompression is performed. Compressed payloads are forwarded raw to backend services.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/172 -->

3. **Heartbeat sending**: No server-to-client WebSocket ping/pong heartbeat is implemented. `HeartbeatIntervalSeconds` controls how often the server records liveness to Redis (via `UpdateSessionHeartbeatAsync` in the message receive loop), but no periodic ping frames are sent to detect dead client connections.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/175 -->

4. **HighPriority flag (0x08)**: Defined but no priority queue or ordering is implemented. High-priority messages are routed the same as standard messages.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/178 -->

---

## Potential Extensions

1. **Multi-instance broadcast**: Current broadcast only reaches clients connected to the same Connect instance. Extend via RabbitMQ fanout exchange to broadcast across all Connect instances.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/181 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks

1. **Singleton lifetime**: Unlike all other Bannou services (which are Scoped), Connect is Singleton because it maintains in-memory WebSocket connection state. This means injected Scoped services (like `IServiceNavigator`) must be resolved via `IServiceScopeFactory.CreateAsyncScope()` per request.

2. **ServiceRequestContext thread-static**: The `ServiceRequestContext.SessionId` is set before each ServiceNavigator call and cleared in a finally block. This relies on single-threaded async execution and could be incorrect if the continuation runs on a different thread.

3. **Session shortcuts ignore client payload**: When a shortcut is triggered, the pre-bound payload from `SessionShortcutData.Payload` completely replaces whatever payload the client sent. The client's payload is discarded.

4. **Meta requests always routed as GET**: When the Meta flag is set, the request is transformed to `GET {path}/meta/{suffix}` regardless of the original endpoint's HTTP method.

5. **All text WebSocket frames rejected**: Authentication happens via the HTTP `Authorization` header during WebSocket upgrade, not via text messages. Once the WebSocket is established, ALL text frames return a `TextProtocolNotSupported` (14) binary error response. The binary protocol is required for all API messages because zero-copy routing depends on the 16-byte service GUID in the binary header. See `docs/WEBSOCKET-PROTOCOL.md` for protocol details.

6. **Orphaned RabbitMQ queues self-clean via TTL**: Per-session RabbitMQ queues are created with `x-expires` set to `ReconnectionWindowSeconds` (default 300s). If the Connect instance crashes, orphaned queues persist for up to 5 minutes without consumers before RabbitMQ automatically deletes them. This is by design: the queue must persist during the reconnection window to buffer server-to-client events for delivery on reconnect.

### Design Considerations (Requires Planning)

1. **Single-instance limitation for P2P**: Peer-to-peer routing (`RouteToClientAsync`) only works when both clients are connected to the same Connect instance. The `_connectionManager.TryGetSessionIdByPeerGuid()` lookup is in-memory only. Distributed P2P requires cross-instance peer registry.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/346 -->

2. **Session mappings dual storage**: Service mappings are stored both in-memory (`_sessionServiceMappings` ConcurrentDictionary) and in Redis (via `ISessionManager`). These can drift if Redis writes fail silently or if multiple instances serve the same session.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/347 -->

3. **No backpressure on message queue**: The `MessageQueueSize` config exists but there is no explicit backpressure mechanism. If a client is slow to consume messages, the WebSocket send buffer grows unbounded.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/348 -->

4. ~~**RabbitMQ subscription lifecycle**~~: **FIXED** (2026-02-08) - Reclassified to Intentional Quirk. RabbitMQ's native `x-expires` (set to `ReconnectionWindowSeconds`, default 300s) automatically deletes orphaned queues after 5 minutes without consumers. This is the correct design: queues persist during the reconnection window to buffer messages, then self-clean. No application-layer cleanup needed.

5. ~~**Account session index staleness**~~: **FIXED** (2026-02-08) - Replaced read-modify-write pattern with atomic Redis Set operations (`ICacheableStateStore.AddToSetAsync`/`RemoveFromSetAsync`) fixing T9 race condition. Added heartbeat-based liveness filtering in `GetSessionsForAccountAsync` — sessions without a heartbeat (5-minute TTL) are filtered out and lazily cleaned from the index.

6. ~~**ServerSalt shared requirement**~~: **FIXED** (2026-02-08) - Reclassified to Intentional Quirk. The constructor already enforces this with a fail-fast check (`InvalidOperationException` if null/empty), and all three GUID generation methods validate the salt parameter. No design planning needed.

7. **Instance ID non-deterministic**: `_instanceId` is generated as `Guid.NewGuid()`. This means the same physical machine generates different instance IDs on restart, which could affect heartbeat tracking in distributed scenarios.

8. **Session subsumed skips cleanup but disconnect event still published**: When a session is "subsumed" by a new connection (same session ID), the subsumed connection's finally block still publishes `session.disconnected` and removes from the account index (lines 857-876) before checking the subsume condition (line 888). Only RabbitMQ unsubscription and reconnection window logic are skipped for subsumed connections.

9. **Disconnect event published before account index removal**: The `session.disconnected` event is published (line 868) before `RemoveSessionFromAccountAsync` (line 875) in the finally block. Race condition: consumers receiving the event might still see the session in GetAccountSessions.

10. **RabbitMQ consumer cancelled during reconnection, queue buffers messages**: For both forced and non-forced disconnects, the RabbitMQ consumer subscription is disposed (lines 906-910 and 922-927). However, the RabbitMQ queue itself persists during the reconnection window, buffering messages. On reconnect, a new consumer is attached to the existing queue, delivering accumulated messages. Forced disconnects additionally call `RemoveSessionAsync` to clean up Redis state.

11. **Internal mode skips capability initialization entirely**: Internal mode connections (lines 633-660) skip all service mapping, capability manifest, and RabbitMQ subscription setup. They only get peer routing capability via a simplified message loop.

12. **Connection state allocated before connection limit check**: A new `ConnectionState` is allocated (line 618) before the defense-in-depth connection limit check (line 624). Auth validation happens earlier in the controller. On high load, connections that pass the controller check but fail the service-level race check still allocate and GC these objects.

13. **ServerSalt shared requirement (enforced)**: All Connect instances MUST use the same `CONNECT_SERVER_SALT` value for distributed deployments. The constructor enforces this with a fail-fast check — startup aborts with `InvalidOperationException` if ServerSalt is null/empty. Different salts across instances would cause GUID validation failures and broken session shortcuts. All three GUID generation methods (`GenerateServiceGuid`, `GenerateClientGuid`, `GenerateSessionShortcutGuid`) also validate the salt parameter. Documented in schema and enforced at runtime per multi-instance safety requirements.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Pending Design Review
- **Encrypted flag (0x02)** - [Issue #171](https://github.com/beyond-immersion/bannou-service/issues/171) - Requires design decisions on key exchange protocol, algorithm selection, and client SDK coordination (2026-01-31)
- **Compressed flag (0x04)** - [Issue #172](https://github.com/beyond-immersion/bannou-service/issues/172) - Requires design decisions on bidirectionality, algorithm flexibility, and client capability negotiation (2026-01-31)
- **Heartbeat sending** - [Issue #175](https://github.com/beyond-immersion/bannou-service/issues/175) - Requires design decisions on ping mechanism type, pong tracking, timer architecture, and client SDK coordination (2026-01-31)
- **HighPriority flag (0x08)** - [Issue #178](https://github.com/beyond-immersion/bannou-service/issues/178) - Requires design decisions on queue architecture, concurrency model changes, and whether this feature is even needed (2026-01-31)
- **Multi-instance broadcast** - [Issue #181](https://github.com/beyond-immersion/bannou-service/issues/181) - Requires design decisions on message deduplication, acknowledgment semantics, mode enforcement, and performance trade-offs (2026-01-31)
- **Single-instance P2P limitation** - [Issue #346](https://github.com/beyond-immersion/bannou-service/issues/346) - Requires design decisions on cross-instance delivery mechanism, peer GUID stability, and Redis latency impact (2026-02-08)
- **Session mappings dead code** - [Issue #347](https://github.com/beyond-immersion/bannou-service/issues/347) - `_sessionServiceMappings` is dead code (never written to), `SetSessionServiceMappingsAsync` never called, internal proxy authorization may be broken (2026-02-08)
- **Message queue backpressure** - [Issue #348](https://github.com/beyond-immersion/bannou-service/issues/348) - `MessageQueueSize` is dead config (T21 violation); decide whether to remove or implement application-level queueing (2026-02-08)

### Completed
- **RabbitMQ subscription lifecycle** - Reclassified from Design Consideration to Intentional Quirk (2026-02-08). RabbitMQ's native `x-expires` mechanism handles orphaned queue cleanup automatically. No code change needed.
- **Account session index staleness** - Fixed T9 race condition and staleness (2026-02-08). Replaced read-modify-write `IStateStore<HashSet<string>>` with atomic `ICacheableStateStore<string>.AddToSetAsync/RemoveFromSetAsync`. Added heartbeat liveness filtering in `GetSessionsForAccountAsync`.
- **ServerSalt shared requirement** - Reclassified from Design Consideration to Intentional Quirk (2026-02-08). Constructor fail-fast check and GUID generator validation already enforce this requirement. No code change needed.
