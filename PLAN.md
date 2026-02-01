# Implementation Plan: Distributed Circuit Breaker for lib-mesh

**Issue:** #219
**Created:** 2026-02-01
**Status:** Awaiting Approval
**Audited:** 2026-02-01 (TENET compliance verified)

---

## Executive Summary

This plan implements an event-backed local cache pattern for the distributed circuit breaker:
- **Redis** stores authoritative circuit breaker state
- **Local `ConcurrentDictionary`** provides zero-latency reads on the hot path
- **RabbitMQ pub/sub** propagates state changes across instances
- **Lua scripts** ensure atomic failure/success recording

Performance characteristics:
- State checks: **0ms** (local ConcurrentDictionary)
- Success recording: **0ms** (no-op if circuit is closed)
- Failure recording: **1-2ms** (Redis Lua script)
- State propagation: **~10-50ms** (pub/sub latency, acceptable)

**Configuration Model:** No new configuration options. The distributed circuit breaker is always-on when `CircuitBreakerEnabled=true`. Per IMPLEMENTATION TENETS (T9 Multi-Instance Safety), the per-instance implementation was a TENET violation, not an alternative mode. Adding opt-in configuration would create dead code paths and testing burden.

---

## Phase 1: Schema Changes (Schema-First)

### 1.1 Add State Store Definition

**File:** `schemas/state-stores.yaml`

Add after existing mesh stores (~line 420):

```yaml
mesh-circuit-breaker:
  backend: redis
  prefix: "mesh:cb"
  service: Mesh
  purpose: Distributed circuit breaker state for cross-instance failure tracking
  # No TTL - entries cleaned up lazily when circuit closes successfully.
  # Worst case: ~50 stale entries for decommissioned services (trivial memory).
```

**Verification:** `python3 scripts/generate-state-stores.py && dotnet build`

---

### 1.2 Add Circuit State Changed Event

**File:** `schemas/mesh-events.yaml`

Add to `components/schemas` (define enum type first per T25 Type Safety):

```yaml
CircuitState:
  type: string
  enum: [Closed, Open, HalfOpen]
  description: |
    Circuit breaker state.
    - Closed: Normal operation, requests flow through
    - Open: Circuit tripped, requests rejected immediately
    - HalfOpen: Testing recovery, one probe request allowed

MeshCircuitStateChangedEvent:
  allOf:
    - $ref: 'common-events.yaml#/components/schemas/BaseServiceEvent'
  type: object
  additionalProperties: false
  description: |
    Published when circuit breaker state changes. Consumed by all mesh instances
    to update their local cache, implementing the event-backed local cache pattern
    per IMPLEMENTATION TENETS (T9 Multi-Instance Safety).
  required:
    - appId
    - newState
    - consecutiveFailures
    - changedAt
  properties:
    appId:
      type: string
      description: App-id whose circuit state changed
    newState:
      $ref: '#/components/schemas/CircuitState'
      description: New circuit breaker state
    consecutiveFailures:
      type: integer
      minimum: 0
      description: Current consecutive failure count
    changedAt:
      type: string
      format: date-time
      description: When the state change occurred (ISO 8601)
```

Add to `x-event-publications`:

```yaml
- topic: mesh.circuit.changed
  event: MeshCircuitStateChangedEvent
  description: Published when a circuit breaker state changes (open/close/half-open)
```

Add to `x-event-subscriptions`:

```yaml
- topic: mesh.circuit.changed
  event: MeshCircuitStateChangedEvent
  handler: HandleCircuitStateChanged
```

**Verification:** `scripts/generate-service-events.sh mesh && dotnet build`

---

## Phase 2: Lua Scripts for Atomic Operations

**TENET Note (T4 Infrastructure Libs):** Direct Redis access via Lua scripts is acceptable here because:
1. lib-mesh is foundational infrastructure (same tier as lib-state, lib-messaging)
2. `MeshStateManager` already has direct `IDatabase` access
3. `RedisDistributedLockProvider` in lib-state uses the same pattern
4. Circuit breaker requires atomic read-modify-write operations that `IStateStore<T>` doesn't support

### 2.1 Create Scripts Directory

**File:** `plugins/lib-mesh/Scripts/RecordCircuitFailure.lua`

```lua
-- RecordCircuitFailure.lua
-- Atomically records a failure and transitions state if threshold reached.
--
-- KEYS[1] = circuit entry key (e.g., "mesh:cb:{appId}")
-- ARGV[1] = failure threshold
-- ARGV[2] = current timestamp (ms since epoch)
-- ARGV[3] = reset timeout milliseconds
--
-- Returns: JSON { "failures": N, "state": "Closed|Open|HalfOpen", "stateChanged": 0|1, "openedAt": N|null }

local key = KEYS[1]
local threshold = tonumber(ARGV[1])
local now = tonumber(ARGV[2])

-- Get or create circuit entry
local entry = redis.call('HGETALL', key)
local entryMap = {}
for i = 1, #entry, 2 do
    entryMap[entry[i]] = entry[i + 1]
end

local failures = tonumber(entryMap['failures'] or '0')
local state = entryMap['state'] or 'Closed'
-- openedAt is nil/null when circuit is Closed (T26: no sentinel values)
local openedAt = entryMap['openedAt'] and tonumber(entryMap['openedAt']) or nil

local stateChanged = 0

-- Increment failures
failures = failures + 1

-- Check state transitions
if state == 'Closed' and failures >= threshold then
    state = 'Open'
    openedAt = now
    stateChanged = 1
elseif state == 'HalfOpen' then
    -- HalfOpen probe failed, re-open
    state = 'Open'
    openedAt = now
    stateChanged = 1
end

-- Update Redis (only set openedAt if circuit is open)
if state == 'Open' then
    redis.call('HSET', key, 'failures', failures, 'state', state, 'openedAt', openedAt, 'updatedAt', now)
else
    redis.call('HSET', key, 'failures', failures, 'state', state, 'updatedAt', now)
    redis.call('HDEL', key, 'openedAt')  -- Remove field when not Open (T26: no sentinel values)
end

return cjson.encode({
    failures = failures,
    state = state,
    stateChanged = stateChanged,
    openedAt = openedAt  -- Will be null in JSON when nil
})
```

---

**File:** `plugins/lib-mesh/Scripts/RecordCircuitSuccess.lua`

```lua
-- RecordCircuitSuccess.lua
-- Atomically records success and resets to Closed state.
--
-- KEYS[1] = circuit entry key
-- ARGV[1] = current timestamp (ms since epoch)
--
-- Returns: JSON { "state": "Closed", "stateChanged": 0|1 }

local key = KEYS[1]
local now = tonumber(ARGV[1])

-- Get current state (default to Closed if no entry exists)
local currentState = redis.call('HGET', key, 'state') or 'Closed'
local stateChanged = (currentState ~= 'Closed') and 1 or 0

if stateChanged == 1 then
    -- Reset to closed - clear failures and remove openedAt (T26: no sentinel values)
    redis.call('HSET', key, 'failures', 0, 'state', 'Closed', 'updatedAt', now)
    redis.call('HDEL', key, 'openedAt')
end

return cjson.encode({
    state = 'Closed',
    stateChanged = stateChanged
})
```

---

**File:** `plugins/lib-mesh/Scripts/GetCircuitState.lua`

```lua
-- GetCircuitState.lua
-- Gets current state with automatic HalfOpen transition check.
--
-- KEYS[1] = circuit entry key
-- ARGV[1] = current timestamp (ms since epoch)
-- ARGV[2] = reset timeout milliseconds
--
-- Returns: JSON { "state": "Closed|Open|HalfOpen", "failures": N, "stateChanged": 0|1 }

local key = KEYS[1]
local now = tonumber(ARGV[1])
local resetTimeoutMs = tonumber(ARGV[2])

local entry = redis.call('HGETALL', key)
if #entry == 0 then
    return cjson.encode({ state = 'Closed', failures = 0, stateChanged = 0 })
end

local entryMap = {}
for i = 1, #entry, 2 do
    entryMap[entry[i]] = entry[i + 1]
end

local failures = tonumber(entryMap['failures'] or '0')
local state = entryMap['state'] or 'Closed'
local stateChanged = 0

-- Check for timeout-based transition to HalfOpen
-- openedAt only exists when state is Open (T26 compliance)
if state == 'Open' and entryMap['openedAt'] then
    local openedAt = tonumber(entryMap['openedAt'])
    if (now - openedAt) >= resetTimeoutMs then
        state = 'HalfOpen'
        stateChanged = 1
        redis.call('HSET', key, 'state', state, 'updatedAt', now)
    end
end

return cjson.encode({
    state = state,
    failures = failures,
    stateChanged = stateChanged
})
```

---

## Phase 3: Project Configuration

### 3.1 Update lib-mesh.csproj

**File:** `plugins/lib-mesh/lib-mesh.csproj`

Add embedded resource section:

```xml
<!-- Embed Lua scripts as resources for circuit breaker operations -->
<ItemGroup>
  <EmbeddedResource Include="Scripts\*.lua" />
</ItemGroup>
```

---

### 3.2 Create Lua Script Loader

**File:** `plugins/lib-mesh/Services/MeshLuaScripts.cs`

```csharp
#nullable enable

using System.Collections.Concurrent;
using System.Reflection;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Loads and caches Mesh-specific Lua scripts from embedded resources.
/// Follows the pattern established by lib-state's RedisLuaScripts.
/// </summary>
public static class MeshLuaScripts
{
    private static readonly ConcurrentDictionary<string, string> _scriptCache = new();
    private static readonly Assembly _assembly = typeof(MeshLuaScripts).Assembly;

    /// <summary>Script for atomic circuit breaker failure recording.</summary>
    public static string RecordCircuitFailure => GetScript("RecordCircuitFailure");

    /// <summary>Script for atomic circuit breaker success recording.</summary>
    public static string RecordCircuitSuccess => GetScript("RecordCircuitSuccess");

    /// <summary>Script for getting circuit state with automatic HalfOpen transition.</summary>
    public static string GetCircuitState => GetScript("GetCircuitState");

    private static string GetScript(string scriptName)
    {
        return _scriptCache.GetOrAdd(scriptName, name =>
        {
            var resourceName = $"BeyondImmersion.BannouService.Mesh.Scripts.{name}.lua";
            using var stream = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Lua script '{name}' not found at {resourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }
}
```

---

## Phase 4: Distributed Circuit Breaker Implementation

**File:** `plugins/lib-mesh/Services/DistributedCircuitBreaker.cs`

Key design points:
- Lazy Redis initialization (follows `RedisDistributedLockProvider` pattern)
- Local `ConcurrentDictionary` cache for zero-latency reads
- Event publishing on state changes via `IMessageBus`
- Direct `IMessageSubscriber` subscription for cache updates (infrastructure pattern)
- Implements `IAsyncDisposable` for Redis connection cleanup

The class will:
1. Accept `StateStoreFactoryConfiguration` for Redis connection string
2. Accept `IMessageBus` for event publishing
3. Accept `IMessageSubscriber` for event subscription (not `IEventConsumer` - see note below)
4. Provide async methods: `GetStateAsync`, `RecordSuccessAsync`, `RecordFailureAsync`
5. Subscribe directly to `mesh.circuit.changed` to update local cache

**Event Subscription Architecture Note:**
`DistributedCircuitBreaker` subscribes to events via `IMessageSubscriber` directly, NOT through `IEventConsumer`. This is because:
- `IEventConsumer` is for service-level fan-out (multiple plugins handling same event)
- The circuit breaker is infrastructure within a single plugin
- It needs to update its own local cache, not dispatch to multiple handlers
- This follows the pattern of `RabbitMQMessageSubscriber` handling its own internal events

**Error Handling (T7 Compliance):**

```csharp
public async Task<CircuitState> GetStateAsync(string appId, CancellationToken ct = default)
{
    // Check local cache first (hot path - 0ms)
    if (_localCache.TryGetValue(appId, out var cached) && !cached.IsStale)
    {
        return cached.State;
    }

    try
    {
        var database = await EnsureInitializedAsync();
        // ... Lua script execution ...
    }
    catch (Exception ex)
    {
        // T7: Log error, emit error event, gracefully degrade
        _logger.LogError(ex, "Redis unavailable for circuit breaker, defaulting to Closed for {AppId}", appId);
        await _messageBus.TryPublishErrorAsync(
            serviceId: "mesh",
            operation: "GetCircuitState",
            errorType: ex.GetType().Name,
            message: ex.Message);

        // Fail-open: allow traffic when Redis is unavailable
        // This prevents Redis outage from cascading to all service calls
        return CircuitState.Closed;
    }
}
```

---

## Phase 5: Integration into MeshInvocationClient

**File:** `plugins/lib-mesh/Services/MeshInvocationClient.cs`

Changes required:

1. **Constructor changes:**
   - Add `StateStoreFactoryConfiguration stateConfig` parameter
   - Add `IMessageBus messageBus` parameter
   - Add `IMessageSubscriber messageSubscriber` parameter
   - Create `DistributedCircuitBreaker` instance (passing dependencies)

2. **Remove inner `CircuitBreaker` class** entirely (lines 534-600)

3. **Replace `_circuitBreaker` field** with `DistributedCircuitBreaker` instance

4. **Update `InvokeMethodWithResponseAsync`:**
   - `_circuitBreaker.GetState(appId)` → `await _circuitBreaker.GetStateAsync(appId, ct)`
   - `RecordCircuitBreakerSuccess(appId)` → `await _circuitBreaker.RecordSuccessAsync(appId, ct)`
   - `RecordCircuitBreakerFailure(appId)` → `await _circuitBreaker.RecordFailureAsync(appId, ct)`

5. **Fix `EndpointCache`** (lines 605-651) - separate from circuit breaker but good hygiene:
   - Replace `Dictionary<string, ...> + lock` with `ConcurrentDictionary<string, ...>`
   - Use `TryGetValue`, `TryAdd`, `TryRemove` instead of lock-protected operations

6. **Update disposal:**
   - Change `IDisposable` to `IAsyncDisposable`
   - Add `await _circuitBreaker.DisposeAsync()` in `DisposeAsync`

---

## Phase 6: Plugin Tests

**File:** `plugins/lib-mesh.tests/DistributedCircuitBreakerTests.cs`

> **TENET Note (Testing Architecture):** Tests go in `plugins/lib-mesh.tests/`, NOT `unit-tests/`.
> Per TESTING.md: "unit-tests/: Can ONLY reference bannou-service. CANNOT reference ANY lib-* plugins"

Test scenarios:
1. State transitions: Closed → Open on threshold reached
2. State transitions: Open → HalfOpen on timeout elapsed
3. State transitions: HalfOpen → Closed on success
4. State transitions: HalfOpen → Open on failure (probe failed)
5. Event publishing: Verify `MeshCircuitStateChangedEvent` published on state change
6. Event consumption: Verify local cache updated when event received
7. Graceful degradation: Returns `Closed` when Redis unavailable (fail-open)
8. Local cache hit: Verify no Redis call when cache is fresh
9. Concurrent failures: Multiple instances recording failures atomically

Use Moq to mock `IDatabase`, `IMessageBus`, and `IMessageSubscriber`.

---

## Phase 7: Documentation Updates

**File:** `docs/plugins/MESH.md`

Updates:
1. **State Storage**: Add `mesh-circuit-breaker` store to table
2. **Events Published**: Add `mesh.circuit.changed` with description
3. **Events Consumed**: Add `mesh.circuit.changed` (self-subscription for cache sync)
4. **Remove from Known Quirks/Gaps**: "Circuit breaker is per-instance, not distributed"
5. **Add to Design Considerations**: Explain event-backed local cache pattern
6. **Work Tracking**: Mark #219 as completed in Completed Work section

---

## Implementation Order

| Step | File(s) | Verification |
|------|---------|--------------|
| 1 | `schemas/state-stores.yaml` | `python3 scripts/generate-state-stores.py && dotnet build` |
| 2 | `schemas/mesh-events.yaml` | `scripts/generate-service-events.sh mesh && dotnet build` |
| 3 | `plugins/lib-mesh/lib-mesh.csproj` | `dotnet build` |
| 4 | `plugins/lib-mesh/Scripts/*.lua` (3 files) | N/A (embedded resources) |
| 5 | `plugins/lib-mesh/Services/MeshLuaScripts.cs` | `dotnet build` |
| 6 | `plugins/lib-mesh/Services/DistributedCircuitBreaker.cs` | `dotnet build` |
| 7 | `plugins/lib-mesh/Services/MeshInvocationClient.cs` | `dotnet build` |
| 8 | `plugins/lib-mesh.tests/DistributedCircuitBreakerTests.cs` | `dotnet test` |
| 9 | `docs/plugins/MESH.md` | Manual review |

---

## TENETs Applied

| Change | TENET Category | Specific Rules |
|--------|----------------|----------------|
| State store in schema | FOUNDATION | T1 Schema-First Development |
| CircuitState enum type | IMPLEMENTATION | T25 Type Safety Across All Models |
| Event schema with $ref | FOUNDATION | T1 Schema-First, T5 Event-Driven Architecture |
| No sentinel values in Lua | IMPLEMENTATION | T26 No Sentinel Values |
| Direct Redis (justified) | FOUNDATION | T4 Infrastructure Libs (exception documented) |
| ConcurrentDictionary cache | IMPLEMENTATION | T9 Multi-Instance Safety |
| Event-backed local cache | IMPLEMENTATION | T9 Multi-Instance Safety (explicitly allowed pattern) |
| Graceful degradation | IMPLEMENTATION | T7 Error Handling |
| TryPublishErrorAsync on failure | IMPLEMENTATION | T7 Error Handling |
| Async methods with await | IMPLEMENTATION | T23 Async Method Pattern |
| IAsyncDisposable | IMPLEMENTATION | T24 Using Statement Pattern |
| Tests in lib-mesh.tests | QUALITY | T11 Testing Requirements, TESTING.md |
| XML documentation | QUALITY | T19 XML Documentation |
| No opt-in config | IMPLEMENTATION | T9 (violation, not alternative), T21 (no dead config) |

---

## Risk Mitigation

| Risk | Mitigation | TENET Basis |
|------|------------|-------------|
| Redis unavailable | Fail-open: return Closed state, emit error event, allow traffic | T7, T9 |
| Event delivery delay | Acceptable ~50ms eventual consistency; local cache prevents stale reads from blocking | T9 |
| Lua script errors | Comprehensive try/catch with logging and TryPublishErrorAsync | T7 |
| Concurrent state changes | Lua scripts are atomic within Redis; events are idempotent | T9 |
| Memory growth | Lazy cleanup when circuits close; bounded by number of appIds (~50 max) | T21 |

---

## Changelog

- **2026-02-01 (Audit):** Fixed T25 violation (enum type), T26 violation (sentinel values), test location (TESTING.md), added T4/T7 specifics, configuration rationale
