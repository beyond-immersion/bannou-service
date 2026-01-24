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
| `HeartbeatIntervalSeconds` | `MESH_HEARTBEAT_INTERVAL_SECONDS` | `30` | Recommended interval between heartbeats |
| `EndpointTtlSeconds` | `MESH_ENDPOINT_TTL_SECONDS` | `90` | TTL for endpoint registration (>2x heartbeat) |
| `DegradationThresholdSeconds` | `MESH_DEGRADATION_THRESHOLD_SECONDS` | `60` | Time without heartbeat before marking degraded |
| `DefaultLoadBalancer` | `MESH_DEFAULT_LOAD_BALANCER` | `RoundRobin` | Algorithm: RoundRobin, LeastConnections, Weighted, Random |
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
| `MeshServiceConfiguration` | Singleton | All 23 config properties above |
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

## Tenet Violations (Fix Immediately)

### 1. FOUNDATION TENETS - Missing Null Checks in Constructor (T6)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshService.cs`
**Lines**: 51-58

The `MeshService` constructor assigns dependencies directly without null checks. Per the Service Implementation Pattern (T6), all dependencies must be validated with `?? throw new ArgumentNullException(...)` or `ArgumentNullException.ThrowIfNull(...)`.

```csharp
// Current (no null checks):
_messageBus = messageBus;
_logger = logger;
_configuration = configuration;
_stateManager = stateManager;
_mappingResolver = mappingResolver;

// Required:
_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
_logger = logger ?? throw new ArgumentNullException(nameof(logger));
_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
_stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
_mappingResolver = mappingResolver ?? throw new ArgumentNullException(nameof(mappingResolver));
ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
```

---

### 2. IMPLEMENTATION TENETS - Non-Async Task-Returning Methods (T23)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/Services/LocalMeshStateManager.cs`
**Lines**: 49-53, 56-59, 63-68, 71-76, 79-89, 92-97, 100-103, 106-117

All methods in `LocalMeshStateManager` return `Task.FromResult(...)` without the `async` keyword. Per T23, all methods returning Task must be async with at least one `await`.

**Fix**: Convert all methods to use `async` keyword with `await Task.CompletedTask` and direct return values, e.g.:

```csharp
// Current:
public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
{
    _logger.LogInformation("...");
    return Task.FromResult(true);
}

// Required:
public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
{
    _logger.LogInformation("...");
    await Task.CompletedTask;
    return true;
}
```

---

### 3. IMPLEMENTATION TENETS - Enum.TryParse in Business Logic for Configuration Value (T25)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshService.cs`
**Line**: 396

`DefaultLoadBalancer` is defined as `string` in the configuration schema with an enum constraint. The service then uses `Enum.TryParse<LoadBalancerAlgorithm>(_configuration.DefaultLoadBalancer, ...)` at runtime in business logic. Per T25, the configuration property should be generated as the `LoadBalancerAlgorithm` enum type so no parsing is needed in business logic.

**Root Cause**: The `mesh-configuration.yaml` defines `DefaultLoadBalancer` as `type: string` with an `enum` constraint, but the code generator produces it as `string`. The schema should define it such that the generated configuration class uses the `LoadBalancerAlgorithm` enum directly.

**Fix**: Either update the code generation to produce enum types for enum-constrained string config properties, or accept the current pattern as a boundary conversion (config loading is a system boundary). If accepted, add a comment documenting this as a boundary conversion per T25 exception rules.

---

### 4. IMPLEMENTATION TENETS - Hardcoded TTL Magic Number (T21)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshServicePlugin.cs`
**Line**: 127

The endpoint registration call uses a hardcoded `90` instead of `_configuration.EndpointTtlSeconds`:

```csharp
var registered = await _stateManager.RegisterEndpointAsync(endpoint, 90);
```

**Fix**: Use the configuration value:
```csharp
var registered = await _stateManager.RegisterEndpointAsync(endpoint, meshConfig.EndpointTtlSeconds);
```

---

### 5. IMPLEMENTATION TENETS - Hardcoded MaxConnections Magic Number (T21)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshServiceEvents.cs`
**Line**: 78

The auto-registration from heartbeat uses a hardcoded `1000` fallback for `MaxConnections`:

```csharp
MaxConnections = evt.Capacity?.MaxConnections ?? 1000,
```

**Fix**: Add a `DefaultMaxConnections` configuration property to `mesh-configuration.yaml` and use it here:
```csharp
MaxConnections = evt.Capacity?.MaxConnections ?? _configuration.DefaultMaxConnections,
```

---

### 6. IMPLEMENTATION TENETS - Plain Dictionary Instead of ConcurrentDictionary (T9)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/Services/MeshInvocationClient.cs`
**Line**: 476

The `EndpointCache` inner class uses a plain `Dictionary<string, (MeshEndpoint, DateTimeOffset)>` protected by explicit `lock` statements. While functionally thread-safe due to locking, T9 requires `ConcurrentDictionary` for local caches:

```csharp
// Current:
private readonly Dictionary<string, (MeshEndpoint Endpoint, DateTimeOffset Expiry)> _cache = new();
private readonly object _lock = new();

// T9 requires:
private readonly ConcurrentDictionary<string, (MeshEndpoint Endpoint, DateTimeOffset Expiry)> _cache = new();
```

**Note**: The current implementation IS thread-safe via explicit locks. This is a T9 conformance issue (ConcurrentDictionary is the mandated pattern), not a correctness bug. The deep-dive document already calls this out under Design Considerations.

---

### 7. IMPLEMENTATION TENETS - Error Handling Missing ApiException Catch (T7)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshService.cs`
**Lines**: 95-106, 143-153, 207-218, 258-269, 322-331, 419-429, 534-545

All endpoint methods catch only `Exception` without first catching `ApiException`. Per T7, the standard pattern requires catching `ApiException` first (for expected API errors logged as Warning with status propagation), then `Exception` (for unexpected errors logged as Error with error event publication).

```csharp
// Current pattern (all methods):
catch (Exception ex)
{
    _logger.LogError(ex, "...");
    await _messageBus.TryPublishErrorAsync(...);
    return (StatusCodes.InternalServerError, null);
}

// Required T7 pattern:
catch (ApiException ex)
{
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
catch (Exception ex)
{
    _logger.LogError(ex, "...");
    await _messageBus.TryPublishErrorAsync(...);
    return (StatusCodes.InternalServerError, null);
}
```

**Note**: MeshService does not make service-to-service calls (it uses IMeshStateManager directly, which throws plain exceptions on failure), so `ApiException` would never actually be thrown. This is a conformance issue for pattern consistency, not a functional bug.

---

### 8. IMPLEMENTATION TENETS - GetMappingsAsync Has No Try-Catch (T7)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshService.cs`
**Lines**: 437-469

`GetMappingsAsync` is the only endpoint method without a try-catch block. While its implementation is purely synchronous (in-memory mapping resolver), T7 requires all endpoint methods to have error handling for consistency and to catch any unexpected exceptions.

**Fix**: Wrap the body in a try-catch matching the T7 pattern.

---

### 9. IMPLEMENTATION TENETS - await Task.CompletedTask Placement (T23)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshServiceEvents.cs`
**Line**: 167

In `HandleServiceMappingsAsync`, `await Task.CompletedTask` is placed AFTER the try-catch block at the end of the method. This is semantically correct but awkward - the `await` serves no purpose at the end of a method that has already completed all work synchronously. Per T23, async methods should have meaningful awaits. Since the method is `async`, having `await Task.CompletedTask` at the end is technically compliant but should be at the top (before business logic) per established project patterns.

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshService.cs`
**Line**: 441

In `GetMappingsAsync`, `await Task.CompletedTask` is at the top of the method, which is the correct placement.

**Fix for MeshServiceEvents.cs**: Move `await Task.CompletedTask;` to the beginning of the method body, before the try-catch block.

---

### 10. IMPLEMENTATION TENETS - Missing Error Event on Heartbeat Handler Failure (T7)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshServiceEvents.cs`
**Lines**: 92-95

`HandleServiceHeartbeatAsync` catches exceptions but only logs - does not call `TryPublishErrorAsync`. Per T7, unexpected errors that are logged at Error level should also publish error events:

```csharp
// Current:
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing service heartbeat from {AppId}", evt.AppId);
}

// Required:
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing service heartbeat from {AppId}", evt.AppId);
    await _messageBus.TryPublishErrorAsync("mesh", "HandleServiceHeartbeat",
        ex.GetType().Name, ex.Message);
}
```

---

### 11. IMPLEMENTATION TENETS - Missing Error Event on Mappings Handler Failure (T7)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshServiceEvents.cs`
**Lines**: 162-165

Same issue as above - `HandleServiceMappingsAsync` catches exceptions and logs at Error level but does not call `TryPublishErrorAsync`.

---

### 12. QUALITY TENETS - Missing XML Documentation on Private/Internal Members (T19)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/Services/MeshInvocationClient.cs`

The following private members of the nested `EndpointCache` class lack XML documentation:
- `TryGet` method (line 484) - no `<param>` docs
- `Set` method (line 504) - no `<param>` docs
- `Invalidate` method (line 512) - no `<param>` docs

The `CircuitEntry` class fields (lines 464-466) lack XML documentation:
- `ConsecutiveFailures`
- `State`
- `OpenedAt`

**Note**: These are private/internal nested classes, so this is lower priority than public API documentation.

---

### 13. IMPLEMENTATION TENETS - MeshStateManager._initialized Not Thread-Safe (T9)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/Services/MeshStateManager.cs`
**Line**: 30, 50-54, 72

The `_initialized` field is a plain `bool` without any synchronization. If two threads call `InitializeAsync()` concurrently, both may pass the `if (_initialized)` check before either sets it to `true`, causing double initialization. Per T9, either use a distributed lock, `Interlocked`, or a proper synchronization primitive:

```csharp
// Fix option 1: volatile + compare-and-swap
private volatile bool _initialized;
// Fix option 2: Interlocked pattern
private int _initializeStarted;
if (Interlocked.CompareExchange(ref _initializeStarted, 1, 0) != 0) return true;
```

---

### 14. QUALITY TENETS - Missing XML Documentation on MeshServicePlugin (T19)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/MeshServicePlugin.cs`

The `ConfigureServices` method (line 24) has no `<param>` documentation for the `services` parameter, and no `<returns>` documentation. The private fields `_stateManager`, `_useLocalRouting`, and `_cachedConfig` (lines 20-22) lack XML documentation.

---

### 15. QUALITY TENETS - Missing XML Documentation on AssemblyInfo.cs (T19)

**File**: `/home/lysander/repos/bannou/plugins/lib-mesh/AssemblyInfo.cs`

No file-level XML documentation. This is minor and common across assembly info files.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Auto-registration uses appId as Host**: When an endpoint is auto-registered from a heartbeat event, `Host` is set to the app-id string (not an IP). This enables Docker Compose DNS-based routing where the app-id is the container service name.

2. **HeartbeatStatus.Overloaded maps to EndpointStatus.Degraded**: The status mapping is lossy - `Overloaded` and `Degraded` heartbeat statuses both become `Degraded` endpoint status. No distinct "overloaded" endpoint state exists.

3. **GetRoute falls back to all endpoints**: If both degradation-threshold filtering and load-threshold filtering eliminate all endpoints, the algorithm falls back to the original unfiltered list. Prevents total routing failure at the cost of routing to potentially degraded endpoints.

4. **Empty FullServiceMappingsEvent resets to default routing**: An event with empty mappings means "reset all services to route to the default app-id (bannou)". This is valid orchestrator behavior for returning to monolithic mode.

5. **Health checking disabled by default**: Active health probing (`HealthCheckEnabled=false`) is off by default. The mesh relies on heartbeat-driven registration and TTL expiry for passive health management.

6. **Dual round-robin implementations**: MeshService uses `static ConcurrentDictionary<string, int>` for per-appId counters. MeshInvocationClient uses `Interlocked.Increment` on a single `int` field. Different approaches for the same problem.

7. **Circuit breaker is per-instance, not distributed**: Each `MeshInvocationClient` instance maintains its own circuit breaker state. In multi-instance deployments, one instance may have an open circuit while others are still closed.

### Design Considerations (Requires Planning)

1. **EndpointCache uses Dictionary + lock, not ConcurrentDictionary**: The `EndpointCache` inner class in MeshInvocationClient uses a plain `Dictionary<>` with explicit lock statements instead of `ConcurrentDictionary`. Lower overhead for simple get/set but not lock-free.

2. **MeshInvocationClient is Singleton with mutable state**: The circuit breaker and endpoint cache are instance-level mutable state in a Singleton-lifetime service. Thread-safe by design (ConcurrentDictionary for circuits, lock for cache) but long-lived state accumulates.

3. **State manager lazy initialization**: `MeshStateManager.InitializeAsync()` must be called before use. The `_initialized` flag prevents re-initialization but doesn't protect against concurrent first-initialization.

4. **Static round-robin counter in MeshService**: `_roundRobinCounters` is static, meaning it persists across scoped service instances. The counter can grow unbounded as new app-ids are encountered (no eviction).

5. **No request-level timeout in MeshInvocationClient**: The only timeout is `ConnectTimeoutSeconds` on the `SocketsHttpHandler`. There's no per-request read/response timeout - slow responses block the retry loop indefinitely until cancellation.

6. **Three overlapping endpoint resolution paths**: MeshService.GetRoute (for API callers), MeshInvocationClient.ResolveEndpointAsync (for generated clients), and heartbeat-based auto-registration all resolve/manage endpoints with subtly different logic.
