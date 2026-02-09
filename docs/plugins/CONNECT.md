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
| `IHttpClientFactory` | Named HTTP client ("ConnectMeshProxy") registered in plugin with configurable timeout; injected but `CreateClient` never called in service code |
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
| `heartbeat:{sessionId}` | `SessionHeartbeat` | Connection liveness tracking (instance ID, last seen, connection count) |
| `reconnect:{token}` | `string` (sessionId) | Reconnection token to session ID mapping (TTL = reconnection window) |
| `account-sessions:{accountId:N}` | Redis Set of `string` | All active session IDs for an account (atomic SADD/SREM via `ICacheableStateStore<string>`) |

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
| `IHttpClientFactory` | Singleton | Injected but `CreateClient` never called; named client registered in plugin with configurable timeout |
| `IServiceAppMappingResolver` | Singleton | Dynamic app-id resolution for distributed routing |
| `IServiceScopeFactory` | Singleton | Creates scoped DI containers for ServiceNavigator |
| `ConnectServiceConfiguration` | Singleton | All 21 configuration properties |
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

- **ProxyInternalRequest** (`POST /internal/proxy`): Stateless HTTP proxy for internal service-to-service calls through Connect. Validates session has access via connection state capability mappings (`ConnectionState.ServiceMappings`, populated by Permission service). Returns `NotFound` if session not connected, `Forbidden` if endpoint not in capability manifest. Resolves app-id via `IServiceAppMappingResolver`. Builds HTTP request with path/query parameters. Routes through `IMeshInvocationClient`. Supports GET/POST/PUT/DELETE/PATCH. Returns raw response body and status code.

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

6. **RabbitMQ queue persistence and consumer lifecycle**: Per-session RabbitMQ queues are created with `x-expires` set to `ReconnectionWindowSeconds` (default 300s). On disconnect (both forced and normal), only the consumer is cancelled — the queue itself persists and buffers messages. For normal disconnects, this enables seamless message delivery on reconnect (new consumer attaches to same queue). For forced disconnects, the queue persists until RabbitMQ's `x-expires` auto-deletes it (5 min max). Application code only manages consumers, not queues — RabbitMQ handles queue lifecycle via native TTL mechanisms. If a Connect instance crashes, orphaned queues self-clean the same way.

7. **ServerSalt shared requirement (enforced)**: All Connect instances MUST use the same `CONNECT_SERVER_SALT` value for distributed deployments. The constructor enforces this with a fail-fast check — startup aborts with `InvalidOperationException` if ServerSalt is null/empty. Different salts across instances would cause GUID validation failures and broken session shortcuts. All three GUID generation methods (`GenerateServiceGuid`, `GenerateClientGuid`, `GenerateSessionShortcutGuid`) also validate the salt parameter.

8. **Non-deterministic instance ID**: `_instanceId` is generated as `Guid.NewGuid()` on each service start. This is intentional: it tracks the runtime process, not the physical machine. When a Connect instance restarts, the new process gets a new ID, correctly distinguishing it from the crashed instance. Old heartbeats expire via TTL (5 minutes), and Redis-persisted session state enables seamless takeover by the new instance.

9. **Disconnect event published before account index removal**: In the finally block, `session.disconnected` is published before `RemoveSessionFromAccountAsync` is called. This means consumers receiving the event could theoretically still see the session in `GetAccountSessions`. In practice this is safe: no consumer of `session.disconnected` calls `GetAccountSessions`, and heartbeat-based liveness filtering in `GetSessionsForAccountAsync` filters out disconnected sessions.

10. **Internal mode minimal initialization**: Internal mode connections skip service mapping, capability manifest, RabbitMQ subscription, session state persistence, heartbeat tracking, and reconnection windows. They receive only a minimal response (sessionId + peerGuid) and enter a simplified binary-only message loop (`HandleInternalModeMessageLoopAsync`). This is intentional design for server-to-server WebSocket communication using specialized authentication (ServiceToken or NetworkTrust).

### Design Considerations (Requires Planning)

1. **Single-instance limitation for P2P**: Peer-to-peer routing (`RouteToClientAsync`) only works when both clients are connected to the same Connect instance. The `_connectionManager.TryGetSessionIdByPeerGuid()` lookup is in-memory only. Distributed P2P requires cross-instance peer registry.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/346 -->

2. ~~**Session mappings dual storage**~~: **FIXED** (2026-02-09) - Removed dead `_sessionServiceMappings` field and unused `Set/GetSessionServiceMappingsAsync` methods. `ProxyInternalRequestAsync` now validates against `ConnectionState.ServiceMappings` (the active source of truth, populated by Permission service). Removed stale `ws-mappings:*` Redis cleanup.

3. ~~**No backpressure on message queue**~~: **FIXED** (2026-02-09) - Removed dead `MessageQueueSize` config property (T21 violation). RabbitMQ per-session queues provide backpressure: when `WebSocket.SendAsync` blocks (slow client), the RabbitMQ consumer callback blocks, causing RabbitMQ to buffer messages in the session queue. No application-level queue needed.

4. ~~**Session subsumed publishes spurious disconnect event**~~: **FIXED** (2026-02-08) - Moved `RemoveConnectionIfMatch` (subsume check) before disconnect event publication. When subsumed, the entire disconnect path is skipped: no `session.disconnected` event, no account index removal, no RabbitMQ unsubscription, no reconnection window. Previously, the subsume check happened after the disconnect event, causing unnecessary state churn across all consumers (Permission, GameSession, Actor, Matchmaking).

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Pending Design Review
- **Multi-instance broadcast** - [Issue #181](https://github.com/beyond-immersion/bannou-service/issues/181) - Requires design decisions on message deduplication, acknowledgment semantics, mode enforcement, and performance trade-offs (2026-01-31)
- **Trace context propagation** - [Issue #184](https://github.com/beyond-immersion/bannou-service/issues/184) - Both proposed options (header extension, JSON injection) break protocol or zero-copy; server-side tracing sufficient for launch (2026-01-31)
- **Single-instance P2P limitation** - [Issue #346](https://github.com/beyond-immersion/bannou-service/issues/346) - Requires design decisions on cross-instance delivery mechanism, peer GUID stability, and Redis latency impact; no production consumers yet (2026-02-08)
- ~~**Session mappings dead code**~~ - [Issue #347](https://github.com/beyond-immersion/bannou-service/issues/347) - FIXED (2026-02-09): Removed dead `_sessionServiceMappings`, unused `Set/GetSessionServiceMappingsAsync`, and stale `ws-mappings:*` cleanup; fixed `ProxyInternalRequestAsync` to use `ConnectionState.ServiceMappings`
- ~~**Dead MessageQueueSize config**~~ - [Issue #348](https://github.com/beyond-immersion/bannou-service/issues/348) - FIXED (2026-02-09): Removed dead `MessageQueueSize` from schema; RabbitMQ is the backpressure mechanism

### Closed (Not Planned)
- **Encrypted flag (0x02)** - [Issue #171](https://github.com/beyond-immersion/bannou-service/issues/171) - CLOSED: violates zero-copy routing; TLS handles transport; E2E encryption is client SDK concern (2026-02-08)
- **Compressed flag (0x04)** - [Issue #172](https://github.com/beyond-immersion/bannou-service/issues/172) - CLOSED: violates zero-copy routing; WSS has permessage-deflate for transport compression (2026-02-08)
- **Heartbeat sending** - [Issue #175](https://github.com/beyond-immersion/bannou-service/issues/175) - CLOSED: already handled by KeepAliveInterval (30s RFC 6455 pings) configured in Program.cs (2026-02-08)
- **HighPriority flag (0x08)** - [Issue #178](https://github.com/beyond-immersion/bannou-service/issues/178) - CLOSED: dead code with no queue, no consumers, no use case; speculative flag (2026-02-08)

### Completed
- **Session subsumed spurious disconnect** (Bugs #4) - [Issue #349](https://github.com/beyond-immersion/bannou-service/issues/349) - FIXED (2026-02-08): Moved subsume check before disconnect event publication
