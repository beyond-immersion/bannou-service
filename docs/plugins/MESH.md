# Mesh Plugin Deep Dive

> **Plugin**: lib-mesh
> **Schema**: schemas/mesh-api.yaml
> **Version**: 1.0.0
> **State Stores**: mesh-endpoints, mesh-appid-index, mesh-global-index (all Redis), + raw Redis for circuit breaker (`mesh:cb:{appId}`)

---

## Overview

Native service mesh providing YARP-based HTTP routing and Redis-backed service discovery. Replaces Dapr-style sidecar invocation with direct in-process service-to-service calls. Provides endpoint registration with TTL-based health tracking, five load balancing algorithms (RoundRobin, LeastConnections, Weighted, WeightedRoundRobin, Random), a distributed per-appId circuit breaker with cross-instance synchronization, and configurable retry logic with exponential backoff. Includes a background health check service for proactive failure detection with automatic deregistration after consecutive failures. Event-driven auto-registration from service heartbeats enables zero-configuration discovery.

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

**Stores**: 3 Redis stores (via lib-state `IStateStoreFactory`) + 1 raw Redis pattern (via `IRedisOperations`)

| Store | Key Pattern | Data Type | Purpose |
|-------|-------------|-----------|---------|
| `mesh-endpoints` | `{instanceId}` (GUID string) | `MeshEndpoint` | Individual endpoint registration with health/load metadata |
| `mesh-appid-index` | `{appId}` (set of instance IDs) | `Set<string>` | App-ID to instance-ID mapping for routing queries |
| `mesh-global-index` | `_index` (set of instance IDs) | `Set<string>` | Global endpoint index for discovery (avoids KEYS/SCAN) |
| *(raw Redis)* | `mesh:cb:{appId}` (hash) | Hash: failures, state, openedAt | Distributed circuit breaker state (via `IRedisOperations`, not state store factory) |

**Key Patterns**:
- Endpoint data keyed by instance ID (GUID)
- App-ID index lists all instance IDs for a given app-id (with TTL refresh on heartbeat)
- Global index (`_index` key) tracks all known instance IDs (no TTL - cleaned lazily on access)
- Circuit breaker uses raw Redis via `IRedisOperations` with Lua scripts for atomic state transitions (no TTL - cleared on success)

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `mesh.endpoint.registered` | `MeshEndpointRegisteredEvent` | New endpoint registered (explicit or auto-discovered from heartbeat) |
| `mesh.endpoint.deregistered` | `MeshEndpointDeregisteredEvent` | Endpoint removed (graceful shutdown or health check failure) |
| `mesh.circuit.changed` | `MeshCircuitStateChangedEvent` | Circuit breaker state changes (Closed→Open, Open→HalfOpen, HalfOpen→Closed, etc.) |

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
| `CircuitBreakerEnabled` | `MESH_CIRCUIT_BREAKER_ENABLED` | `true` | Enable per-appId circuit breaker |
| `CircuitBreakerThreshold` | `MESH_CIRCUIT_BREAKER_THRESHOLD` | `5` | Consecutive failures before opening circuit |
| `CircuitBreakerResetSeconds` | `MESH_CIRCUIT_BREAKER_RESET_SECONDS` | `30` | Seconds before half-open probe attempt |
| `MaxRetries` | `MESH_MAX_RETRIES` | `3` | Maximum retry attempts for failed calls |
| `RetryDelayMilliseconds` | `MESH_RETRY_DELAY_MILLISECONDS` | `100` | Initial retry delay (doubles each retry) |
| `PooledConnectionLifetimeMinutes` | `MESH_POOLED_CONNECTION_LIFETIME_MINUTES` | `2` | HTTP connection pool lifetime |
| `ConnectTimeoutSeconds` | `MESH_CONNECT_TIMEOUT_SECONDS` | `10` | TCP connection timeout |
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
| `MeshServiceConfiguration` | Singleton | All 24 config properties above |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IMessageSubscriber` | Scoped | Circuit state change subscription in MeshInvocationClient |
| `IEventConsumer` | Scoped | Heartbeat and mapping event subscription (via generated code) |
| `IMeshStateManager` | Singleton | Redis state via lib-state (3 stores + raw Redis) |
| `IServiceAppMappingResolver` | Singleton | Shared service→app-id routing (used by all generated clients) |
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

- **Register** (`/mesh/register`): Announces instance availability. Generates instance ID if not provided. Stores with configurable TTL. Publishes `mesh.endpoint.registered`.
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

1. **Local routing mode is minimal**: `LocalMeshStateManager` provides in-memory state for testing but does not simulate failure scenarios or load balancing nuances. All calls return the same local endpoint regardless of app-id.
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/162 -->

---

## Potential Extensions

1. **Endpoint affinity**: Sticky routing for stateful services (session affinity based on request metadata).

2. **Graceful draining**: Endpoint status `ShuttingDown` could actively drain connections before full deregistration.

3. **Request-level timeout**: Add a configurable per-request timeout separate from connection timeout to prevent slow responses from blocking the retry loop indefinitely.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No known bugs requiring immediate attention.*

### Intentional Quirks (Documented Behavior)

1. **HeartbeatStatus.Overloaded maps to EndpointStatus.Degraded**: The status mapping is lossy - `Overloaded` and `Degraded` heartbeat statuses both become `Degraded` endpoint status. No distinct "overloaded" endpoint state exists.

2. **GetRoute falls back to all endpoints**: If both degradation-threshold filtering and load-threshold filtering eliminate all endpoints, the algorithm falls back to the original unfiltered list. Prevents total routing failure at the cost of routing to potentially degraded endpoints.

3. **Dual round-robin implementations**: MeshService uses `static ConcurrentDictionary<string, RoundRobinCounter>` for per-appId counters with `Interlocked.Increment`. MeshInvocationClient uses `Interlocked.Increment` on a single `int` field (no per-appId tracking). Different approaches for the same problem - not a bug, but worth noting.

4. **Distributed circuit breaker uses eventual consistency**: Circuit breaker state is shared across instances via Redis + RabbitMQ events. All instances see the same circuit state within event propagation latency (~milliseconds). During the propagation window, different instances may briefly disagree about circuit state.

5. **No request-level timeout in MeshInvocationClient**: The only timeout is `ConnectTimeoutSeconds` on the `SocketsHttpHandler`. There's no per-request read/response timeout - slow responses block the retry loop until the configured retry attempts are exhausted or cancellation is requested.

6. **Global index has no TTL**: The `mesh-global-index` store adds instance IDs on registration but removal only happens via explicit deregistration or lazy cleanup when `GetAllEndpointsAsync` encounters stale entries. App-id index has TTL refresh on heartbeat, but global index does not.

7. **Empty service mappings reset to default routing**: When `HandleServiceMappingsAsync` receives a `FullServiceMappingsEvent` with empty mappings, it resets all routing to the default app-id ("bannou"). This is intentional for container teardown scenarios.

8. **Health check deregistration uses configurable threshold**: After `HealthCheckFailureThreshold` (default 3) consecutive probe failures, endpoints are automatically deregistered with `HealthCheckFailed` reason. Set threshold to 0 to disable deregistration and rely solely on TTL expiry.

### Design Considerations (Requires Planning)

1. **MeshInvocationClient is Singleton with mutable state**: The circuit breaker and endpoint cache are instance-level mutable state in a Singleton-lifetime service. Thread-safe by design (ConcurrentDictionary for all caches) but long-lived state accumulates.

2. **Event-backed local cache pattern for circuit breaker**: The distributed circuit breaker uses a hybrid architecture: Redis stores authoritative state (via atomic Lua scripts), local `ConcurrentDictionary` provides 0ms reads on hot path, and RabbitMQ events propagate state changes across instances. This pattern avoids Redis round-trip latency on every invocation while ensuring eventual consistency across the cluster.

3. **State manager lazy initialization**: `MeshStateManager.InitializeAsync()` must be called before use. Uses `Interlocked.CompareExchange` for thread-safe first initialization and resets `_initialized` flag on failure to allow retry.

4. **Static load balancing state in MeshService**: Both `_roundRobinCounters` (for RoundRobin) and `_weightedRoundRobinCurrentWeights` (for WeightedRoundRobin) are static, meaning they persist across scoped service instances. Configure `LoadBalancingStateMaxAppIds` to limit growth (default 0 = unlimited). Use `ResetLoadBalancingStateForTesting()` to clear in tests.

5. **Three overlapping endpoint resolution paths**: MeshService.GetRoute (for API callers), MeshInvocationClient.ResolveEndpointAsync (for generated clients), and heartbeat-based auto-registration all resolve/manage endpoints with subtly different logic. This is intentional separation of concerns but requires awareness when debugging routing issues.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed

- **2026-02-07**: Closed [#161](https://github.com/beyond-immersion/bannou-service/issues/161) - removed `metadata` field from `RegisterEndpointRequest` schema.
- **2026-02-07**: Closed [#219](https://github.com/beyond-immersion/bannou-service/issues/219) - distributed circuit breaker implementation complete.
- **2026-02-07**: Created [#323](https://github.com/beyond-immersion/bannou-service/issues/323) for future degradation events (tied to Orchestrator response).
- **2026-02-07**: Closed [#322](https://github.com/beyond-immersion/bannou-service/issues/322) - all production readiness items complete, including event topic fix (`bannou.service-heartbeat`).
