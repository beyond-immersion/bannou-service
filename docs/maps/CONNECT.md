# Connect Implementation Map

> **Plugin**: lib-connect
> **Schema**: schemas/connect-api.yaml
> **Layer**: AppFoundation
> **Deep Dive**: [docs/plugins/CONNECT.md](../plugins/CONNECT.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-connect |
| Layer | L1 AppFoundation |
| Endpoints | 10 (4 generated + 3 controller-only + 3 manual) |
| State Stores | connect-statestore (Redis) |
| Events Published | 3 (session.connected, session.disconnected, session.reconnected) |
| Events Consumed | 2 static (session.invalidated, service.error) + 3 dynamic per-session |
| Client Events | 0 (Connect IS the client event delivery mechanism) |
| Background Services | 3 |

---

## State

**Store**: `connect-statestore` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ws-session:{sessionId}` | `ConnectionStateData` | Full session state (accountId, roles, authorizations, timestamps, reconnection info) |
| `heartbeat:{sessionId}` | `SessionHeartbeat` | Liveness tracking with TTL (instanceId, lastSeen, connectionCount) |
| `reconnect:{token}` | `string` (sessionId) | Reconnection token to session ID mapping (TTL = ReconnectionWindowSeconds) |
| `account-sessions:{accountId:N}` | Redis Set of `string` | All active session IDs for an account |
| `entity-sessions:{entityType}:{entityId:N}` | Redis Set of `string` | Forward index: session IDs interested in an entity |
| `session-entities:{sessionId}` | Redis Set of `string` | Reverse index: entity bindings for a session (values: `"{entityType}:{entityId:N}"`) |
| `broadcast-registry` | Redis Sorted Set of JSON | Inter-node broadcast mesh registry (BroadcastRegistryEntry members, score = Unix timestamp) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Redis persistence via ISessionManager, IEntitySessionRegistry, InterNodeBroadcastManager |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing session lifecycle events, RPC response forwarding |
| lib-messaging (IMessageSubscriber) | L0 | Hard | Per-session dynamic RabbitMQ subscriptions for client event delivery |
| lib-mesh (IMeshInvocationClient) | L0 | Hard | HTTP request routing for ProxyInternalRequest and service message forwarding |
| lib-mesh (IServiceAppMappingResolver) | L0 | Hard | Dynamic app-id resolution for distributed routing |
| lib-mesh (IMeshInstanceIdentifier) | L0 | Hard | Process-stable instance identity for heartbeats and broadcast registry |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation |
| lib-auth (IAuthClient) | L1 | Hard | JWT validation for WebSocket connections and meta endpoint auth |
| ISessionActivityListener | L1 | DI | Dispatches lifecycle notifications to co-located listeners (connect/disconnect/reconnect/heartbeat) |

**Notes**:
- Connect is registered as **Singleton** (unique in Bannou) — maintains in-memory WebSocket connection state. Scoped services (IServiceNavigator) resolved per-request via `IServiceScopeFactory.CreateAsyncScope()`.
- lib-state not directly injected — consumed via `ISessionManager` (BannouSessionManager) and `IEntitySessionRegistry` (EntitySessionRegistry) helper singletons.
- No lib-resource integration — session state is ephemeral (Redis TTL-based).
- `ISessionActivityListener` discovered via `IEnumerable<T>` injection; currently implemented by lib-permission's `PermissionSessionActivityListener`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `session.connected` | `SessionConnectedEvent` | New WebSocket connection authenticated (external/relayed mode, after RabbitMQ subscription created) |
| `session.disconnected` | `SessionDisconnectedEvent` | WebSocket disconnect (when not subsumed by replacement connection) |
| `session.reconnected` | `SessionReconnectedEvent` | Client reconnects within grace period using reconnection token |
| `{rpc.ResponseChannel}` | `ClientRPCResponseEvent` | Client responds to bidirectional RPC; forwarded to originating service's configured topic |

---

## Events Consumed

### Static Subscriptions

| Topic | Handler | Action |
|-------|---------|--------|
| `session.invalidated` | `HandleSessionInvalidatedAsync` | Force-disconnects listed sessions when DisconnectClients=true |
| `service.error` | `HandleServiceErrorAsync` | Forwards error payload as binary event to admin WebSocket clients |

### Dynamic Per-Session Subscriptions

Each connected session gets a dedicated RabbitMQ queue (`CONNECT_SESSION_{sessionId}` on `bannou-client-events` exchange). Three internal event types are intercepted before client delivery:

| Internal Event | Handler | Action |
|----------------|---------|--------|
| `permission.session-capabilities` | `ProcessCapabilitiesAsync` | Generates client-salted GUIDs, updates service mappings, sends capability manifest |
| `session.shortcut-published` | `HandleShortcutPublishedAsync` | Adds/updates shortcut in session, rebuilds and sends capability manifest |
| `session.shortcut-revoked` | `HandleShortcutRevokedAsync` | Removes shortcut(s) from session, rebuilds and sends capability manifest |

All other events are normalized (NSwag name fix) and forwarded as binary WebSocket frames to the client.

### Custom HTTP Event Endpoints

Three manually-registered `MapPost` endpoints (in `OnStartAsync`) receive events via direct HTTP calls, not RabbitMQ subscriptions. These are same-instance only — the caller must target the Connect instance where the session is connected. See Methods section for full pseudo-code: ProcessAuthEvent, ProcessClientMessage, ProcessClientRPC.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<ConnectService>` | Structured logging |
| `ILoggerFactory` | Logger creation for child components |
| `ConnectServiceConfiguration` | Typed configuration (29 properties) |
| `IMessageBus` | Event publishing |
| `IMessageSubscriber` | Dynamic RabbitMQ subscription management |
| `IMeshInvocationClient` | HTTP service invocation |
| `IServiceAppMappingResolver` | App-id resolution for distributed routing |
| `IServiceScopeFactory` | Scoped DI for per-request IServiceNavigator |
| `IAuthClient` | JWT validation |
| `ITelemetryProvider` | Span instrumentation |
| `IMeshInstanceIdentifier` | Process-stable instance identity (used in constructor, not stored) |
| `IEventConsumer` | Event consumer registration (not stored) |
| `ISessionManager` (BannouSessionManager) | Redis-backed session state CRUD, heartbeats, reconnection, account index |
| `ICapabilityManifestBuilder` (CapabilityManifestBuilder) | API list and shortcut list construction for capability manifests |
| `IEntitySessionRegistry` (EntitySessionRegistry) | Redis-backed dual-index entity-to-session mapping |
| `InterNodeBroadcastManager` | Multi-node WebSocket broadcast relay and peer discovery |
| `WebSocketConnectionManager` | In-memory WebSocket connection tracking (created internally, not injected) |

#### Collection-Injected Providers

| Interface | Injection | Source | Role |
|-----------|-----------|--------|------|
| `IEnumerable<ISessionActivityListener>` | Collection | External (lib-permission implements `PermissionSessionActivityListener`) | Dispatches session lifecycle notifications (connect, disconnect, reconnect, heartbeat) to registered listeners |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| ProxyInternalRequest | POST /internal/proxy | generated | [] | - | - |
| GetClientCapabilities | POST /client-capabilities | generated | user | - | - |
| ConnectWebSocket | GET /connect | x-controller-only | [] | session, heartbeat, account-sessions, reconnect | session.connected, session.disconnected, session.reconnected |
| ConnectWebSocketPost | POST /connect | x-controller-only | [] | (identical to ConnectWebSocket) | (identical) |
| BroadcastWebSocket | GET /connect/broadcast | x-controller-only | [] | - | - |
| GetEndpointMeta | POST /connect/get-endpoint-meta | generated | [] | - | - |
| GetAccountSessions | POST /connect/get-account-sessions | generated | admin | - | - |
| ProcessAuthEvent | POST /events/auth-events | manual | internal | session | - |
| ProcessClientMessage | POST /events/client-messages | manual | internal | - | - |
| ProcessClientRPC | POST /events/client-rpc | manual | internal | pending-rpcs | {rpc.ResponseChannel} |

---

## Methods

### ProxyInternalRequest
POST /internal/proxy | Roles: []

```
connection = _connectionManager.GetConnection(sessionId)            -> 404 if null
endpointKey = "{targetService}:{targetEndpoint}"
IF NOT connection.HasServiceMapping(endpointKey)                    -> 403
appId = _appMappingResolver.GetAppIdForService(targetService)
// Substitute pathParameters into endpoint path; append queryParameters
// Body serialized only for Post/Put/Patch methods
CALL _meshClient.InvokeMethodWithResponseAsync(request)             -> 503 on exception
RETURN (200, InternalProxyResponse { StatusCode, Response, Headers, ExecutionTime })
```

---

### GetClientCapabilities
POST /client-capabilities | Roles: [user]

```
connection = _connectionManager.GetConnection(sessionId)            -> 404 if null
apiEntries = _manifestBuilder.BuildApiList(connection.ServiceMappings, serviceFilter)
shortcuts = _manifestBuilder.BuildShortcutList(connection.GetAllShortcuts())
// Expired shortcuts removed as side effect of BuildShortcutList
RETURN (200, ClientCapabilitiesResponse { SessionId, Capabilities, Shortcuts, Version=1 })
```

---

### ConnectWebSocket
GET /connect | Roles: [] | x-controller-only

// Authentication handled by controller via IAuthClient.ValidateTokenAsync
// Session ID from JWT SessionKey (external/relayed) or Guid.NewGuid (internal)

```
IF connectionMode == Internal
  _connectionManager.AddConnection(sessionId, webSocket, connectionState)
  // Send minimal JSON response: { sessionId, peerGuid }
  // Enter simplified binary-only message loop (no Redis, no events, no RabbitMQ)
  WHILE webSocket is open
    IF binary message -> route via HandleBinaryMessageAsync
    // No heartbeats, no session state, no reconnection
  RETURN

// --- External / Relayed mode ---

WRITE connect:heartbeat:{sessionId} <- SessionHeartbeat { InstanceId, LastSeen }
WRITE connect:ws-session:{sessionId} <- ConnectionStateData { AccountId, Roles, Authorizations, ... }
WRITE connect:account-sessions:{accountId:N} <- SET ADD sessionId
_connectionManager.AddConnection(sessionId, webSocket, connectionState)

// RabbitMQ subscription BEFORE event publish (prevents capability delivery race)
SUBSCRIBE CONNECT_SESSION_{sessionId} on bannou-client-events exchange
  queue: session.events.{sessionId} (deterministic, TTL = ReconnectionWindowSeconds)
  -> HandleClientEventAsync

IF isReconnection
  PUBLISH session.reconnected { SessionId, AccountId, Roles, Authorizations, PeerGuid }
  // Dispatch ISessionActivityListener.OnReconnectedAsync
ELSE
  PUBLISH session.connected { SessionId, AccountId, Roles, Authorizations, PeerGuid }
  // Dispatch ISessionActivityListener.OnConnectedAsync

// --- Message receive loop ---

WHILE webSocket is open
  IF heartbeat interval elapsed since last activity
    WRITE connect:heartbeat:{sessionId} <- SessionHeartbeat
    // Dispatch ISessionActivityListener.OnHeartbeatAsync

  IF binary message received
    message = BinaryMessage.Parse(buffer)

    // RPC response interception
    IF message.IsResponse AND _pendingRPCs has messageId
      PUBLISH {rpc.ResponseChannel} { ClientId, MessageId, Payload, ResponseCode }
      CONTINUE

    // Meta request interception (before route validation)
    IF message.IsMeta
      metaSuffix = MetaType from message.Channel  // info, request-schema, response-schema, schema
      metaPath = "{originalPath}/meta/{metaSuffix}"
      // Route as GET to companion meta endpoint via service mesh
      CONTINUE

    routeInfo = MessageRouter.AnalyzeMessage(message, connectionState, maxChannel)
                                                                    -> binary error if invalid
    IF MessageRouter.CheckRateLimit exceeded                        -> binary TooManyRequests

    IF routeInfo.Type == Service
      // Create scoped IServiceNavigator; set ServiceRequestContext.SessionId
      CALL navigator.ExecuteRawApiAsync(service, path, payload, method)
      // Map HTTP status to binary ResponseCode; send response to client

    ELSE IF routeInfo.Type == SessionShortcut
      // Rewrite message GUID with target service GUID
      // Inject pre-bound payload (client payload discarded)
      // Route rewritten message via service mesh

    ELSE IF routeInfo.Type == Client
      IF NOT config.EnableClientToClientRouting                     -> binary error
      targetSession = _connectionManager.TryGetSessionIdByPeerGuid(targetGuid)
                                                                    -> binary 404 if not found
      // Forward message zero-copy to target client WebSocket

    ELSE IF routeInfo.Type == Broadcast
      IF connectionMode == External                                 -> binary BroadcastNotAllowed
      FOREACH activeSession (parallel) except sender
        // Send message to each local peer
      // Fire-and-forget relay to inter-node peers via InterNodeBroadcastManager

  ELSE IF text message received
    // Return binary TextProtocolNotSupported (14) error response

// --- Disconnect handling (finally block) ---

IF connection was subsumed (RemoveConnectionIfMatch returns false)
  // New connection replaced this one — skip all disconnect logic
  RETURN

// Dispatch ISessionActivityListener.OnDisconnectedAsync
PUBLISH session.disconnected { SessionId, AccountId, Reconnectable, Reason }
WRITE connect:account-sessions:{accountId:N} <- SET REMOVE sessionId
// Clean up entity session bindings (reverse index walk)
FOREACH entityKey in READ connect:session-entities:{sessionId}
  WRITE connect:entity-sessions:{entityKey} <- SET REMOVE sessionId
DELETE connect:session-entities:{sessionId}
// Cancel RabbitMQ consumer (queue persists to buffer messages via x-expires TTL)

IF forced disconnect
  DELETE connect:ws-session:{sessionId}
  DELETE connect:heartbeat:{sessionId}
ELSE
  // Initiate reconnection window
  READ connect:ws-session:{sessionId}
  // Update with DisconnectedAt, ReconnectionExpiresAt, ReconnectionToken
  WRITE connect:ws-session:{sessionId} <- updated ConnectionStateData (extended TTL)
  WRITE connect:reconnect:{token} <- sessionId (TTL = ReconnectionWindowSeconds)
  // Send disconnect_notification to client with reconnectionToken
```

---

### ConnectWebSocketPost
POST /connect | Roles: [] | x-controller-only

POST variant for clients that cannot use GET for WebSocket upgrade. Identical implementation to ConnectWebSocket.

---

### BroadcastWebSocket
GET /connect/broadcast | Roles: [] | x-controller-only

// Internal endpoint for inter-node broadcast mesh.
// Requires instanceId query parameter and service-token authentication.

```
// Validate service token (must match config.InternalServiceToken)
// Accept WebSocket upgrade
// Register incoming peer connection in InterNodeBroadcastManager._nodeConnections
WHILE webSocket is open
  IF binary message received
    // Deliver broadcast to all local sessions
    FOREACH activeSession (parallel)
      _connectionManager.SendMessageAsync(sessionId, message)
// On disconnect: remove peer from _nodeConnections (logged as warning)
```

---

### GetEndpointMeta
POST /connect/get-endpoint-meta | Roles: []

// Auth handled manually — extracts JWT from HttpContext Authorization header.

```
// Resolve IHttpContextAccessor via scoped DI (Singleton-safe)
token = extract from Authorization header                           -> 401 if missing
CALL _authClient.ValidateTokenAsync(token)                          -> 401 if invalid
sessionKey = validation result                                      -> 401 if empty
connection = _connectionManager.GetConnection(sessionKey)           -> 401 if null

// Parse meta path components
serviceName = first path segment                                    -> 404 if unparseable
metaSuffix = segment after "/meta/"                                 -> 404 if missing
IF metaSuffix NOT in [info, request-schema, response-schema, schema]  -> 404

endpointKey = "{serviceName}:{basePath}"
IF NOT connection.HasServiceMapping(endpointKey)                    -> 403

// Create scoped IServiceNavigator; proxy GET to companion meta endpoint
CALL navigator.ExecuteRawApiAsync(serviceName, metaPath, empty, GET)  -> 503 on exception
// Deserialize MetaResponse; map to GetEndpointMetaResponse
RETURN (200, GetEndpointMetaResponse { MetaType, ServiceName, Method, Path, Data, SchemaVersion })
```

---

### GetAccountSessions
POST /connect/get-account-sessions | Roles: [admin]

```
READ connect:account-sessions:{accountId:N} -> sessionIdSet
FOREACH sessionId in sessionIdSet
  READ connect:heartbeat:{sessionId}
  IF heartbeat missing (stale entry)
    WRITE connect:account-sessions:{accountId:N} <- SET REMOVE sessionId
// Filter to valid Guids only
RETURN (200, GetAccountSessionsResponse { AccountId, SessionIds })
```

---

### Event Handlers

#### HandleSessionInvalidatedAsync
Consumes: `session.invalidated`

```
IF NOT evt.DisconnectClients
  RETURN
FOREACH sessionId in evt.SessionIds
  connection = _connectionManager.GetConnection(sessionId)
  IF connection exists
    // Mark forced_disconnect; close WebSocket gracefully
    _connectionManager.RemoveConnectionIfMatch(sessionId, webSocket)
    DELETE connect:ws-session:{sessionId}
    DELETE connect:heartbeat:{sessionId}
```

---

#### HandleServiceErrorAsync
Consumes: `service.error`

```
IF _connectionManager.GetAdminConnectionCount() == 0
  RETURN
// Build binary event message for admin delivery
guid = GuidGenerator.GenerateServiceGuid("system", "admin-notification", serverSalt)
message = BinaryMessage(Event flag, guid, serialized error payload)
_connectionManager.SendToAdminsAsync(message)
```

---

#### HandleClientEventAsync
Dynamic per-session subscription: `CONNECT_SESSION_{sessionId}`

```
// Check for internal events first (intercept before client delivery)
IF TryHandleInternalEventAsync(eventBytes, connection) == true
  RETURN  // Handled internally

// Normalize event payload (fix NSwag underscore → dot name mangling)
normalizedPayload = ClientEventNormalizer.NormalizeEventPayload(eventBytes)
message = BinaryMessage(Event flag, Guid.Empty, normalizedPayload)
_connectionManager.SendMessageAsync(sessionId, message)
// IF client not connected: throw → NACK → requeue (RabbitMQ buffers for reconnection)
```

---

### ProcessAuthEvent
POST /events/auth-events | Source: manual | Roles: internal

// Manually registered in OnStartAsync via MapPost. Same-instance only.

```
// Parse AuthEvent from request body
IF NOT _connectionManager.HasConnection(sessionId)
  RETURN  // Client not connected to this instance — no-op

IF eventType == Login
  // No-op — Permission service handles capability recompilation
IF eventType == Logout
  // No-op (disconnect commented out — Permission handles capability revocation)
IF eventType == TokenRefresh
  connection = _connectionManager.GetConnection(sessionId)
  IF connection is null OR session invalid
    CALL DisconnectAsync(sessionId, "Session invalid")
```

---

### ProcessClientMessage
POST /events/client-messages | Source: manual | Roles: internal

// Manually registered in OnStartAsync via MapPost. Same-instance only.

```
// Parse ClientMessageEvent from request body
IF NOT _connectionManager.HasConnection(clientId)
  RETURN  // Client not on this instance — silently drop

message = BinaryMessage(flags | Event, channel, serviceGuid, messageId, payload)
_connectionManager.SendMessageAsync(clientId, message)
```

---

### ProcessClientRPC
POST /events/client-rpc | Source: manual | Roles: internal

// Manually registered in OnStartAsync via MapPost. Same-instance only.

```
// Parse ClientRPCEvent from request body
IF NOT _connectionManager.HasConnection(clientId)
  RETURN  // Client not on this instance — silently drop

message = BinaryMessage(flags, channel, serviceGuid, messageId, payload)
_connectionManager.SendMessageAsync(clientId, message)

timeout = event.TimeoutSeconds > 0 ? event.TimeoutSeconds : config.DefaultRpcTimeoutSeconds
_pendingRPCs[messageId] = PendingRPCInfo { ResponseChannel, ClientId, TimeoutAt = now + timeout }
// When client responds (binary Response-flagged message with matching MessageId):
//   ForwardRPCResponseAsync publishes ClientRPCResponseEvent to {rpc.ResponseChannel}
//   via _messageBus.TryPublishAsync(responseChannel, responseEvent, ct)
```

---

## Background Services

### PendingRPCCleanupTimer
**Interval**: config.RpcCleanupIntervalSeconds (default 30s)
**Purpose**: Removes expired entries from in-memory pending RPC correlation table to prevent memory leaks from unanswered RPCs.

```
FOREACH kvp in _pendingRPCs
  IF kvp.Value.TimeoutAt < now
    _pendingRPCs.TryRemove(kvp.Key)
```

---

### InterNodeBroadcastManager.MaintenanceTimer
**Interval**: config.BroadcastHeartbeatIntervalSeconds (default 30s)
**Purpose**: Refreshes own broadcast registry heartbeat score and removes stale peer entries.

```
WRITE connect:broadcast-registry <- SORTED SET UPDATE own entry score = now
FOREACH entry WHERE score < (now - BroadcastStaleThresholdSeconds)
  DELETE connect:broadcast-registry <- SORTED SET REMOVE entry
  // Disconnect stale peer WebSocket if connected
```

---

### WebSocketConnectionManager.CleanupTimer
**Interval**: config.ConnectionCleanupIntervalSeconds (default 30s)
**Purpose**: Evicts WebSocket connections inactive longer than InactiveConnectionTimeoutMinutes.

```
FOREACH connection in _connections
  IF connection.LastActivity + InactiveConnectionTimeoutMinutes < now
    // Close WebSocket gracefully with timeout
    // Remove from connection registry
```

---

## Non-Standard Implementation Patterns

### Plugin Lifecycle (OnStartAsync)

Non-trivial startup logic — registers three manual HTTP event endpoints and initializes timers and broadcast mesh:

```
// Register manual MapPost routes for same-instance event delivery
MAP POST /events/auth-events -> ProcessAuthEventAsync
MAP POST /events/client-messages -> ProcessClientMessageEventAsync
MAP POST /events/client-rpc -> ProcessClientRPCEventAsync

// Start pending RPC cleanup timer
START Timer: _pendingRPCCleanupTimer (interval = config.RpcCleanupIntervalSeconds)

// Initialize inter-node broadcast mesh
CALL _interNodeBroadcast.InitializeAsync(ct)
  // If BroadcastInternalUrl is non-null AND mode != None:
  //   WRITE connect:broadcast-registry <- SORTED SET ADD own entry (score = now)
  //   Discover compatible peers from sorted set
  //   Connect to peers via outbound WebSocket
  //   START Timer: MaintenanceTimer (interval = config.BroadcastHeartbeatIntervalSeconds)
```

### Singleton Lifetime Override

Connect is registered as `ServiceLifetime.Singleton` (unique among Bannou services) because it maintains in-memory WebSocket connection state (`WebSocketConnectionManager`, `_pendingRPCs`, `_sessionSubscriptions`). Scoped services (`IServiceNavigator`, `IHttpContextAccessor`) are resolved via `IServiceScopeFactory.CreateAsyncScope()` per request.
