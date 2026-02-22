# Connect Plugin Deep Dive

> **Plugin**: lib-connect
> **Schema**: schemas/connect-api.yaml
> **Version**: 2.0.0
> **State Store**: connect-statestore (Redis)

---

## Overview

WebSocket-first edge gateway (L1 AppFoundation) providing zero-copy binary message routing between game clients and backend services. Manages persistent connections with client-salted GUID generation for cross-session security, three connection modes (external, relayed, internal), session shortcuts for game-specific flows, reconnection windows, per-session RabbitMQ subscriptions for server-to-client event delivery, and multi-node broadcast relay via a WebSocket mesh between Connect instances. Internet-facing (the primary client entry point alongside Auth). Registered as Singleton (unusual for Bannou) because it maintains in-memory connection state.

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
| `IServiceScopeFactory` | Creates scoped DI containers for per-request ServiceNavigator resolution |
| `ICapabilityManifestBuilder` | Builds capability manifest JSON from service mappings for client delivery |
| `IEntitySessionRegistry` | Entity-to-session mapping for client event push routing; cleaned up on disconnect |
| `InterNodeBroadcastManager` | WebSocket mesh for relaying broadcasts to peer Connect instances; uses Redis sorted set for instance discovery |
| `IMeshInstanceIdentifier` | Process-stable instance identity for broadcast registry entries and peer identification |

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
| `entity-sessions:{entityType}:{entityId:N}` | Redis Set of `string` | Forward index: all session IDs interested in an entity (atomic SADD/SREM via `ICacheableStateStore<string>`) |
| `session-entities:{sessionId}` | Redis Set of `string` | Reverse index: all entity bindings for a session, values are `"{entityType}:{entityId:N}"` (enables O(n) cleanup on disconnect) |
| `broadcast-registry` | Redis Sorted Set of JSON `BroadcastRegistryEntry` | Inter-node broadcast mesh registry; members are JSON `{instanceId, internalUrl, broadcastMode}`, scores are Unix timestamps for heartbeat-based stale detection |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `session.connected` | `SessionConnectedEvent` | WebSocket client successfully connects and authenticates |
| `session.disconnected` | `SessionDisconnectedEvent` | WebSocket client disconnects (graceful or unexpected) |
| `session.reconnected` | `SessionReconnectedEvent` | Client reconnects within grace period using reconnection token |
| `connect.session-events` | `SessionEvent` | Internal session lifecycle events for cross-instance communication |
| `service.error` | (via `TryPublishErrorAsync`) | Published on internal failures (state access, routing, WebSocket errors) |
| `{pendingRPC.ResponseChannel}` | `ClientRPCResponseEvent` | Forwards client RPC responses back to originating service |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `session.invalidated` | `SessionInvalidatedEvent` | `HandleSessionInvalidatedAsync` - Disconnects affected WebSocket clients when Auth invalidates sessions |
| `service.error` | `ServiceErrorEvent` | `HandleServiceErrorAsync` - Forwards error events to connected admin WebSocket clients as binary messages |
| Per-session queue (`CONNECT_SESSION_{sessionId}`) | Raw bytes | `HandleClientEventAsync` - Routes client events from RabbitMQ to WebSocket; handles internal events (capabilities, shortcuts) |
| `/events/auth-events` (HTTP) | `AuthEvent` | `ProcessAuthEventAsync` - Handles login/logout/token refresh from Auth service |
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
| `RpcCleanupIntervalSeconds` | `CONNECT_RPC_CLEANUP_INTERVAL_SECONDS` | `30` | Interval between pending RPC cleanup runs |
| `DefaultRpcTimeoutSeconds` | `CONNECT_DEFAULT_RPC_TIMEOUT_SECONDS` | `30` | Default timeout for RPC calls when not specified |
| `ConnectionCleanupIntervalSeconds` | `CONNECT_CONNECTION_CLEANUP_INTERVAL_SECONDS` | `30` | Interval between connection cleanup runs |
| `InactiveConnectionTimeoutMinutes` | `CONNECT_INACTIVE_CONNECTION_TIMEOUT_MINUTES` | `30` | Timeout after which inactive connections are cleaned up |
| `PendingMessageTimeoutSeconds` | `CONNECT_PENDING_MESSAGE_TIMEOUT_SECONDS` | `30` | Timeout for pending messages awaiting acknowledgment |
| `ConnectionShutdownTimeoutSeconds` | `CONNECT_CONNECTION_SHUTDOWN_TIMEOUT_SECONDS` | `5` | WebSocket close timeout during shutdown |
| `ReconnectionWindowExtensionMinutes` | `CONNECT_RECONNECTION_WINDOW_EXTENSION_MINUTES` | `1` | Extra minutes added to reconnection window state TTL |
| `MaxChannelNumber` | `CONNECT_MAX_CHANNEL_NUMBER` | `1000` | Maximum allowed channel number in WebSocket binary messages (rejected if exceeded) |
| `CompanionRoomMode` | `CONNECT_COMPANION_ROOM_MODE` | `Disabled` | How Connect manages companion chat rooms (Disabled, AutoJoinLazy, AutoJoin, Manual) |
| `CompressionEnabled` | `CONNECT_COMPRESSION_ENABLED` | `false` | Enable Brotli compression for outbound WebSocket payloads above the size threshold |
| `CompressionThresholdBytes` | `CONNECT_COMPRESSION_THRESHOLD_BYTES` | `1024` | Minimum payload size in bytes before compression is applied |
| `CompressionQuality` | `CONNECT_COMPRESSION_QUALITY` | `1` | Brotli compression quality level (0=none, 1=fastest, 11=best ratio) |
| `MultiNodeBroadcastMode` | `CONNECT_MULTINODE_BROADCAST_MODE` | `None` | Broadcast mesh mode: None (isolated), Send (relay outbound only), Receive (accept inbound only), Both (full mesh) |
| `BroadcastInternalUrl` | `CONNECT_BROADCAST_INTERNAL_URL` | `null` | Internal WebSocket URL for peer connections (e.g., `ws://localhost:5012/connect/broadcast`). Null disables broadcast mesh regardless of mode |
| `BroadcastHeartbeatIntervalSeconds` | `CONNECT_BROADCAST_HEARTBEAT_INTERVAL_SECONDS` | `30` | Interval between heartbeat refreshes and stale peer cleanup in the broadcast registry |
| `BroadcastStaleThresholdSeconds` | `CONNECT_BROADCAST_STALE_THRESHOLD_SECONDS` | `90` | Time after which a peer's registry entry is considered stale and removed (should be > heartbeat interval) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `IAuthClient` | Scoped | Token validation for WebSocket connection auth |
| `IMeshInvocationClient` | Singleton | HTTP request creation and service invocation |
| `IMessageBus` | Scoped | Event publishing (session lifecycle, error, permission recompile) |
| `IMessageSubscriber` | Singleton | Per-session dynamic RabbitMQ subscription management |
| `IServiceAppMappingResolver` | Singleton | Dynamic app-id resolution for distributed routing |
| `IServiceScopeFactory` | Singleton | Creates scoped DI containers for ServiceNavigator |
| `ConnectServiceConfiguration` | Singleton | All configuration properties (29 total) |
| `ITelemetryProvider` | Singleton | Distributed tracing span instrumentation for all async helper methods |
| `ILogger<ConnectService>` | Singleton | Structured logging |
| `ILoggerFactory` | Singleton | Logger creation for child components |
| `IEventConsumer` | Singleton | Event consumer registration for pub/sub handlers |
| `ISessionManager` (`BannouSessionManager`) | Singleton | Distributed session state management (Redis-backed) |
| `ICapabilityManifestBuilder` (`CapabilityManifestBuilder`) | Singleton | Builds API lists and shortcut lists for capability manifests |
| `IEntitySessionRegistry` (`EntitySessionRegistry`) | Singleton | Redis-backed dual-index entity-to-session mapping for client event push routing |
| `InterNodeBroadcastManager` | Singleton | WebSocket mesh for relaying broadcasts to/from peer Connect instances; Redis sorted set for discovery, heartbeat timer for liveness |
| `IMeshInstanceIdentifier` | Singleton | Process-stable instance identity for session heartbeats and broadcast registry |
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

### Inter-Node Broadcast (1 endpoint)

- **BroadcastWebSocket** (`GET /connect/broadcast`): Internal WebSocket endpoint for the multi-node broadcast mesh. Other Connect instances connect here to relay broadcast messages. Requires `instanceId` query parameter and service-token authentication (same as Internal connection mode). Not client-facing — used exclusively for inter-node communication. `x-controller-only: true`.

### Meta Proxy (1 endpoint)

- **GetEndpointMeta** (`POST /connect/get-endpoint-meta`): Permission-gated proxy for endpoint metadata. Accepts a full meta endpoint path (e.g., `/account/get/meta/info`), validates the caller's JWT, looks up their active WebSocket session via the JWT's sessionKey, checks the session's capability mappings for the underlying endpoint, and proxies the internal meta GET request if authorized. Returns the meta endpoint response directly.

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
       ├── Clean up entity session bindings (IEntitySessionRegistry)
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


Multi-Node Broadcast Mesh
============================

  Instance A (Both)          Instance B (Both)          Instance C (Receive)
  ┌──────────────────┐       ┌──────────────────┐       ┌──────────────────┐
  │ Connect Service   │◄─ws─►│ Connect Service   │◄─ws──│ Connect Service   │
  │ InterNodeBroadcast│       │ InterNodeBroadcast│       │ InterNodeBroadcast│
  │ Manager           │       │ Manager           │       │ Manager           │
  └──────────────────┘       └──────────────────┘       └──────────────────┘
        │                          │                          │
        ▼                          ▼                          ▼
  broadcast-registry (Redis Sorted Set)
  ┌──────────────────────────────────────────────────────┐
  │ {instanceA, url, Both}  score: 1740000000            │
  │ {instanceB, url, Both}  score: 1740000000            │
  │ {instanceC, url, Recv}  score: 1740000000            │
  └──────────────────────────────────────────────────────┘

  Compatibility matrix (who connects to whom):
  ┌──────────────────┬──────┬──────┬──────┬──────┐
  │ My Mode \ Peer   │ None │ Send │ Recv │ Both │
  ├──────────────────┼──────┼──────┼──────┼──────┤
  │ None             │  ✗   │  ✗   │  ✗   │  ✗   │
  │ Send             │  ✗   │  ✗   │  ✓   │  ✓   │
  │ Receive          │  ✗   │  ✓   │  ✗   │  ✓   │
  │ Both             │  ✗   │  ✓   │  ✓   │  ✓   │
  └──────────────────┴──────┴──────┴──────┴──────┘

  Broadcast relay flow:
  Client ──broadcast──► Instance A ──relay──► Instance B ──deliver──► B's clients
                              │
                              └──────relay──► Instance C ──deliver──► C's clients


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

No outstanding stubs. All previously-tracked items (encrypted flag, compressed flag, heartbeat sending, high-priority flag) were resolved and removed.

---

## Potential Extensions

No outstanding extensions. Multi-instance broadcast was implemented (2026-02-22) via `InterNodeBroadcastManager`.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Orphaned configuration: `CompanionRoomMode`**: Defined in `connect-configuration.yaml` and generated into `ConnectServiceConfiguration`, but `_configuration.CompanionRoomMode` is never referenced anywhere in service code. T21 violation — dead config should be wired up in service or removed from schema.

2. ~~**Orphaned configuration: `MaxChannelNumber`**~~: **FIXED** (2026-02-22) - Wired `_configuration.MaxChannelNumber` into `MessageRouter.AnalyzeMessage()` call, replacing the hardcoded `1000` default.

### Intentional Quirks

1. **Singleton lifetime**: Unlike all other Bannou services (which are Scoped), Connect is Singleton because it maintains in-memory WebSocket connection state. This means injected Scoped services (like `IServiceNavigator`) must be resolved via `IServiceScopeFactory.CreateAsyncScope()` per request.

2. **ServiceRequestContext thread-static**: The `ServiceRequestContext.SessionId` is set before each ServiceNavigator call and cleared in a finally block. This relies on single-threaded async execution and could be incorrect if the continuation runs on a different thread.

3. **Session shortcuts ignore client payload**: When a shortcut is triggered, the pre-bound payload from `SessionShortcutData.Payload` completely replaces whatever payload the client sent. The client's payload is discarded.

4. **Meta requests always routed as GET**: When the Meta flag is set, the request is transformed to `GET {path}/meta/{suffix}` regardless of the original endpoint's HTTP method.

5. **All text WebSocket frames rejected**: Authentication happens via the HTTP `Authorization` header during WebSocket upgrade, not via text messages. Once the WebSocket is established, ALL text frames return a `TextProtocolNotSupported` (14) binary error response. The binary protocol is required for all API messages because zero-copy routing depends on the 16-byte service GUID in the binary header. See `docs/WEBSOCKET-PROTOCOL.md` for protocol details.

6. **RabbitMQ queue persistence and consumer lifecycle**: Per-session RabbitMQ queues are created with `x-expires` set to `ReconnectionWindowSeconds` (default 300s). On disconnect (both forced and normal), only the consumer is cancelled — the queue itself persists and buffers messages. For normal disconnects, this enables seamless message delivery on reconnect (new consumer attaches to same queue). For forced disconnects, the queue persists until RabbitMQ's `x-expires` auto-deletes it (5 min max). Application code only manages consumers, not queues — RabbitMQ handles queue lifecycle via native TTL mechanisms. If a Connect instance crashes, orphaned queues self-clean the same way.

7. **ServerSalt shared requirement (enforced)**: All Connect instances MUST use the same `CONNECT_SERVER_SALT` value for distributed deployments. The constructor enforces this with a fail-fast check — startup aborts with `InvalidOperationException` if ServerSalt is null/empty. Different salts across instances would cause GUID validation failures and broken session shortcuts. All three GUID generation methods (`GenerateServiceGuid`, `GenerateClientGuid`, `GenerateSessionShortcutGuid`) also validate the salt parameter.

8. **Disconnect event published before account index removal**: In the finally block, `session.disconnected` is published before `RemoveSessionFromAccountAsync` is called. This means consumers receiving the event could theoretically still see the session in `GetAccountSessions`. In practice this is safe: no consumer of `session.disconnected` calls `GetAccountSessions`, and heartbeat-based liveness filtering in `GetSessionsForAccountAsync` filters out disconnected sessions.

9. **Internal mode minimal initialization**: Internal mode connections skip service mapping, capability manifest, RabbitMQ subscription, session state persistence, heartbeat tracking, and reconnection windows. They receive only a minimal response (sessionId + peerGuid) and enter a simplified binary-only message loop (`HandleInternalModeMessageLoopAsync`). This is intentional design for server-to-server WebSocket communication using specialized authentication (ServiceToken or NetworkTrust).

10. **Subsume-safe disconnect**: When a new connection replaces an existing one for the same session (subsume), the old connection's finally block uses `RemoveConnectionIfMatch` (WebSocket reference equality) to detect the subsume. If subsumed, the entire disconnect path is skipped: no `session.disconnected` event, no account index removal, no RabbitMQ unsubscription, no reconnection window. This prevents state churn across all consumers (Permission, GameSession, Actor, Matchmaking) that would otherwise rebuild on a false disconnect/reconnect cycle.

11. **Broadcast mesh is fire-and-forget**: `RelayBroadcastAsync` sends to all connected peers without waiting for delivery confirmation. If a peer WebSocket is in a broken state, the send fails silently (logged as warning) and the connection is removed from `_nodeConnections`. No message retry or guaranteed delivery — the same semantics as local broadcast delivery.

12. **New instances establish connections, not old ones**: When a new Connect instance starts, it discovers existing peers from the Redis sorted set and initiates WebSocket connections to compatible peers. Existing instances never proactively connect to new instances — they accept incoming connections via the `/connect/broadcast` endpoint. This avoids the need for background workers or polling for new peers.

13. **BroadcastInternalUrl null is a hard disable**: If `BroadcastInternalUrl` is null or empty, the broadcast manager is completely inactive regardless of `MultiNodeBroadcastMode`. No Redis registration, no peer discovery, no heartbeat timer. This allows nodes to be isolated without changing the mode enum.

14. **Broadcast relay mode gating**: A node set to `Receive` will accept and deliver incoming broadcasts from peers but will NOT relay its own broadcasts outward (even if peers connect to it). Conversely, a `Send` node relays outward but discards any broadcasts received from peers. The WebSocket connection is bidirectional, but the `BroadcastMode` gates which direction each node actually uses.

### Design Considerations (Requires Planning)

1. **Single-instance limitation for P2P**: Peer-to-peer routing (`RouteToClientAsync`) only works when both clients are connected to the same Connect instance. The `_connectionManager.TryGetSessionIdByPeerGuid()` lookup is in-memory only. Distributed P2P requires cross-instance peer registry.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/346 -->


---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Completed
- **2026-02-22**: L3 hardening audit — Schema: consolidated connect-shortcuts.yaml into standard files, extracted ConnectionMode/InternalAuthMode/BroadcastMode/CompanionRoomMode enums to connect-api.yaml, added format:uuid to all UUID fields, added validation constraints to all config integers, extracted inline HTTP method enum, fixed additionalProperties:true T29 descriptions. Code: fixed T9 thread safety (ConcurrentDictionary/ConcurrentQueue in ConnectionState), fixed T26 Guid.Empty sentinels, fixed T5 anonymous objects (typed event/protocol payloads), wired MaxChannelNumber config (T21), fixed T7 bare catch blocks in NetworkByteOrder, fixed T23 .Wait() in WebSocketConnectionManager.Dispose, fixed T24 IDisposable/IAsyncDisposable + ClientWebSocket leak, deduplicated capability manifest construction (removed dead code), added T30 telemetry spans to ConnectService/BannouSessionManager/EntitySessionRegistry/ConnectServiceEvents, added XML docs to MessageRouteInfo/WebSocketConnection properties.

### Pending Design Review
- **Single-instance P2P limitation** - [Issue #346](https://github.com/beyond-immersion/bannou-service/issues/346) - Requires design decisions on cross-instance delivery mechanism, peer GUID stability, and Redis latency impact; no production consumers yet (2026-02-08)

