# State Plugin Deep Dive

> **Plugin**: lib-state
> **Schema**: schemas/state-api.yaml
> **Version**: 1.0.0
> **State Store**: Self (manages all state stores for the platform)

---

## Overview

The State service is the infrastructure abstraction layer that provides all Bannou services with access to Redis and MySQL backends through a unified API. It operates in a dual role: (1) as the `IStateStoreFactory` infrastructure library used by all services for state persistence, and (2) as an HTTP API providing direct state access for debugging and administration. Supports Redis (ephemeral/session data), MySQL (durable/queryable data), and InMemory (testing) backends with optimistic concurrency via ETags, TTL support, sorted sets, and JSON path queries.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| StackExchange.Redis 2.10.1 | Redis connection multiplexing and operations |
| NRedisStack 0.13.1 | Redis JSON and search (FT) commands |
| Pomelo.EntityFrameworkCore.MySql 9.0.0 | MySQL via EF Core |
| Microsoft.EntityFrameworkCore 9.0.0 | ORM and change tracking |
| lib-messaging (`IMessageBus`) | Error event publishing |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| Every service | Uses `IStateStoreFactory.GetStore<T>()` for typed state access |
| lib-leaderboard | Uses sorted set operations for rankings |
| lib-permission | Uses Redis store with distributed locks |
| lib-asset | Uses Redis for uploads, MySQL for metadata |

All services depend on state infrastructure. The HTTP API (`IStateClient`) is used for debugging/admin only.

---

## State Storage

**Self-managed**: This plugin defines and manages all state stores across Bannou.

**Store Registry**: `schemas/state-stores.yaml` (560 lines, ~35 stores)

### Backend Distribution

| Backend | Count | Use Case |
|---------|-------|----------|
| Redis | ~10 | Sessions, caches, ephemeral state, leaderboards |
| MySQL | ~25 | Durable entity data, queryable records |
| Memory | 0 (runtime only) | Testing with `UseInMemory=true` |

### Key Structure (Redis)

| Pattern | Purpose |
|---------|---------|
| `{prefix}:{key}` | Primary value storage |
| `{prefix}:{key}:meta` | Hash with version + updated timestamp |
| `{prefix}:set:{key}` | Set members |
| `{prefix}:zset:{key}` | Sorted set (score-based) |
| `{storeName}-idx` | FT search index (auto-created) |

### Key Structure (MySQL)

| Column | Type | Purpose |
|--------|------|---------|
| `StoreName` | VARCHAR(255) | Part of composite PK |
| `Key` | VARCHAR(255) | Part of composite PK |
| `ValueJson` | LONGTEXT | JSON-serialized value |
| `ETag` | VARCHAR(64) | SHA256[0:12] hash |
| `Version` | INT | Concurrency token |
| `CreatedAt` / `UpdatedAt` | TIMESTAMP | Audit timestamps |

---

## Events

### Published Events

This service **intentionally publishes no lifecycle events**. State change events were considered expensive to publish for every operation. Error events are published via `TryPublishErrorAsync` for operational visibility.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `UseInMemory` | `STATE_USE_INMEMORY` | `false` | Use in-memory stores for testing |
| `RedisConnectionString` | `STATE_REDIS_CONNECTION_STRING` | `"bannou-redis:6379"` | Redis host:port |
| `MySqlConnectionString` | `STATE_MYSQL_CONNECTION_STRING` | `"server=bannou-mysql;..."` | Full MySQL connection string |
| `ConnectionTimeoutSeconds` | `STATE_CONNECTION_TIMEOUT_SECONDS` | `60` | Database connection timeout |

### Unused Configuration Properties

| Property | Env Var | Default | Notes |
|----------|---------|---------|-------|
| `DefaultConsistency` | `STATE_DEFAULT_CONSISTENCY` | `"strong"` | Defined but never evaluated in service code |
| `EnableMetrics` | `STATE_ENABLE_METRICS` | `true` | Feature flag, never implemented |
| `EnableTracing` | `STATE_ENABLE_TRACING` | `true` | Feature flag, never implemented |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `IStateStoreFactory` | Singleton | Creates typed store instances, manages connections |
| `StateStoreFactory` | Singleton | Implementation with Redis/MySQL initialization |
| `IDistributedLockProvider` | Singleton | Redis-backed distributed mutex |
| `StateService` | Scoped | HTTP API implementation |

Service lifetime is **Scoped** for the HTTP API, **Singleton** for the factory.

---

## API Endpoints (Implementation Notes)

### Get (`/state/get`)

Returns value with ETag for concurrent-safe retrieval. Uses `GetWithETagAsync()` internally. Returns NotFound if store doesn't exist. Includes `StateMetadata` in response.

### Save (`/state/save`)

Supports optimistic concurrency via ETag in `StateOptions`. If ETag provided and mismatches, returns 409 Conflict. TTL support for Redis stores. Uses Redis transactions for atomicity.

### Delete (`/state/delete`)

Boolean response indicates deletion success. Returns false if key not found.

### Query (`/state/query`)

Routes to MySQL for conditions-based queries (JSON path expressions) or Redis Search if enabled. Returns BadRequest if Redis search not supported for the store. MySQL uses `JSON_EXTRACT`, `JSON_UNQUOTE`, and `JSON_CONTAINS_PATH` functions. Supports operators: equals, notEquals, greaterThan, lessThan, contains, in, exists, notExists, fullText.

### Bulk Get (`/state/bulk-get`)

Uses `GetBulkAsync()` for efficient multi-key retrieval. Returns per-key `Found` flags. Redis uses MGET; MySQL uses multi-key query.

### List Stores (`/state/list-stores`)

Returns registered store names with backend info. Optional backend filter (Redis/MySQL). KeyCount always null (not implemented).

---

## Visual Aid

```
State Store Architecture
==========================

  Service Code                StateStoreFactory              Backends
  ============                ================              ========

  _stateStoreFactory          GetStore<T>(name)
  .GetStore<T>()  ──────────► ┌─────────────┐
                               │ Store Cache  │
                               │ (Concurrent  │
                               │  Dictionary) │
                               └──────┬──────┘
                                      │
                    ┌─────────────────┼─────────────────┐
                    │                 │                  │
              ┌─────▼─────┐    ┌─────▼──────┐    ┌─────▼─────┐
              │RedisState  │    │RedisSearch │    │MySqlState │
              │Store<T>    │    │StateStore  │    │Store<T>   │
              │            │    │<T>         │    │           │
              │ String ops │    │ JSON ops   │    │ EF Core   │
              │ Set ops    │    │ FT search  │    │ JSON query│
              │ ZSet ops   │    │ All Redis  │    │ No sets   │
              │ TTL support│    │ ops +search│    │ No zsets  │
              └─────┬──────┘    └─────┬──────┘    └─────┬─────┘
                    │                 │                  │
              ┌─────▼─────┐    ┌─────▼──────┐    ┌─────▼─────┐
              │   Redis    │    │   Redis    │    │   MySQL   │
              │  (single   │    │  + FT idx  │    │ StateEntry│
              │ connection)│    │            │    │  table    │
              └────────────┘    └────────────┘    └───────────┘
```

---

## Stubs & Unimplemented Features

1. **KeyCount in ListStores**: Always returns null. Would require DBSIZE for Redis or COUNT for MySQL.
2. **DefaultConsistency**: Config property exists but consistency mode is not evaluated anywhere.
3. **Metrics/Tracing**: Config flags exist but no instrumentation implemented.
4. **State change events**: Considered but rejected as too expensive per-operation.

---

## Potential Extensions

1. **Store-level metrics**: Track operation counts, latencies per store for capacity planning.
2. **TTL support for MySQL**: Currently Redis-only. MySQL stores never expire data.
3. **Bulk save operation**: Currently only bulk-get exists. Bulk save would help seed operations.
4. **Store migration tooling**: Move data between Redis and MySQL backends without downtime.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Sync-over-async in GetStore()**: `StateStoreFactory.GetStore<T>()` calls `.GetAwaiter().GetResult()` if initialization hasn't completed. This supports services that call GetStore in constructors but can deadlock in async contexts. Prefer `GetStoreAsync<T>()`.

2. **ETag format inconsistency**: Redis uses a `long` version counter as ETag. MySQL uses `SHA256(json)[0:12]` (base64). Same logical concept, different formats across backends.

3. **Per-operation DbContext creation**: `MySqlStateStore` creates a fresh `StateDbContext` per operation to avoid EF Core change-tracking concurrency issues. Adds overhead but ensures thread safety.

4. **Shared static stores in InMemory mode**: `InMemoryStateStore` uses static `ConcurrentDictionary` instances. `GetStore<TypeA>("store")` and `GetStore<TypeB>("store")` see the same underlying data. Enables cross-type access but can cause test pollution.

5. **RedisSearch fallback to string**: `RedisSearchStateStore` accepts both JSON objects and raw strings for backwards compatibility. If a value was stored as string before search was enabled, it's still readable.

6. **FT index creation on startup**: `StateStoreFactory.InitializeAsync()` creates full-text indexes for all stores with `enableSearch=true`. Uses `TextField("$.*", "content")` to index all JSON fields. Skips if index already exists.

7. **MySQL JSON query operators**: `Contains` uses `LIKE %value%`, `FullText` also uses `LIKE %value%`. These are simplified implementations, not true full-text search on MySQL.

### Design Considerations (Requires Planning)

1. **Three unused config properties**: `DefaultConsistency`, `EnableMetrics`, `EnableTracing` are defined but never evaluated. Should be wired up or removed per IMPLEMENTATION TENETS.

2. **MySQL query loads all into memory**: `JsonQueryPagedAsync` builds SQL with WHERE/ORDER BY/LIMIT but the result mapping still deserializes all matching rows. For very large result sets this could be memory-intensive.

3. **No store-level access control**: Any service can access any store via `IStateStoreFactory.GetStore<T>(anyName)`. No enforcement of store ownership. Relies on convention (services only access their own stores).

4. **Set operation support varies by backend**: Redis supports sets and sorted sets. MySQL throws `NotSupportedException`. InMemory supports sets but not sorted sets. Services must know their backend to use these features.

5. **Connection initialization retry**: MySQL initialization retries up to `ConnectionRetryCount` times with configurable delay. Redis initialization does not retry (relies on StackExchange.Redis auto-reconnect).

---

## Tenet Violations (Audit)

*Audit performed: 2026-01-24*

### Category: IMPLEMENTATION

1. **Configuration-First Development (T21)** - `schemas/state-configuration.yaml:42-57` - Three unused configuration properties
   - What's wrong: `DefaultConsistency`, `EnableMetrics`, and `EnableTracing` are defined in the configuration schema but never referenced in service code. T21 states "Every defined config property MUST be referenced in service code" and "No Dead Configuration".
   - Fix: Either wire up these configuration properties to actual functionality (implement metrics/tracing, implement consistency mode evaluation) OR remove them from `schemas/state-configuration.yaml`.

2. **Configuration-First Development (T21)** - `Services/StateStoreFactory.cs:119-122` - Hardcoded retry delay calculation
   - What's wrong: The retry delay is calculated as `Math.Max(1000, (totalTimeoutSeconds * 1000) / Math.Max(1, maxRetries))` using a hardcoded minimum of 1000ms. T21 states "Any tunable value (limits, timeouts, thresholds, capacities) MUST be a configuration property."
   - Fix: Add `MinRetryDelayMs` configuration property to `state-configuration.yaml` and use it instead of hardcoded `1000`.

3. **Configuration-First Development (T21)** - `Services/StateStoreFactory.cs:40` - Hardcoded ConnectionRetryCount default
   - What's wrong: `ConnectionRetryCount` has a hardcoded default of `10` in `StateStoreFactoryConfiguration` class but this property is not exposed in the configuration schema. This should be a configurable value.
   - Fix: Add `ConnectionRetryCount` to `schemas/state-configuration.yaml` with appropriate env var `STATE_CONNECTION_RETRY_COUNT`.

4. **Async Method Pattern (T23)** - `Services/InMemoryStateStore.cs:93-111` - Non-async method body with `await Task.CompletedTask`
   - What's wrong: The `GetAsync` method uses `await Task.CompletedTask` at line 95 but then has no other await, then returns directly. This is acceptable but could be cleaner.
   - Fix: This is borderline - the pattern is allowed per T23 for synchronous implementations of async interfaces. No action required.

5. **Async Method Pattern (T23)** - `Services/InMemoryStateStore.cs:136-169` - Redundant `await Task.CompletedTask` placement
   - What's wrong: In `SaveAsync`, the `await Task.CompletedTask` at line 167 comes after the return statement logic is complete but before the actual return. The await should be at the end of the method body for clarity, which it is here. This is acceptable.
   - Fix: No action required - pattern is correct.

6. **Error Handling (T7)** - `Services/RedisDistributedLockProvider.cs:98-102` - Generic exception catch without error event publishing
   - What's wrong: The `LockAsync` method catches `Exception ex` at line 98 but only logs it without calling `TryPublishErrorAsync`. T7 states "Unexpected error - log as error, emit error event".
   - Fix: Inject `IMessageBus` into `RedisDistributedLockProvider` and call `TryPublishErrorAsync` in the catch block.

7. **Error Handling (T7)** - `Services/StateStoreFactory.cs:467-470` - Warning log for search index creation failure
   - What's wrong: At line 469, `LogWarning` is used for a failure that indicates an unexpected condition (search index creation failure). T7 states "Warning vs Error Log Levels: LogError for unexpected failures".
   - Fix: Change to `LogError` since failed index creation is an unexpected infrastructure issue, not an expected transient failure.

8. **Logging Standards (T10)** - `Services/RedisSearchStateStore.cs:451-452` - Operation entry logged at Information level
   - What's wrong: At line 451, `LogInformation` is used for "FT.SEARCH on '{Index}' with query '{Query}' found {TotalResults} documents" which is operation entry/debugging output. T10 states "Operation Entry (Debug)".
   - Fix: Change to `LogDebug` for consistency - search results are routine operational data, not significant state changes.

### Category: FOUNDATION

9. **Service Implementation Pattern (T6)** - `StateService.cs:24-34` - Missing IEventConsumer in constructor
   - What's wrong: The service constructor does not accept or call `RegisterEventConsumers(eventConsumer)`. While the deep-dive document notes this service does not consume events, the standardized pattern in T6 shows `IEventConsumer` as a common dependency for consistency.
   - Fix: This is acceptable - T6 notes ServiceEvents.cs is OPTIONAL for services that don't subscribe to events. The State service intentionally does not consume events. No action required.

10. **Infrastructure Libs Pattern (T4)** - `Services/RedisDistributedLockProvider.cs:50` - Direct Redis connection
    - What's wrong: At line 50, `ConnectionMultiplexer.ConnectAsync` is used directly. T4 states "Direct Redis/MySQL connection - Use lib-state".
    - Fix: This is an **ALLOWED EXCEPTION** - `lib-state` IS the infrastructure lib for Redis access. The lock provider is part of lib-state itself, so it must use direct Redis connections. The tenet applies to service code, not infrastructure lib internals. No action required.

11. **Infrastructure Libs Pattern (T4)** - `Services/StateStoreFactory.cs:109` - Direct Redis connection
    - What's wrong: At line 109, `ConnectionMultiplexer.ConnectAsync` is used directly.
    - Fix: This is an **ALLOWED EXCEPTION** - same as above. `StateStoreFactory` is part of lib-state which is the infrastructure lib. No action required.

### Category: QUALITY

12. **Logging Standards (T10)** - `StateServicePlugin.cs:42` - Operation entry logged at Information level
    - What's wrong: At line 42, `LogInformation("Initializing StateStoreFactory connections...")` is used for operation entry. T10 states "Operation Entry (Debug): Log input parameters".
    - Fix: Change to `LogDebug` since this is initialization startup logging, not a significant business state change.

13. **Logging Standards (T10)** - `StateServicePlugin.cs:44` - Operation success logged at Information level
    - What's wrong: At line 44, `LogInformation("StateStoreFactory initialized successfully")` is used. This is acceptable as initialization success is a significant state change (Business Decisions).
    - Fix: No action required - this is appropriate for Information level.

14. **Logging Standards (T10)** - `Services/RedisDistributedLockProvider.cs:49-50` - Operation entry at Information level
    - What's wrong: At lines 49-50, `LogInformation("Initializing Redis connection for distributed locks")` and `LogInformation("Redis connection established...")` are used. Line 49 is operation entry (should be Debug), line 50 is significant state change (appropriate for Information).
    - Fix: Change line 49 to `LogDebug`.

15. **Logging Standards (T10)** - `Services/StateStoreFactory.cs:107-108` - Operation entry at Information level
    - What's wrong: At line 107, `LogInformation("Connecting to Redis...")` is operation entry which should be Debug per T10.
    - Fix: Change to `LogDebug`.

16. **Logging Standards (T10)** - `Services/StateStoreFactory.cs:123-125` - Operation entry at Information level
    - What's wrong: At line 123, `LogInformation("Initializing MySQL connection...")` is operation entry which should be Debug per T10.
    - Fix: Change to `LogDebug`.

17. **Logging Standards (T10)** - `Services/StateStoreFactory.cs:147` - Business decision at Information level
    - What's wrong: At line 147, `LogInformation("MySQL connection established successfully")` is appropriate - this is a significant state change (Business Decision).
    - Fix: No action required.

18. **Logging Standards (T10)** - `Services/StateStoreFactory.cs:170` - Business decision at Information level
    - What's wrong: At line 170, `LogInformation("State store factory initialized...")` is appropriate - significant state change.
    - Fix: No action required.

19. **Logging Standards (T10)** - `Services/RedisSearchStateStore.cs:370` - Business decision at Information level
    - What's wrong: At line 370, `LogInformation("Created search index...")` is appropriate - significant state change (index creation).
    - Fix: No action required.

20. **XML Documentation (T19)** - `Services/StateStoreFactoryConfiguration.cs:50` - Missing documentation on Stores property
    - What's wrong: The `Stores` dictionary property at line 50 has XML documentation, but this is a manually-written class (not generated) in a non-Generated location.
    - Fix: No action required - documentation is present.

### Summary

**Violations requiring fixes:**
1. T21 - Three unused config properties (DefaultConsistency, EnableMetrics, EnableTracing)
2. T21 - Hardcoded retry delay minimum (1000ms)
3. T21 - ConnectionRetryCount not in configuration schema
4. T7 - Missing TryPublishErrorAsync in RedisDistributedLockProvider.LockAsync
5. T7 - Wrong log level (Warning -> Error) for search index creation failure
6. T10 - Wrong log level (Information -> Debug) in RedisSearchStateStore.SearchAsync
7. T10 - Wrong log level (Information -> Debug) in StateServicePlugin.OnInitializeAsync
8. T10 - Wrong log level (Information -> Debug) in RedisDistributedLockProvider.EnsureInitializedAsync
9. T10 - Wrong log level (Information -> Debug) in StateStoreFactory.EnsureInitializedAsync (Redis)
10. T10 - Wrong log level (Information -> Debug) in StateStoreFactory.EnsureInitializedAsync (MySQL)

**Allowed exceptions (no fix required):**
- T4 violations in lib-state internals (RedisDistributedLockProvider, StateStoreFactory) - lib-state IS the infrastructure lib
- T6 missing IEventConsumer - State service does not consume events, ServiceEvents.cs is optional
- T23 async patterns - implementation is correct per tenet guidelines
