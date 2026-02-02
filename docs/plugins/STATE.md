# State Plugin Deep Dive

> **Plugin**: lib-state
> **Schema**: schemas/state-api.yaml
> **Version**: 1.0.0
> **State Store**: Self (manages all state stores for the platform)

---

## Overview

The State service is the infrastructure abstraction layer that provides all Bannou services with access to Redis and MySQL backends through a unified API. It operates in a dual role: (1) as the `IStateStoreFactory` infrastructure library used by all services for state persistence, and (2) as an HTTP API providing direct state access for debugging and administration. Supports Redis (ephemeral/session data), MySQL (durable/queryable data), and InMemory (testing) backends with optimistic concurrency via ETags, TTL support, sorted sets, and JSON path queries.

### Interface Hierarchy (as of 2026-02-01)

```
IStateStore<T>                    - Core CRUD (all backends)
├── ICacheableStateStore<T>       - Sets + Sorted Sets (Redis + InMemory)
├── IQueryableStateStore<T>       - LINQ queries (MySQL only)
│   └── IJsonQueryableStateStore<T> - JSON path queries (MySQL only)
└── ISearchableStateStore<T>      - Full-text search (Redis+Search only)

IRedisOperations                  - Low-level Redis access (Lua scripts, hashes, atomic counters)
```

**Backend Support Matrix**:

| Interface | Redis | MySQL | InMemory | RedisSearch |
|-----------|:-----:|:-----:|:--------:|:-----------:|
| `IStateStore<T>` | ✅ | ✅ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sets) | ✅ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sorted Sets) | ✅ | ❌ | ✅ | ❌* |
| `IQueryableStateStore<T>` | ❌ | ✅ | ❌ | ❌ |
| `IJsonQueryableStateStore<T>` | ❌ | ✅ | ❌ | ❌ |
| `ISearchableStateStore<T>` | ❌ | ❌ | ❌ | ✅ |
| `IRedisOperations` | ✅ | ❌ | ❌ | ❌ |

\* RedisSearchStateStore implements `ICacheableStateStore<T>` but throws `NotSupportedException` for all sorted set operations. This is because JSON storage mode required for RedisSearch indexing is incompatible with sorted set operations. Use `RedisStateStore` for stores requiring both sorted sets and caching.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| StackExchange.Redis 2.10.1 | Redis connection multiplexing and operations |
| NRedisStack 0.13.1 | Redis JSON and search (FT) commands |
| Pomelo.EntityFrameworkCore.MySql 9.0.0 | MySQL via EF Core |
| Microsoft.EntityFrameworkCore 9.0.0 | ORM and change tracking |
| lib-messaging (`IMessageBus`) | Error event publishing via `TryPublishErrorAsync` |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| Every service | Uses `IStateStoreFactory.GetStore<T>()` for typed state access |
| lib-leaderboard | Uses sorted set operations for rankings |
| lib-permission | Uses Redis store with distributed locks via `IDistributedLockProvider` |
| lib-asset | Uses Redis for uploads, MySQL for metadata |
| lib-currency | Uses Redis cache with MySQL backing for wallets/holds |
| lib-inventory | Uses Redis cache with MySQL backing for containers |

All services depend on state infrastructure. The HTTP API (`IStateClient`) is used for debugging/admin only.

---

## State Storage

**Self-managed**: This plugin defines and manages all state stores across Bannou.

**Store Registry**: `schemas/state-stores.yaml` (~35 stores)

### Backend Distribution

| Backend | Count | Use Case |
|---------|-------|----------|
| Redis | ~10 | Sessions, caches, ephemeral state, leaderboards |
| MySQL | ~25 | Durable entity data, queryable records |
| Memory | 0 (runtime only) | Testing with `UseInMemory=true` |

### Key Structure (Redis)

| Pattern | Purpose |
|---------|---------|
| `{prefix}:{key}` | Primary value storage (STRING or JSON depending on store type) |
| `{prefix}:{key}:meta` | Hash with `version` and `updated` timestamp |
| `{prefix}:set:{key}` | Set members (SET type) |
| `{prefix}:zset:{key}` | Sorted set for rankings (ZSET type) |
| `{storeName}-idx` | FT search index (auto-created for EnableSearch stores) |

### Key Structure (MySQL)

| Column | Type | Purpose |
|--------|------|---------|
| `StoreName` | VARCHAR(255) | Part of composite PK |
| `Key` | VARCHAR(255) | Part of composite PK |
| `ValueJson` | LONGTEXT | JSON-serialized value |
| `ETag` | VARCHAR(64) | SHA256[0:12] base64 hash |
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
| `ConnectionRetryCount` | `STATE_CONNECTION_RETRY_COUNT` | `10` | Max MySQL connection retry attempts |
| `MinRetryDelayMs` | `STATE_MIN_RETRY_DELAY_MS` | `1000` | Min delay between MySQL retry attempts |

**Note:** `DefaultConsistency`, `EnableMetrics`, and `EnableTracing` were removed as dead config. Telemetry is now controlled centrally via lib-telemetry. Consistency is specified per-request via `StateOptions.Consistency`.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `IStateStoreFactory` | Singleton | Creates typed store instances, manages connections |
| `StateStoreFactory` | Singleton | Implementation with Redis/MySQL initialization and store caching |
| `IDistributedLockProvider` | Singleton | Distributed mutex using Redis (SET NX EX) with InMemory fallback |
| `RedisDistributedLockProvider` | Singleton | Uses `IRedisOperations` for Lua-based safe unlock, falls back to in-memory locks |
| `IRedisOperations` | - | Low-level Redis ops (Lua scripts, hashes, counters); obtained via `GetRedisOperations()` |
| `RedisOperations` | Internal | Shares `ConnectionMultiplexer` with state stores |
| `RedisLuaScripts` | Static | Loads and caches Lua scripts from embedded resources (`Scripts/*.lua`) |
| `StateService` | Scoped | HTTP API implementation |

### Factory Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `GetStore<T>(name)` | `IStateStore<T>` | Basic CRUD operations (all backends) |
| `GetStoreAsync<T>(name)` | `IStateStore<T>` | Async version, avoids sync-over-async |
| `GetCacheableStore<T>(name)` | `ICacheableStateStore<T>` | Set + Sorted Set ops (throws for MySQL) |
| `GetCacheableStoreAsync<T>(name)` | `ICacheableStateStore<T>` | Async version |
| `GetSearchableStore<T>(name)` | `ISearchableStateStore<T>` | Full-text search (RedisSearch only) |
| `GetRedisOperations()` | `IRedisOperations?` | Low-level Redis; null when `UseInMemory=true` |

### Store Implementation Classes

| Class | Backend | Implements | Features |
|-------|---------|------------|----------|
| `RedisStateStore<T>` | Redis | `ICacheableStateStore<T>` | String ops, sets, sorted sets, TTL, transactions |
| `RedisSearchStateStore<T>` | Redis | `ICacheableStateStore<T>`, `ISearchableStateStore<T>` | JSON storage, FT search, sets only (sorted sets throw NotSupportedException) |
| `MySqlStateStore<T>` | MySQL | `IQueryableStateStore<T>`, `IJsonQueryableStateStore<T>` | EF Core, JSON path queries (no sets/sorted sets) |
| `InMemoryStateStore<T>` | Memory | `ICacheableStateStore<T>` | Static shared stores, sets, sorted sets, TTL via lazy cleanup |

---

## API Endpoints (Implementation Notes)

### Get (`/state/get`)

Returns value with ETag for concurrent-safe retrieval. Uses `GetWithETagAsync()` internally. Returns NotFound if store doesn't exist or key not found. Includes empty `StateMetadata` in response (timestamps not populated).

### Save (`/state/save`)

Supports optimistic concurrency via ETag in `StateOptions`. If ETag provided and mismatches, returns 409 Conflict via `TrySaveAsync()`. TTL support for Redis stores. Uses Redis transactions for atomicity in `RedisStateStore`.

### Delete (`/state/delete`)

Boolean response indicates deletion success. Returns false if key not found. Deletes both value and metadata keys in Redis.

### Query (`/state/query`)

Routes to MySQL for conditions-based queries (JSON path expressions) or Redis Search if enabled. Returns BadRequest if Redis search not supported for the store. MySQL uses `JSON_EXTRACT`, `JSON_UNQUOTE`, and `JSON_CONTAINS_PATH` functions. Supports operators: equals, notEquals, greaterThan, lessThan, contains, startsWith, endsWith, in, exists, notExists, fullText.

### Bulk Get (`/state/bulk-get`)

Uses `GetBulkAsync()` for efficient multi-key retrieval. Returns per-key `Found` flags. Redis uses MGET; MySQL uses multi-key IN query.

### List Stores (`/state/list-stores`)

Returns registered store names with backend info. Optional backend filter (Redis/MySQL). `KeyCount` always null (not implemented).

---

## Visual Aid

```
State Store Architecture (Interface Hierarchy)
==============================================

  Service Code                     StateStoreFactory
  ============                     ================

  GetStore<T>()         ─────────► IStateStore<T>          (all backends)
  GetCacheableStore<T>() ────────► ICacheableStateStore<T> (Redis/Memory)
  GetSearchableStore<T>() ───────► ISearchableStateStore<T>(RedisSearch)
  GetRedisOperations()   ────────► IRedisOperations?       (Redis only)

  Interface Hierarchy:
  ===================

    IStateStore<T>  ◄────────────── Core CRUD (all backends)
         │
         ├── ICacheableStateStore<T>  ◄── Sets + Sorted Sets
         │        │                        (Redis, InMemory; RedisSearch: Sets only)
         │        ├── RedisStateStore<T>     (full support)
         │        ├── RedisSearchStateStore<T> (Sets only - Sorted Sets throw)
         │        └── InMemoryStateStore<T>  (full support)
         │
         ├── IQueryableStateStore<T>  ◄── LINQ queries
         │        │                        (MySQL only)
         │        └── IJsonQueryableStateStore<T>
         │                 └── MySqlStateStore<T>
         │
         └── ISearchableStateStore<T> ◄── Full-text search
                  └── RedisSearchStateStore<T>  (Redis+FT only)

    IRedisOperations  ◄────────────── Lua scripts, hashes, counters
         └── RedisOperations            (Redis only, null if InMemory)

  Backend Layout:
  ===============

    ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
    │ RedisStateStore │    │RedisSearchStore │    │  MySqlStateStore│
    │ + ICacheable    │    │ + ICacheable*   │    │ + IQueryable    │
    │ (Sets+ZSets)    │    │ + ISearchable   │    │ + IJsonQueryable│
    └────────┬────────┘    └────────┬────────┘    └────────┬────────┘
                           * Sets only; ZSets throw NotSupportedException
             │                      │                      │
    ┌────────▼────────┐    ┌────────▼────────┐    ┌────────▼────────┐
    │     Redis       │    │   Redis + FT    │    │      MySQL      │
    │  (String/Sets)  │    │   (JSON/Index)  │    │   (StateEntry)  │
    └─────────────────┘    └─────────────────┘    └─────────────────┘
```

---

## Stubs & Unimplemented Features

1. **KeyCount in ListStores**: Always returns null. Would require DBSIZE for Redis or COUNT for MySQL.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/174 -->
2. ~~**DefaultConsistency**~~: **FIXED** (2026-02-01) - Removed as dead config per IMPLEMENTATION TENETS (T21). Consistency is specified per-request via `StateOptions.Consistency`, not as a global default. The `ConsistencyLevel` enum remains available for per-request use.
3. ~~**Metrics/Tracing**~~: **FIXED** (2026-01-31) - `EnableMetrics` and `EnableTracing` config flags removed. Telemetry is now controlled centrally via lib-telemetry plugin. See Potential Extensions #1 for details.
4. **StateMetadata population**: `GetStateResponse.Metadata` returns empty object; timestamps not retrieved from backend.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/177 -->

---

## Potential Extensions

1. ~~**Store-level metrics**~~: **IMPLEMENTED** (2026-01-31) - Addressed by lib-telemetry plugin (#180). `InstrumentedStateStore<T>` wrappers record operation counts (`bannou.state.operations`), latencies (`bannou.state.duration`), and OpenTelemetry tracing spans per store/operation/backend. Enabled via `TELEMETRY_TRACING_ENABLED` and `TELEMETRY_METRICS_ENABLED`.
2. **TTL support for MySQL**: Currently Redis-only. MySQL stores never expire data.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/182 -->
3. ~~**Bulk save operation**~~: **IMPLEMENTED** - `SaveBulkAsync()` exists on `IStateStore<T>` and is exposed via `/state/bulk-save` endpoint.
4. **Store migration tooling**: Move data between Redis and MySQL backends without downtime.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/190 -->
5. **Prefix query support**: Add SCAN-based prefix queries for Redis (with careful iteration limits).
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/194 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**RedisSearchStateStore.TrySaveAsync broken transaction**~~: **FIXED** (2026-01-31) - Replaced broken transaction pattern with Lua scripts (`TryCreate.lua` and `TryUpdate.lua` via `RedisLuaScripts`) that atomically check version and perform JSON.SET + metadata update. The `TryCreate` script handles empty ETag (create-if-not-exists) semantics, while `TryUpdate` handles optimistic concurrency updates.

### Intentional Quirks (Documented Behavior)

1. **Sync-over-async in GetStore()**: `StateStoreFactory.GetStore<T>()` calls `.GetAwaiter().GetResult()` if initialization hasn't completed. This supports services that call GetStore in constructors but can deadlock in async contexts. A warning is logged when this occurs. Prefer `GetStoreAsync<T>()` or call `InitializeAsync()` at startup.

2. **ETag format inconsistency**: Redis uses a `long` version counter as ETag (incremented on each save). MySQL uses `SHA256(json)[0:12]` (base64). Same logical concept, different formats across backends. Services should treat ETags as opaque strings.

3. **Shared static stores in InMemory mode**: `InMemoryStateStore` uses static `ConcurrentDictionary` instances keyed by store name. `GetStore<TypeA>("store")` and `GetStore<TypeB>("store")` see the same underlying data (serialized as JSON). Enables cross-type access but can cause test pollution if not cleared between tests.

4. **MySQL JSON query operators**: `Contains` and `FullText` both use `LIKE %value%`. These are simplified implementations, not true full-text search on MySQL.

5. **TrySaveAsync empty ETag semantics differ by backend**: In Redis and MySQL, empty ETag means "create new entry if it doesn't exist" with atomic conflict detection. In InMemoryStateStore, TrySaveAsync requires a valid version number as ETag; empty or non-numeric ETags return null immediately (no create-on-empty semantics). Use `SaveAsync` for initial creation in InMemory mode.

6. **RedisSearchStateStore falls back to string storage**: The `GetAsync` and `GetWithETagAsync` methods catch `WRONGTYPE` errors and fall back to `StringGet` for backwards compatibility with keys stored as strings before search was enabled.

7. **No state change events**: The State service intentionally does not publish lifecycle events (StateChanged, StoreMigration, StoreHealth) for state mutations. This was a deliberate design decision because publishing events for every save/delete operation would be prohibitively expensive given the high operation volume across all services. Error events are still published via `TryPublishErrorAsync` for operational visibility. See `schemas/state-events.yaml` for documentation of this decision.

8. **No store-level access control**: Any service can access any store via `IStateStoreFactory.GetStore<T>(anyName)`. This is intentional - enforcement was considered and rejected for these reasons:
   - All services are in the same trust boundary (same codebase, same deployment)
   - Adding access control would require ambient context or passed parameters to identify the "current service" at call time
   - Significant performance overhead for every state store access
   - Would break the clean DI pattern where services just inject `IStateStoreFactory`
   - The generated `StateStoreDefinitions` constants guide correct usage via convention
   - This is internal infrastructure, not an external API requiring authorization

9. **Asymmetric connection retry between MySQL and Redis**: MySQL initialization retries up to `ConnectionRetryCount` times with configurable delay. Redis initialization does not retry. This is intentional: StackExchange.Redis has built-in auto-reconnect functionality that handles connection failures and reconnection automatically. EF Core/MySQL does not have this capability, so explicit retry logic is required for MySQL only.

10. **RedisSearchStateStore sorted set operations throw NotSupportedException**: Although `RedisSearchStateStore` implements `ICacheableStateStore<T>`, all 10 sorted set methods (`SortedSetAddAsync`, `SortedSetRemoveAsync`, etc.) throw `NotSupportedException`. This is because RedisSearch requires JSON storage mode (`JSON.SET`), which is incompatible with Redis sorted set commands (`ZADD`, etc.). Set operations (non-sorted) work correctly because they can operate on JSON-serialized string members. Services requiring both full-text search and sorted sets must use separate stores.

### Design Considerations (Requires Planning)

1. ~~**Unused config properties**~~: **FIXED** (2026-02-01) - All three properties (`DefaultConsistency`, `EnableMetrics`, `EnableTracing`) removed from schema as dead config. `DefaultConsistency` removed in this audit; `EnableMetrics`/`EnableTracing` removed previously when telemetry was centralized to lib-telemetry.

2. **MySQL query loads all into memory**: `QueryAsync` and `QueryPagedAsync` in MySqlStateStore load all entries from the store, deserialize each one, then filter in memory. For very large stores this could be memory-intensive. Consider SQL-level filtering.
   <!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/251 -->

3. ~~**No store-level access control**~~: **MOVED TO QUIRKS** (2026-02-02) - Intentional design decision. See Intentional Quirks #8.

4. ~~**Set and sorted set operation support varies by backend**~~: **FIXED** (2026-02-01) - Set and Sorted Set operations are now consolidated in `ICacheableStateStore<T>` interface. Services call `GetCacheableStore<T>()` for stores needing these operations. MySQL throws `InvalidOperationException` at factory call time (compile-time safety via interface segregation). Redis, RedisSearch, and InMemory all support the full `ICacheableStateStore<T>` interface including sorted sets.

5. ~~**Connection initialization retry**~~: **MOVED TO QUIRKS** (2026-02-02) - Intentional asymmetry. See Intentional Quirks #9.

6. ~~**Hardcoded retry delay minimum**~~: **FIXED** (2026-02-02) - Added `MinRetryDelayMs` to `state-configuration.yaml` (env: `STATE_MIN_RETRY_DELAY_MS`, default: 1000). The retry delay calculation now uses this configurable value.

7. ~~**ConnectionRetryCount not in schema**~~: **FIXED** (2026-02-02) - Added `ConnectionRetryCount` to `state-configuration.yaml` (env: `STATE_CONNECTION_RETRY_COUNT`, default: 10).

8. ~~**RedisDistributedLockProvider direct Redis connection**~~: **FIXED** (2026-02-01) - `RedisDistributedLockProvider` now uses `IStateStoreFactory.GetRedisOperations()` instead of managing its own `ConnectionMultiplexer`. When Redis is unavailable (`UseInMemory=true`), it falls back to an in-memory lock implementation using `ConcurrentDictionary` with TTL-based expiration. Error events are still not published (infrastructure libs avoid event publishing to prevent circular dependencies).

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Completed

- **2026-01-31**: Fixed `RedisSearchStateStore.TrySaveAsync` broken transaction. The original code created a Redis transaction with a condition but executed it with `FireAndForget`, then performed the actual JSON.SET outside the transaction. Replaced with two Lua scripts: `TryCreate.lua` (for empty ETag create-if-not-exists) and `TryUpdate.lua` (for optimistic concurrency updates), loaded via `RedisLuaScripts` class from embedded resources. Lua scripts execute atomically on Redis server, ensuring proper concurrency control for JSON document storage.
- **2026-01-31**: Moved "State change events" from Stubs & Unimplemented Features to Intentional Quirks. This was incorrectly categorized as a gap when it's actually a documented design decision. The decision to not publish state change events was intentional due to performance concerns (high operation volume would make per-operation event publishing prohibitively expensive).
- **2026-01-31**: Marked "Store-level metrics" Potential Extension as IMPLEMENTED. lib-telemetry (#180) provides `InstrumentedStateStore<T>` wrappers that record operation counts, latencies, and tracing spans. Also marked "Bulk save operation" as IMPLEMENTED since `SaveBulkAsync()` already exists.
- **2026-02-01**: Removed `DefaultConsistency` from `state-configuration.yaml` as dead config per IMPLEMENTATION TENETS (T21). The property was defined but never evaluated - consistency is specified per-request via `StateOptions.Consistency`. Also updated docs to reflect that `EnableMetrics`/`EnableTracing` were previously removed when telemetry was centralized to lib-telemetry.
- **2026-02-01**: **Interface Hierarchy Cleanup (#255)** - Major refactoring to clarify backend-specific interface support:
  - Added `IRedisOperations` interface for low-level Redis access (Lua scripts, hash operations, atomic counters, TTL manipulation)
  - Added `ICacheableStateStore<T>` interface consolidating Set and Sorted Set operations (Redis + InMemory only)
  - Added Sorted Set support to `InMemoryStateStore` for full testability without Redis
  - Migrated `RedisDistributedLockProvider` to use `IRedisOperations` with in-memory fallback
  - Updated all services using Set/Sorted Set operations to call `GetCacheableStore<T>()` instead of `GetStore<T>()`
  - Added `InstrumentedCacheableStateStore<T>` telemetry decorator
  - MySQL backend now throws `InvalidOperationException` at factory call time rather than runtime `NotSupportedException`
- **2026-02-02**: Reclassified "No store-level access control" from Design Considerations to Intentional Quirks. Investigation confirmed this is an intentional design decision: all services are in the same trust boundary, enforcement would add performance overhead and complexity, and the `StateStoreDefinitions` constants provide convention-based guidance.
- **2026-02-02**: Reclassified "Connection initialization retry asymmetry" from Design Considerations to Intentional Quirks. StackExchange.Redis has built-in auto-reconnect; EF Core/MySQL does not. The asymmetry is by design.
- **2026-02-02**: Added `ConnectionRetryCount` and `MinRetryDelayMs` to `state-configuration.yaml`. Both were previously hardcoded in `StateStoreFactoryConfiguration`. Now configurable via `STATE_CONNECTION_RETRY_COUNT` (default: 10) and `STATE_MIN_RETRY_DELAY_MS` (default: 1000).
- **2026-02-02**: Documentation maintenance - corrected several inaccuracies:
  - Fixed backend support matrix: `RedisSearchStateStore` does NOT support sorted set operations (throws `NotSupportedException` for all 10 sorted set methods)
  - Updated Store Implementation Classes table to clarify RedisSearchStateStore supports "sets only (sorted sets throw NotSupportedException)"
  - Added Intentional Quirk #10 documenting RedisSearchStateStore sorted set limitation (JSON storage incompatible with ZADD commands)
  - Fixed Lua script naming: corrected `TryCreateScript`/`TryUpdateScript` to actual names `TryCreate.lua`/`TryUpdate.lua` (via `RedisLuaScripts` class)
  - Clarified InMemory TrySaveAsync semantics: requires valid numeric ETag, empty/non-numeric ETags return null immediately
  - Added `RedisLuaScripts` to DI Services table
  - Updated visual aid diagram with sorted set support annotations
