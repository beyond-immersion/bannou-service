# Mesh Plugin Deep Dive

> **Plugin**: lib-mesh
> **Schema**: schemas/mesh-api.yaml
> **Version**: 1.0.0
> **State Stores**: mesh-endpoints, mesh-appid-index, mesh-global-index (all Redis)

---

## Overview

Native service mesh providing YARP-based HTTP routing and Redis-backed service discovery. Replaces Dapr-style sidecar invocation with direct in-process service-to-service calls. Provides endpoint registration with TTL-based health tracking, four load balancing algorithms (RoundRobin, LeastConnections, Weighted, Random), a per-appId circuit breaker, and configurable retry logic with exponential backoff. Includes a background health check service for proactive failure detection. Event-driven auto-registration from service heartbeats enables zero-configuration discovery.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for endpoint registry, app-id indexes, and global index |
| lib-messaging (`IMessageBus`) | Publishing endpoint lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Subscribing to `bannou.service-heartbeats` and `bannou.full-service-mappings` |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| All services (via generated clients) | Every NSwag-generated client uses `IMeshInvocationClient` for service-to-service HTTP calls |
| lib-connect | Routes WebSocket messages to backend services via mesh invocation |
| lib-orchestrator | Manages service routing tables; publishes `FullServiceMappingsEvent` consumed by mesh |

---

## State Storage

**Stores**: 3 Redis stores (via lib-state `IStateStoreFactory`)

| Store | Prefix | Purpose |
|-------|--------|---------|
| `mesh-endpoints` | `mesh:endpoint` | Individual endpoint registration with health/load metadata |
| `mesh-appid-index` | `mesh:appid` | App-ID to instance-ID mapping for routing queries |
| `mesh-global-index` | `mesh:idx` | Global endpoint index for discovery (avoids KEYS/SCAN) |

**Key Patterns**:
- Endpoint data keyed by instance ID (GUID)
- App-ID index lists all instance IDs for a given app-id
- Global index (`_index` key) tracks all known instance IDs

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `mesh.endpoint.registered` | New endpoint registered (explicit or auto-discovered from heartbeat) |
| `mesh.endpoint.deregistered` | Endpoint removed (graceful shutdown or health check failure) |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `bannou.service-heartbeats` | `ServiceHeartbeatEvent` | Updates existing endpoint metrics or auto-registers new endpoints |
| `bannou.full-service-mappings` | `FullServiceMappingsEvent` | Atomically updates `IServiceAppMappingResolver` for all generated clients |

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
| `DefaultLoadBalancer` | `MESH_DEFAULT_LOAD_BALANCER` | `RoundRobin` | Load balancing algorithm (enum: RoundRobin, LeastConnections, Weighted, Random) |
| `LoadThresholdPercent` | `MESH_LOAD_THRESHOLD_PERCENT` | `80` | Load % above which endpoint is considered high-load |
| `EnableServiceMappingSync` | `MESH_ENABLE_SERVICE_MAPPING_SYNC` | `true` | Subscribe to FullServiceMappingsEvent for routing updates |
| `HealthCheckEnabled` | `MESH_HEALTH_CHECK_ENABLED` | `false` | Enable active health check probing |
| `HealthCheckIntervalSeconds` | `MESH_HEALTH_CHECK_INTERVAL_SECONDS` | `60` | Interval between health probes |
| `HealthCheckTimeoutSeconds` | `MESH_HEALTH_CHECK_TIMEOUT_SECONDS` | `5` | Timeout for health check requests |
| `HealthCheckStartupDelaySeconds` | `MESH_HEALTH_CHECK_STARTUP_DELAY_SECONDS` | `10` | Delay before first health probe |
| `CircuitBreakerEnabled` | `MESH_CIRCUIT_BREAKER_ENABLED` | `true` | Enable per-appId circuit breaker |
| `CircuitBreakerThreshold` | `MESH_CIRCUIT_BREAKER_THRESHOLD` | `5` | Consecutive failures before opening circuit |
| `CircuitBreakerResetSeconds` | `MESH_CIRCUIT_BREAKER_RESET_SECONDS` | `30` | Seconds before half-open probe attempt |
| `MaxRetries` | `MESH_MAX_RETRIES` | `3` | Maximum retry attempts for failed calls |
| `RetryDelayMilliseconds` | `MESH_RETRY_DELAY_MILLISECONDS` | `100` | Initial retry delay (doubles each retry) |
| `PooledConnectionLifetimeMinutes` | `MESH_POOLED_CONNECTION_LIFETIME_MINUTES` | `2` | HTTP connection pool lifetime |
| `ConnectTimeoutSeconds` | `MESH_CONNECT_TIMEOUT_SECONDS` | `10` | TCP connection timeout |
| `EndpointCacheTtlSeconds` | `MESH_ENDPOINT_CACHE_TTL_SECONDS` | `5` | TTL for cached endpoint resolution |
| `MaxTopEndpointsReturned` | `MESH_MAX_TOP_ENDPOINTS_RETURNED` | `2` | Max alternates in route response |
| `MaxServiceMappingsDisplayed` | `MESH_MAX_SERVICE_MAPPINGS_DISPLAYED` | `10` | Max mappings in diagnostic logs |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<MeshService>` | Scoped | Structured logging |
| `MeshServiceConfiguration` | Singleton | All 24 config properties above |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Heartbeat and mapping event subscription |
| `IMeshStateManager` | Singleton | Redis state via lib-state (3 stores) |
| `IServiceAppMappingResolver` | Singleton | Shared service→app-id routing (used by all generated clients) |
| `MeshInvocationClient` | Singleton | HTTP invocation with circuit breaker, retries, caching |
| `MeshHealthCheckService` | Hosted (BackgroundService) | Active endpoint health probing |
| `LocalMeshStateManager` | Singleton | In-memory state for `UseLocalRouting=true` mode |

Service lifetime is **Scoped** (per-request) for MeshService itself. Infrastructure components (state manager, invocation client) are Singleton.

---

## API Endpoints (Implementation Notes)

### Service Discovery (2 endpoints)

- **GetEndpoints** (`/mesh/endpoints/get`): Returns endpoints for an app-id. Filters by health status (default: healthy only) and optional service name. Returns healthy/total counts.
- **ListEndpoints** (`/mesh/endpoints/list`): Admin-level listing of all endpoints. Groups by status for summary (healthy, degraded, unavailable). Supports app-id prefix filter.

### Registration (3 endpoints)

- **Register** (`/mesh/register`): Announces instance availability. Generates instance ID if not provided. Stores with configurable TTL. Publishes `mesh.endpoint.registered`.
- **Deregister** (`/mesh/deregister`): Graceful shutdown removal. Looks up endpoint first (for app-id), removes from store, publishes `mesh.endpoint.deregistered` with reason=Graceful.
- **Heartbeat** (`/mesh/heartbeat`): Refreshes TTL and updates metrics (status, load%, connections). Returns `NextHeartbeatSeconds` and `TtlSeconds` for client scheduling.

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

1. **Local routing mode is minimal**: `LocalMeshStateManager` provides in-memory state for testing but does not simulate failure scenarios or load balancing nuances.
2. **Health check deregistration**: `MeshHealthCheckService` probes endpoints but the failure handling (marking unavailable, deregistering after sustained failure) depends on the full implementation of `ProbeAllEndpointsAsync`.

---

## Potential Extensions

1. **Weighted round-robin**: Combine round-robin with load-based weighting for predictable but load-aware distribution.
2. **Distributed circuit breaker**: Share circuit breaker state across instances via Redis for cluster-wide protection.
3. **Endpoint affinity**: Sticky routing for stateful services (session affinity based on request metadata).
4. **Graceful draining**: Endpoint status `ShuttingDown` could actively drain connections before full deregistration.

---

## Known Quirks & Caveats

### Bugs

No bugs identified.

### Intentional Quirks

1. **HeartbeatStatus.Overloaded maps to EndpointStatus.Degraded**: The status mapping is lossy - `Overloaded` and `Degraded` heartbeat statuses both become `Degraded` endpoint status. No distinct "overloaded" endpoint state exists.

2. **GetRoute falls back to all endpoints**: If both degradation-threshold filtering and load-threshold filtering eliminate all endpoints, the algorithm falls back to the original unfiltered list. Prevents total routing failure at the cost of routing to potentially degraded endpoints.

3. **Dual round-robin implementations**: MeshService uses `static ConcurrentDictionary<string, int>` for per-appId counters. MeshInvocationClient uses `Interlocked.Increment` on a single `int` field. Different approaches for the same problem.

4. **Circuit breaker is per-instance, not distributed**: Each `MeshInvocationClient` instance maintains its own circuit breaker state. In multi-instance deployments, one instance may have an open circuit while others are still closed.

5. **No request-level timeout in MeshInvocationClient**: The only timeout is `ConnectTimeoutSeconds` on the `SocketsHttpHandler`. There's no per-request read/response timeout - slow responses block the retry loop indefinitely until cancellation.

### Design Considerations

1. **EndpointCache uses Dictionary + lock, not ConcurrentDictionary**: The `EndpointCache` inner class in MeshInvocationClient uses a plain `Dictionary<>` with explicit lock statements instead of `ConcurrentDictionary`. Lower overhead for simple get/set but not lock-free.

2. **MeshInvocationClient is Singleton with mutable state**: The circuit breaker and endpoint cache are instance-level mutable state in a Singleton-lifetime service. Thread-safe by design (ConcurrentDictionary for circuits, lock for cache) but long-lived state accumulates.

3. **State manager lazy initialization**: `MeshStateManager.InitializeAsync()` must be called before use. The `_initialized` flag prevents re-initialization but doesn't protect against concurrent first-initialization.

4. **Static round-robin counter in MeshService**: `_roundRobinCounters` is static, meaning it persists across scoped service instances. The counter can grow unbounded as new app-ids are encountered (no eviction).

5. **Three overlapping endpoint resolution paths**: MeshService.GetRoute (for API callers), MeshInvocationClient.ResolveEndpointAsync (for generated clients), and heartbeat-based auto-registration all resolve/manage endpoints with subtly different logic.
