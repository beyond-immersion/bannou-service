# Mesh Plugin Deep Dive

> **Plugin**: lib-mesh
> **Schema**: schemas/mesh-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: mesh-endpoints, mesh-appid-index, mesh-global-index, mesh-circuit-breaker (all Redis)
> **Implementation Map**: [docs/maps/MESH.md](../maps/MESH.md)
> **Short**: Service-to-service invocation via YARP with circuit breaking and Redis-backed discovery

---

## Overview

Native service mesh (L0 Infrastructure) providing direct in-process service-to-service calls with YARP-based HTTP routing and Redis-backed service discovery. Provides endpoint registration with TTL-based health tracking, configurable load balancing, a distributed per-appId circuit breaker, and retry logic with exponential backoff. Includes proactive health checking with automatic deregistration and event-driven auto-registration from Orchestrator heartbeats for zero-configuration discovery.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| All services (via generated clients) | Every NSwag-generated client uses `IMeshInvocationClient` for service-to-service HTTP calls |
| lib-connect | Routes WebSocket messages to backend services via mesh invocation |
| lib-orchestrator | Manages service routing tables; pushes mapping updates via `IServiceMappingReceiver` DI interface |

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
| `EnableServiceMappingSync` | `MESH_ENABLE_SERVICE_MAPPING_SYNC` | `true` | Subscribe to `mesh.mappings.updated` L0 events for cross-node routing sync |
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
            ├── Success (2xx) or Application Error (4xx, 500) → RecordSuccess → return response
            │
            └── Infrastructure Error (502, 503, 504) or HttpRequestException
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

1. ~~**L0 subscribing to L3 events (`bannou.full-service-mappings`)**~~: **FIXED** (2026-03-13) - Replaced with DI interface inversion: Mesh (L0) provides `IServiceMappingReceiver` implementation that Orchestrator (L3) discovers and pushes mapping updates into. The implementation updates the local `IServiceAppMappingResolver` and broadcasts `mesh.mappings.updated` (L0→L0) events for cross-node sync. Mesh no longer subscribes to any L3 events.

### Intentional Quirks (Documented Behavior)

1. **`InvokeRawAsync` bypasses circuit breaker**: The raw API invocation path (`InvokeRawAsync`) intentionally skips circuit breaker checks and does not record success/failure to the breaker. This is by design because raw API execution targets services that may be optional or disabled — failures against absent services should not trip the circuit.

2. **Two-level routing model**: Mesh routing operates in two stages. First, `IServiceAppMappingResolver` maps a **service name** to an **app-id** (e.g., `"auth"` → `"bannou-auth-node1"`). This mapping is populated by Orchestrator via the `IServiceMappingReceiver` DI interface (Orchestrator pushes updates; Mesh broadcasts `mesh.mappings.updated` L0 events for cross-node sync). Second, Mesh resolves the **app-id** to a specific **endpoint** using load balancing across all healthy instances registered under that app-id. Node affinity is handled at the first level (Orchestrator assigns per-node app-ids like `bannou-{service}-{nodeName}`), so Mesh's load balancing at the second level is always stateless. In development, all services map to the default `"bannou"` app-id (single instance); in production, Orchestrator controls which node each service routes to by pushing different mappings.

3. **Resilient routing fallback**: If health/load filtering eliminates all endpoints, `GetRoute` falls back to the full unfiltered list. Prefers degraded routing over total failure.

4. **Circuit breaker eventual consistency**: State syncs across instances via Redis + RabbitMQ events within milliseconds. Brief disagreement during propagation is expected and harmless.

5. **Global index lazy cleanup**: The `mesh-global-index` store cleans stale entries on access rather than via TTL. Endpoints themselves have TTL, so this is an optimization choice, not a bug.

6. **Empty mappings = reset**: Empty mappings passed via `IServiceMappingReceiver.UpdateMappingsAsync` (or via `mesh.mappings.updated` event) reset all routing to default ("bannou"). This is intentional for container teardown.

### Design Considerations (Requires Planning)

*No design issues requiring planning.*

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Active

*No active work items.*

### Completed

- [#638](https://github.com/beyond-immersion/bannou-service/issues/638) — T27: Mesh (L0) subscribes to Orchestrator (L3) full-service-mappings event — **FIXED** (2026-03-13)
