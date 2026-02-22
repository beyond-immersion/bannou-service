# Mesh Plugin Deep Dive

> **Plugin**: lib-mesh
> **Schema**: schemas/mesh-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: mesh-endpoints, mesh-appid-index, mesh-global-index, mesh-circuit-breaker (all Redis)

---

## Overview

Native service mesh (L0 Infrastructure) providing direct in-process service-to-service calls with YARP-based HTTP routing and Redis-backed service discovery. Provides endpoint registration with TTL-based health tracking, configurable load balancing, a distributed per-appId circuit breaker, and retry logic with exponential backoff. Includes proactive health checking with automatic deregistration and event-driven auto-registration from Orchestrator heartbeats for zero-configuration discovery.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for endpoint registry, app-id indexes, global index, and circuit breaker state |
| lib-state (`IRedisOperations`) | Lua script execution for atomic circuit breaker state transitions |
| lib-messaging (`IMessageBus`) | Publishing endpoint lifecycle events, circuit state change events, and error events |
| lib-messaging (`IMessageSubscriber`) | Subscribing to `mesh.circuit.changed` for cross-instance circuit breaker sync |
| lib-messaging (`IEventConsumer`) | Subscribing to `bannou.service-heartbeat` and `bannou.full-service-mappings` |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| All services (via generated clients) | Every NSwag-generated client uses `IMeshInvocationClient` for service-to-service HTTP calls |
| lib-connect | Routes WebSocket messages to backend services via mesh invocation |
| lib-orchestrator | Manages service routing tables; publishes `FullServiceMappingsEvent` consumed by mesh |

---

## State Storage

**Stores**: 4 Redis stores (3 via `IStateStoreFactory`, 1 via `IRedisOperations` Lua scripts)

| Store | Key Pattern | Data Type | Purpose |
|-------|-------------|-----------|---------|
| `mesh-endpoints` | `{instanceId}` (GUID string) | `MeshEndpoint` | Individual endpoint registration with health/load metadata |
| `mesh-appid-index` | `{appId}` (set of instance IDs) | `Set<string>` | App-ID to instance-ID mapping for routing queries |
| `mesh-global-index` | `_index` (set of instance IDs) | `Set<string>` | Global endpoint index for discovery (avoids KEYS/SCAN) |
| `mesh-circuit-breaker` | `mesh:cb:{appId}` (hash) | Hash: failures, state, openedAt | Distributed circuit breaker state (key prefix from state-stores.yaml; operations via `IRedisOperations` Lua scripts, not state store factory) |

**Key Patterns**:
- Endpoint data keyed by instance ID (GUID)
- App-ID index lists all instance IDs for a given app-id (with TTL refresh on heartbeat)
- Global index (`_index` key) tracks all known instance IDs (no TTL - cleaned lazily on access)
- Circuit breaker key prefix sourced from `StateStoreDefinitions.MeshCircuitBreaker` (schema-first), operations via `IRedisOperations` Lua scripts for atomic state transitions (no TTL - cleared on success)

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `mesh.endpoint.registered` | `MeshEndpointRegisteredEvent` | New endpoint registered (explicit or auto-discovered from heartbeat) |
| `mesh.endpoint.deregistered` | `MeshEndpointDeregisteredEvent` | Endpoint removed (graceful shutdown or health check failure) |
| `mesh.circuit.changed` | `MeshCircuitStateChangedEvent` | Circuit breaker state changes (Closed→Open, Open→HalfOpen, HalfOpen→Closed, etc.) |
| `mesh.endpoint.health.failed` | `MeshEndpointHealthCheckFailedEvent` | Health check probe failed (before deregistration threshold - enables proactive monitoring) |
| `mesh.endpoint.degraded` | `MeshEndpointDegradedEvent` | Endpoint transitioned to Degraded status (from Healthy; reason: MissedHeartbeat, HighLoad, HighConnectionCount) |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `bannou.service-heartbeat` | `ServiceHeartbeatEvent` | `HandleServiceHeartbeatAsync` - Updates existing endpoint metrics or auto-registers new endpoints |
| `bannou.full-service-mappings` | `FullServiceMappingsEvent` | `HandleServiceMappingsAsync` - Atomically updates `IServiceAppMappingResolver` for all generated clients (configurable via `EnableServiceMappingSync`) |
| `mesh.circuit.changed` | `MeshCircuitStateChangedEvent` | `HandleCircuitStateChanged` - Updates local circuit breaker cache from other instances |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `InstanceId` | `MESH_INSTANCE_ID` | `null` | Explicit mesh node identity override. Falls back to `--force-service-id` CLI, then random GUID |
| `UseLocalRouting` | `MESH_USE_LOCAL_ROUTING` | `false` | Bypass Redis, route all calls to local instance (testing only) |
| `EndpointHost` | `MESH_ENDPOINT_HOST` | `null` | Hostname for endpoint registration (defaults to app-id) |
| `EndpointPort` | `MESH_ENDPOINT_PORT` | `80` | Port for endpoint registration |
| `DefaultMaxConnections` | `MESH_DEFAULT_MAX_CONNECTIONS` | `1000` | Default max connections for auto-registered endpoints when heartbeat does not provide capacity info |
| `HeartbeatIntervalSeconds` | `MESH_HEARTBEAT_INTERVAL_SECONDS` | `30` | Recommended interval between heartbeats |
| `EndpointTtlSeconds` | `MESH_ENDPOINT_TTL_SECONDS` | `90` | TTL for endpoint registration (>2x heartbeat) |
| `DegradationThresholdSeconds` | `MESH_DEGRADATION_THRESHOLD_SECONDS` | `60` | Time without heartbeat before marking degraded |
| `DefaultLoadBalancer` | `MESH_DEFAULT_LOAD_BALANCER` | `RoundRobin` | Load balancing algorithm (enum: RoundRobin, LeastConnections, Weighted, WeightedRoundRobin, Random) |
| `LoadThresholdPercent` | `MESH_LOAD_THRESHOLD_PERCENT` | `80` | Load % above which endpoint is considered high-load |
| `EnableServiceMappingSync` | `MESH_ENABLE_SERVICE_MAPPING_SYNC` | `true` | Subscribe to FullServiceMappingsEvent for routing updates |
| `HealthCheckEnabled` | `MESH_HEALTH_CHECK_ENABLED` | `false` | Enable active health check probing |
| `HealthCheckIntervalSeconds` | `MESH_HEALTH_CHECK_INTERVAL_SECONDS` | `60` | Interval between health probes |
| `HealthCheckTimeoutSeconds` | `MESH_HEALTH_CHECK_TIMEOUT_SECONDS` | `5` | Timeout for health check requests |
| `HealthCheckFailureThreshold` | `MESH_HEALTH_CHECK_FAILURE_THRESHOLD` | `3` | Consecutive failures before deregistering endpoint (0 disables) |
| `HealthCheckStartupDelaySeconds` | `MESH_HEALTH_CHECK_STARTUP_DELAY_SECONDS` | `10` | Delay before first health probe |
| `HealthCheckEventDeduplicationWindowSeconds` | `MESH_HEALTH_CHECK_EVENT_DEDUPLICATION_WINDOW_SECONDS` | `60` | Dedup window for health check failure events per endpoint |
| `DegradationEventDeduplicationWindowSeconds` | `MESH_DEGRADATION_EVENT_DEDUPLICATION_WINDOW_SECONDS` | `60` | Dedup window for degradation events per endpoint+reason |
| `CircuitBreakerEnabled` | `MESH_CIRCUIT_BREAKER_ENABLED` | `true` | Enable per-appId circuit breaker |
| `CircuitBreakerThreshold` | `MESH_CIRCUIT_BREAKER_THRESHOLD` | `5` | Consecutive failures before opening circuit |
| `CircuitBreakerResetSeconds` | `MESH_CIRCUIT_BREAKER_RESET_SECONDS` | `30` | Seconds before half-open probe attempt |
| `MaxRetries` | `MESH_MAX_RETRIES` | `3` | Maximum retry attempts for failed calls |
| `RetryDelayMilliseconds` | `MESH_RETRY_DELAY_MILLISECONDS` | `100` | Initial retry delay (doubles each retry) |
| `PooledConnectionLifetimeMinutes` | `MESH_POOLED_CONNECTION_LIFETIME_MINUTES` | `2` | HTTP connection pool lifetime |
| `ConnectTimeoutSeconds` | `MESH_CONNECT_TIMEOUT_SECONDS` | `10` | TCP connection timeout |
| `RequestTimeoutSeconds` | `MESH_REQUEST_TIMEOUT_SECONDS` | `30` | Per-request timeout for complete request/response cycle |
| `EndpointCacheTtlSeconds` | `MESH_ENDPOINT_CACHE_TTL_SECONDS` | `5` | TTL for cached endpoint resolution |
| `EndpointCacheMaxSize` | `MESH_ENDPOINT_CACHE_MAX_SIZE` | `0` | Max app-ids in endpoint cache (0 = unlimited) |
| `LoadBalancingStateMaxAppIds` | `MESH_LOAD_BALANCING_STATE_MAX_APP_IDS` | `0` | Max app-ids in load balancing state (0 = unlimited) |
| `MaxTopEndpointsReturned` | `MESH_MAX_TOP_ENDPOINTS_RETURNED` | `2` | Max alternates in route response |
| `MaxServiceMappingsDisplayed` | `MESH_MAX_SERVICE_MAPPINGS_DISPLAYED` | `10` | Max mappings in diagnostic logs |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<MeshService>` | Scoped | Structured logging |
| `MeshServiceConfiguration` | Singleton | All 31 config properties above |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IMessageSubscriber` | Scoped | Circuit state change subscription in MeshInvocationClient |
| `IEventConsumer` | Scoped | Heartbeat and mapping event subscription (via generated code) |
| `IMeshStateManager` | Singleton | Redis state via lib-state (3 stores + raw Redis) |
| `IServiceAppMappingResolver` | Singleton | Shared service→app-id routing (used by all generated clients) |
| `ITelemetryProvider` | Singleton | Distributed tracing and metrics instrumentation (`NullTelemetryProvider` when telemetry disabled) |
| `IMeshInstanceIdentifier` | Singleton | Canonical mesh node identity (priority: `MESH_INSTANCE_ID` env > `--force-service-id` CLI > random GUID) |
| `MeshInvocationClient` | Singleton | HTTP invocation with distributed circuit breaker, retries, caching |
| `DistributedCircuitBreaker` | Internal (via MeshInvocationClient) | Redis-backed circuit breaker with local cache + event sync |
| `MeshHealthCheckService` | Hosted (BackgroundService) | Active endpoint health probing |
| `LocalMeshStateManager` | Singleton | In-memory state for `UseLocalRouting=true` mode |

Service lifetime is **Scoped** (per-request) for MeshService itself. Infrastructure components (state manager, invocation client) are Singleton.

---

## API Endpoints (Implementation Notes)

### Service Discovery (2 endpoints)

- **GetEndpoints** (`/mesh/endpoints/get`): Returns endpoints for an app-id. Filters by health status (default: healthy only) and optional service name. Returns healthy/total counts.
- **ListEndpoints** (`/mesh/endpoints/list`): Admin-level listing of all endpoints. Groups by status for summary (healthy, degraded, unavailable). Supports app-id prefix filter and status filter (filters in-memory after fetching from Redis).

### Registration (3 endpoints)

- **Register** (`/mesh/register`): Announces instance availability. Instance ID is required in request. Stores with configurable TTL. Publishes `mesh.endpoint.registered`.
- **Deregister** (`/mesh/deregister`): Graceful shutdown removal. Looks up endpoint first (for app-id), removes from store, publishes `mesh.endpoint.deregistered` with reason=Graceful.
- **Heartbeat** (`/mesh/heartbeat`): Refreshes TTL and updates metrics (status, load%, connections, issues). Issues are stored on the endpoint and visible in endpoint queries. Returns `NextHeartbeatSeconds` and `TtlSeconds` for client scheduling.

### Routing (2 endpoints)

- **GetRoute** (`/mesh/route`): Core routing logic. Filters: health (degradation threshold), load (threshold %), service name. Falls back to ALL endpoints if filtering empties the list. Applies load balancing algorithm. Returns primary endpoint + alternates.
- **GetMappings** (`/mesh/mappings`): Returns service→app-id routing table from `IServiceAppMappingResolver`. Supports service name prefix filter. Version-tracked for stale detection.

### Diagnostics (1 endpoint)

- **GetHealth** (`/mesh/health`): Overall mesh health status. Checks Redis connectivity via state manager. Aggregates endpoint counts by status. Calculates uptime. Optionally includes full endpoint list.

---

## Visual Aid

```
Service Invocation Flow (MeshInvocationClient)
================================================

  GeneratedClient.SomeMethodAsync(request)
       │
       ▼
  MeshInvocationClient.InvokeMethodAsync<TReq, TResp>(appId, method, request)
       │
       ├──► Circuit Breaker Check
       │    state == Open? → throw CircuitBreakerOpen
       │
       ├──► Resolve Endpoint (with cache)
       │    ├── Cache hit? → use cached endpoint
       │    └── Cache miss? → _stateManager.GetEndpointsForAppIdAsync()
       │                       │
       │                       └──► Round-robin selection → cache result
       │
       ├──► Build Target URI: http://{host}:{port}/{method}
       │
       └──► Send HTTP Request
            │
            ├── Success (2xx, 4xx) → RecordSuccess → return response
            │
            └── Transient Error (408, 429, 5xx) or HttpRequestException
                 │
                 ├── Retries remaining? → invalidate cache, exponential backoff, retry
                 │
                 └── All attempts exhausted → RecordFailure → throw


Load Balancing Algorithms
==========================

  SelectEndpoint(endpoints, appId, algorithm)
       │
       ├── RoundRobin  → static ConcurrentDictionary<appId, counter>
       │                  counter = (current + 1) % endpoints.Count
       │
       ├── LeastConnections → OrderBy(CurrentConnections).First()
       │
       ├── Weighted → Inverse load probability
       │              weight = max(100 - LoadPercent, 1)
       │              weighted random selection
       │
       ├── WeightedRoundRobin → Smooth weighted round-robin (nginx-style)
       │                         effective_weight = max(100 - LoadPercent, 1)
       │                         current_weight += effective_weight
       │                         select highest current_weight
       │                         selected.current_weight -= total_effective_weight
       │
       └── Random → Random.Shared.Next(endpoints.Count)


Event-Driven Auto-Registration
================================

  ServiceHeartbeatEvent (from RabbitMQ)
       │
       ▼
  HandleServiceHeartbeatAsync
       │
       ├── Existing endpoint? → UpdateHeartbeatAsync (status, load, connections)
       │
       └── New instance? → Auto-register endpoint
                           Host = appId (mesh-style routing)
                           Port = config.EndpointPort
                           Services = evt.Services
```

---

## Stubs & Unimplemented Features

*No stubs remaining - all features are implemented.*

---

## Potential Extensions

*No extensions identified. Graceful draining was previously listed here but deemed unnecessary: Orchestrator's two-level routing model (mapping resolver + endpoint resolution) handles managed deployments by changing app-id mappings before stopping old nodes, so new requests never reach the draining node. Crash scenarios can't be helped by draining since the node is already dead.*

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified.*

### Intentional Quirks (Documented Behavior)

1. **`InvokeRawAsync` bypasses circuit breaker**: The raw API invocation path (`InvokeRawAsync`) intentionally skips circuit breaker checks and does not record success/failure to the breaker. This is by design because raw API execution targets services that may be optional or disabled — failures against absent services should not trip the circuit.

### Operational Notes

These are standard behaviors worth understanding for operations and debugging:

1. **Two-level routing model**: Mesh routing operates in two stages. First, `IServiceAppMappingResolver` maps a **service name** to an **app-id** (e.g., `"auth"` → `"bannou-auth-node1"`). This mapping is populated by Orchestrator's `FullServiceMappingsEvent` broadcasts after deployments and topology changes. Second, Mesh resolves the **app-id** to a specific **endpoint** using load balancing across all healthy instances registered under that app-id. Node affinity is handled at the first level (Orchestrator assigns per-node app-ids like `bannou-{service}-{nodeName}`), so Mesh's load balancing at the second level is always stateless. In development, all services map to the default `"bannou"` app-id (single instance); in production, Orchestrator controls which node each service routes to by publishing different mappings.

2. **Resilient routing fallback**: If health/load filtering eliminates all endpoints, `GetRoute` falls back to the full unfiltered list. Prefers degraded routing over total failure.

3. **Circuit breaker eventual consistency**: State syncs across instances via Redis + RabbitMQ events within milliseconds. Brief disagreement during propagation is expected and harmless.

4. **Global index lazy cleanup**: The `mesh-global-index` store cleans stale entries on access rather than via TTL. Endpoints themselves have TTL, so this is an optimization choice, not a bug.

5. **Empty mappings = reset**: `FullServiceMappingsEvent` with empty mappings resets all routing to default ("bannou"). This is intentional for container teardown.

### Design Considerations (Requires Planning)

*No design issues requiring planning - architecture is stable.*

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

*No active work items. All previous items completed — see git history for details.*
