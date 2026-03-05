# Connect Plugin Deep Dive

> **Plugin**: lib-connect
> **Schema**: schemas/connect-api.yaml
> **Version**: 2.0.0
> **Layer**: AppFoundation
> **State Store**: connect-statestore (Redis)
> **Implementation Map**: [docs/maps/CONNECT.md](../maps/CONNECT.md)

---

## Overview

WebSocket-first edge gateway (L1 AppFoundation) providing zero-copy binary message routing between game clients and backend services. Manages persistent connections with client-salted GUID generation for cross-session security, three connection modes (external, relayed, internal), session shortcuts for game-specific flows, reconnection windows, per-session RabbitMQ subscriptions for server-to-client event delivery, and multi-node broadcast relay via a WebSocket mesh between Connect instances. Internet-facing (the primary client entry point alongside Auth). Registered as Singleton (unusual for Bannou) because it maintains in-memory connection state.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-auth | Publishes `session.invalidated` events consumed by Connect to disconnect clients |
| lib-permission | Implements `ISessionActivityListener` for heartbeat-driven TTL refresh; pushes `SessionCapabilitiesEvent` via per-session RabbitMQ queue to update client capabilities |
| lib-game-session | Publishes session shortcuts and subscribes to `session.connected`/`session.disconnected`/`session.reconnected` |
| lib-actor | Subscribes to `session.disconnected` events for actor lifecycle |
| lib-matchmaking | Subscribes to `session.connected`/`session.disconnected`/`session.reconnected` events for queue management |
| lib-voice | Subscribes to `session.disconnected`/`session.reconnected` events for voice room participant cleanup |
| All services (via IClientEventPublisher) | Send server-to-client events through per-session RabbitMQ queues consumed by Connect |

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

1. **Native companion room integration (Chat/Voice)**: Connect has no runtime mechanism for integrating with Chat (L1) or Voice (L3) during session lifecycle. The `CompanionRoomMode` config property exists but is dead code. An `ICompanionRoomProvider` DI interface (in `bannou-service/Providers/`) would let Chat and Voice register companion room creation/teardown logic, discovered by Connect via `IEnumerable<ICompanionRoomProvider>` — same pattern as `IVariableProviderFactory`. Required for L0+L1+L3-only (application) deployments where L2 is absent.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/382 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Orphaned configuration: `CompanionRoomMode`**: Defined in `connect-configuration.yaml` and generated into `ConnectServiceConfiguration`, but `_configuration.CompanionRoomMode` is never referenced anywhere in service code. T21 violation — resolution requires implementing the companion room integration feature ([Issue #382](https://github.com/beyond-immersion/bannou-service/issues/382)) or removing the dead config. See Potential Extensions above for the design approach.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/382 -->

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

### Pending Design Review
- **Companion room integration** - [Issue #382](https://github.com/beyond-immersion/bannou-service/issues/382) - DI Provider pattern (`ICompanionRoomProvider`) for Chat (L1) and Voice (L3) companion rooms during session lifecycle; dead `CompanionRoomMode` config requires this or removal (2026-02-08)
- **Single-instance P2P limitation** - [Issue #346](https://github.com/beyond-immersion/bannou-service/issues/346) - Requires design decisions on cross-instance delivery mechanism, peer GUID stability, and Redis latency impact; no production consumers yet (2026-02-08)

