# Mesh Implementation Map

> **Plugin**: lib-mesh
> **Schema**: schemas/mesh-api.yaml
> **Layer**: Infrastructure
> **Deep Dive**: [docs/plugins/MESH.md](../plugins/MESH.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-mesh |
| Layer | L0 Infrastructure |
| Endpoints | 8 |
| State Stores | mesh-endpoints (Redis), mesh-appid-index (Redis), mesh-global-index (Redis), mesh-circuit-breaker (Redis) |
| Events Published | 6 (mesh.endpoint.registered, mesh.endpoint.deregistered, mesh.circuit.changed, mesh.endpoint.health.failed, mesh.endpoint.degraded, mesh.mappings.updated) |
| Events Consumed | 3 |
| Client Events | 0 |
| Background Services | 1 |

---

## State

**Store**: `mesh-endpoints` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{instanceId}` | `MeshEndpoint` | Individual endpoint registration with health/load metadata. TTL = `EndpointTtlSeconds` |

**Store**: `mesh-appid-index` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{appId}` | `Set<string>` | Redis set of instance IDs for a given app-id. TTL refreshed on heartbeat |

**Store**: `mesh-global-index` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `_index` | `Set<string>` | Global set of all known instance IDs. No TTL — cleaned lazily on access |

**Store**: `mesh-circuit-breaker` (Backend: Redis — via `IRedisOperations` Lua scripts, not `IStateStore<T>`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{prefix}:{appId}` | Hash (`failures`, `state`, `openedAt`) | Distributed per-appId circuit breaker state. Atomic transitions via Lua scripts. No TTL |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Redis stores for endpoints, indexes (via `IMeshStateManager`) |
| lib-state (`IRedisOperations`) | L0 | Hard | Lua script execution for atomic circuit breaker transitions |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 5 endpoint/circuit lifecycle events |
| lib-messaging (`IMessageSubscriber`) | L0 | Hard | Direct subscription to `mesh.circuit.changed` in `MeshInvocationClient` |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Registering `bannou.service-heartbeat` and `mesh.mappings.updated` handlers |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation (`NullTelemetryProvider` when disabled) |

No service-layer dependencies. Mesh is L0 Infrastructure and explicitly avoids all service clients. `MeshInvocationClient` receives `IMeshStateManager` directly (not `IMeshClient`) to prevent circular DI dependency — all generated clients depend on `IMeshInvocationClient`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `mesh.endpoint.registered` | `MeshEndpointRegisteredEvent` | RegisterEndpoint on success (explicit registration only — auto-registration from heartbeat does NOT publish) |
| `mesh.endpoint.deregistered` | `MeshEndpointDeregisteredEvent` | DeregisterEndpoint (reason: `Graceful`); MeshHealthCheckService (reason: `HealthCheckFailed`) |
| `mesh.circuit.changed` | `MeshCircuitStateChangedEvent` | DistributedCircuitBreaker on state transitions (Closed↔Open↔HalfOpen) |
| `mesh.endpoint.health.failed` | `MeshEndpointHealthCheckFailedEvent` | MeshHealthCheckService on probe failure (deduplicated per endpoint within `HealthCheckEventDeduplicationWindowSeconds`) |
| `mesh.endpoint.degraded` | `MeshEndpointDegradedEvent` | HandleServiceHeartbeatAsync on Healthy→Degraded transition (deduplicated per endpoint+reason within `DegradationEventDeduplicationWindowSeconds`) |
| `mesh.mappings.updated` | `MeshMappingsUpdatedEvent` | MeshServiceMappingReceiver after updating local IServiceAppMappingResolver (enables cross-node sync) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `bannou.service-heartbeat` | `HandleServiceHeartbeatAsync` | Updates existing endpoint metrics or auto-registers new endpoints; publishes degradation event on status transition |
| `mesh.mappings.updated` | `HandleMeshMappingsUpdatedAsync` | Atomically replaces `IServiceAppMappingResolver` mappings (version-guarded; skips self-originated events; conditional on `EnableServiceMappingSync`) |
| `mesh.circuit.changed` | `DistributedCircuitBreaker.HandleStateChangeEvent` | Updates local circuit breaker cache from other instances (via `MeshInvocationClient` direct `IMessageSubscriber` subscription, not `IEventConsumer`) |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<MeshService>` | Structured logging |
| `MeshServiceConfiguration` | 31 typed configuration properties |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration |
| `IMeshStateManager` | All Redis state operations — endpoints, indexes (Singleton) |
| `IServiceAppMappingResolver` | In-memory service→app-id routing table (Singleton) |
| `ITelemetryProvider` | Distributed tracing |
| `MeshInvocationClient` | HTTP invocation with circuit breaker, retries, endpoint caching (Singleton) |
| `DistributedCircuitBreaker` | Redis-backed per-appId circuit breaker with local cache + event sync (internal to MeshInvocationClient) |
| `MeshHealthCheckService` | Active endpoint health probing (BackgroundService) |
| `LocalMeshStateManager` | In-memory state for `UseLocalRouting=true` mode (replaces MeshStateManager) |
| `IServiceMappingReceiver` | DI interface (in `bannou-service/Providers/`) — Mesh provides default implementation (`MeshServiceMappingReceiver`) that updates local `IServiceAppMappingResolver` and broadcasts `mesh.mappings.updated` L0 events for cross-node sync. Orchestrator (L3) discovers and pushes live mapping updates into it |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetEndpoints | POST /mesh/endpoints/get | [] | - | - |
| ListEndpoints | POST /mesh/endpoints/list | [] | - | - |
| RegisterEndpoint | POST /mesh/register | [] | endpoint, appid-index, global-index | mesh.endpoint.registered |
| DeregisterEndpoint | POST /mesh/deregister | [] | endpoint, appid-index, global-index | mesh.endpoint.deregistered |
| Heartbeat | POST /mesh/heartbeat | [] | endpoint, appid-index | - |
| GetRoute | POST /mesh/route | [] | - | - |
| GetMappings | POST /mesh/mappings | [] | - | - |
| GetHealth | POST /mesh/health | [] | - | - |

---

## Methods

### GetEndpoints
POST /mesh/endpoints/get | Roles: []

```
READ mesh-endpoints via stateManager.GetEndpointsForAppIdAsync(appId, includeUnhealthy)
  // Reads appid-index set by appId, then each endpoint from endpoint store
  // Stale index entries cleaned lazily (removed from set if endpoint expired)
IF serviceName provided
  filter endpoints to those whose Services contains serviceName
RETURN (200, GetEndpointsResponse { endpoints, healthyCount, totalCount })
```

---

### ListEndpoints
POST /mesh/endpoints/list | Roles: []

```
READ mesh-endpoints via stateManager.GetAllEndpointsAsync(appIdFilter)
  // Reads global-index set "_index", then each endpoint from endpoint store
  // Stale index entries cleaned lazily; appId prefix filter applied in state manager
IF statusFilter provided
  filter endpoints to matching status
// Compute summary by grouping endpoints by EndpointStatus
RETURN (200, ListEndpointsResponse { endpoints, summary })
```

---

### RegisterEndpoint
POST /mesh/register | Roles: []

```
// Build MeshEndpoint from request (Status=Healthy, LoadPercent=0, CurrentConnections=0)
WRITE mesh-endpoints:{instanceId} <- MeshEndpoint from request (with TTL = EndpointTtlSeconds)
WRITE mesh-appid-index:{appId} <- add instanceId to set (with TTL)
WRITE mesh-global-index:_index <- add instanceId to set (no TTL)
  -> 500 if stateManager.RegisterEndpointAsync returns false
PUBLISH mesh.endpoint.registered { instanceId, appId, host, port, services }
RETURN (200, RegisterEndpointResponse { endpoint, ttlSeconds })
```

---

### DeregisterEndpoint
POST /mesh/deregister | Roles: []

```
READ mesh-endpoints:{instanceId}                                -> 404 if null
DELETE mesh-endpoints:{instanceId}
DELETE mesh-appid-index:{appId} <- remove instanceId from set
DELETE mesh-global-index:_index <- remove instanceId from set
  -> 404 if stateManager.DeregisterEndpointAsync returns false
PUBLISH mesh.endpoint.deregistered { instanceId, appId, reason: Graceful }
RETURN (200, DeregisterEndpointResponse {})
```

---

### Heartbeat
POST /mesh/heartbeat | Roles: []

```
READ mesh-endpoints:{instanceId}                                -> 404 if null
// Defaults: status=Healthy, loadPercent=0, currentConnections=0
WRITE mesh-endpoints:{instanceId} <- updated status/load/connections/issues/lastSeen (with TTL refresh)
WRITE mesh-appid-index:{appId} <- refresh set TTL
  -> 404 if stateManager.UpdateHeartbeatAsync returns false
RETURN (200, HeartbeatResponse { nextHeartbeatSeconds, ttlSeconds })
```

---

### GetRoute
POST /mesh/route | Roles: []

```
READ mesh-endpoints via stateManager.GetEndpointsForAppIdAsync(appId, includeUnhealthy: false)
  -> 404 if empty
IF serviceName provided
  filter to endpoints whose Services contains serviceName
  -> 404 if empty
// Exclude endpoints with LastSeen older than DegradationThresholdSeconds
// Exclude endpoints with LoadPercent > LoadThresholdPercent (only if at least 1 remains)
// Falls back to full healthy list if all filtering would leave 0 endpoints
// Select endpoint using algorithm (request.Algorithm or config.DefaultLoadBalancer):
//   RoundRobin: static per-appId counter (ConcurrentDictionary)
//   LeastConnections: OrderBy(CurrentConnections).First()
//   Weighted: inverse-load probability via Random.Shared
//   WeightedRoundRobin: smooth weighted (nginx-style) via static per-instance weights
//   Random: Random.Shared.Next(count)
RETURN (200, GetRouteResponse { endpoint, alternates })
```

---

### GetMappings
POST /mesh/mappings | Roles: []

```
// Reads from in-memory IServiceAppMappingResolver (not state stores)
IF serviceNameFilter provided
  filter mapping keys to those starting with filter (case-insensitive)
RETURN (200, GetMappingsResponse { mappings, defaultAppId: "bannou", version })
```

---

### GetHealth
POST /mesh/health | Roles: []

```
// Check Redis connectivity via stateManager.CheckHealthAsync
READ mesh-global-index:_index via ExistsAsync               // Redis health probe
READ mesh-endpoints via stateManager.GetAllEndpointsAsync()
// Compute summary by grouping endpoints by EndpointStatus
// Overall status:
//   Unavailable if Redis down or unavailableCount > healthyCount
//   Degraded if degradedCount > 0 or unavailableCount > 0
//   Healthy otherwise
// Compute uptime from static _serviceStartTime
IF includeEndpoints
  include full endpoint list in response
RETURN (200, MeshHealthResponse { status, summary, redisConnected, uptime, endpoints? })
```

---

## Event Handlers

### HandleServiceHeartbeatAsync
Topic: `bannou.service-heartbeat`

```
READ mesh-endpoints:{evt.ServiceId}
IF existing endpoint
  // Map InstanceHealthStatus to EndpointStatus
  WRITE mesh-endpoints:{instanceId} <- updated status/load/connections (with TTL refresh)
  WRITE mesh-appid-index:{appId} <- refresh set TTL
  IF endpoint transitioned from non-Degraded to Degraded
    // Reason: HighLoad if CpuUsage >= LoadThresholdPercent,
    //   HighConnectionCount if CurrentConnections >= MaxConnections, else MissedHeartbeat
    // Deduplicated by "{instanceId}:{reason}" within DegradationEventDeduplicationWindowSeconds
    PUBLISH mesh.endpoint.degraded { instanceId, appId, reason, loadPercent, lastHeartbeatAt }
ELSE // new endpoint — auto-register
  WRITE mesh-endpoints:{evt.ServiceId} <- new MeshEndpoint (host=appId, port=config.EndpointPort) (with TTL)
  WRITE mesh-appid-index:{appId} <- add instanceId to set (with TTL)
  WRITE mesh-global-index:_index <- add instanceId to set (no TTL)
  // NOTE: Does NOT publish mesh.endpoint.registered event
```

---

### HandleMeshMappingsUpdatedAsync
Topic: `mesh.mappings.updated` | Guard: `EnableServiceMappingSync == true`

```
// Updates in-memory IServiceAppMappingResolver only — no state store writes
// Receives cross-node broadcasts from MeshServiceMappingReceiver
IF evt.SourceInstanceId == own instance ID
  SKIP (already applied locally by DI call)
IF evt.Version > mappingResolver.CurrentVersion
  mappingResolver.ReplaceAllMappings(mappings, defaultAppId, version)
  // Empty mappings = reset all routing to default "bannou" app-id
ELSE
  // Stale version — skip update
```

---

### HandleCircuitStateChanged
Topic: `mesh.circuit.changed` | Handler: `DistributedCircuitBreaker` (via `MeshInvocationClient` direct `IMessageSubscriber` subscription)

```
// Updates local in-memory circuit breaker cache from other instance's state change
// Keeps per-instance caches eventually consistent across the cluster
```

---

## Background Services

### MeshHealthCheckService
**Interval**: `config.HealthCheckIntervalSeconds` (default: 60s)
**Guard**: Only runs when `config.HealthCheckEnabled == true` (default: false)
**Startup delay**: `config.HealthCheckStartupDelaySeconds` (default: 10s)
**Purpose**: Probes all registered endpoints via HTTP GET /health

```
READ mesh-endpoints via stateManager.GetAllEndpointsAsync()
FOREACH endpoint in endpoints
  // HTTP GET {scheme}://{host}:{port}/health (timeout: HealthCheckTimeoutSeconds)
  IF probe succeeds
    IF endpoint was not Healthy
      WRITE mesh-endpoints:{instanceId} <- status: Healthy
    // Reset consecutive failure counter
  ELSE // probe failed
    // Increment per-endpoint consecutive failure counter
    // Deduplicate within HealthCheckEventDeduplicationWindowSeconds per endpoint
    PUBLISH mesh.endpoint.health.failed { instanceId, appId, consecutiveFailures, failureThreshold, lastError }
    IF consecutiveFailures >= HealthCheckFailureThreshold AND threshold > 0
      DELETE mesh-endpoints:{instanceId}
      DELETE mesh-appid-index:{appId} <- remove instanceId from set
      DELETE mesh-global-index:_index <- remove instanceId from set
      PUBLISH mesh.endpoint.deregistered { instanceId, appId, reason: HealthCheckFailed }
    ELSE
      WRITE mesh-endpoints:{instanceId} <- status: Unavailable
```
