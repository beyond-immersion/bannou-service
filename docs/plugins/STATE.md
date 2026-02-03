# State Plugin Deep Dive

> **Plugin**: lib-state
> **Schema**: schemas/state-api.yaml
> **Version**: 1.0.0
> **State Store**: Self (manages all state stores for the platform)

---

## Overview

The State service is the infrastructure abstraction layer that provides all Bannou services with access to Redis and MySQL backends through a unified API. It operates in a dual role: (1) as the `IStateStoreFactory` infrastructure library used by all services for state persistence, and (2) as an HTTP API providing direct state access for debugging and administration. Supports Redis (ephemeral/session data), MySQL (durable/queryable data), and InMemory (testing) backends with optimistic concurrency via ETags, TTL support, sorted sets, and JSON path queries.

### Interface Hierarchy (as of 2026-02-03)

```
IStateStore<T>                    - Core CRUD (all backends)
├── ICacheableStateStore<T>       - Sets, Sorted Sets, Counters, Hashes (Redis + InMemory)
│   └── ISearchableStateStore<T>  - Full-text search (extends Cacheable)
├── IQueryableStateStore<T>       - LINQ queries (MySQL only)
│   └── IJsonQueryableStateStore<T> - JSON path queries (MySQL only)

IRedisOperations                  - Low-level Redis access (Lua scripts, transactions)
```

**Key Design**: `ISearchableStateStore<T>` extends `ICacheableStateStore<T>` because all searchable stores are Redis-based and therefore support all cacheable operations (sets, sorted sets, counters, hashes). This ensures proper telemetry instrumentation for all operations when using searchable stores.

**Backend Support Matrix**:

| Interface | Redis | MySQL | InMemory | RedisSearch |
|-----------|:-----:|:-----:|:--------:|:-----------:|
| `IStateStore<T>` | ✅ | ✅ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sets) | ✅ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sorted Sets) | ✅ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Counters) | ✅ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Hashes) | ✅ | ❌ | ✅ | ✅ |
| `IQueryableStateStore<T>` | ❌ | ✅ | ❌ | ❌ |
| `IJsonQueryableStateStore<T>` | ❌ | ✅ | ❌ | ❌ |
| `ISearchableStateStore<T>` | ❌ | ❌ | ❌ | ✅ |
| `IRedisOperations` | ✅ | ❌ | ❌ | ❌ |

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
| `{prefix}:counter:{key}` | Atomic counter (STRING with INCR/DECR) |
| `{prefix}:hash:{key}` | Hash for field-value pairs (HASH type) |
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
| `IRedisOperations` | - | Low-level Redis ops (Lua scripts, transactions only); obtained via `GetRedisOperations()` |
| `RedisOperations` | Internal | Shares `ConnectionMultiplexer` with state stores |
| `RedisLuaScripts` | Static | Loads and caches Lua scripts from embedded resources (`Scripts/*.lua`) |
| `StateService` | Scoped | HTTP API implementation |

### Factory Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `GetStore<T>(name)` | `IStateStore<T>` | Basic CRUD operations (all backends) |
| `GetStoreAsync<T>(name)` | `IStateStore<T>` | Async version, avoids sync-over-async |
| `GetCacheableStore<T>(name)` | `ICacheableStateStore<T>` | Set, Sorted Set, Counter, Hash ops (throws for MySQL) |
| `GetCacheableStoreAsync<T>(name)` | `ICacheableStateStore<T>` | Async version |
| `GetSearchableStore<T>(name)` | `ISearchableStateStore<T>` | Full-text search (RedisSearch only) |
| `GetRedisOperations()` | `IRedisOperations?` | Lua scripts, transactions only; null when `UseInMemory=true` |

### Store Implementation Classes

| Class | Backend | Implements | Features |
|-------|---------|------------|----------|
| `RedisStateStore<T>` | Redis | `ICacheableStateStore<T>` | String ops, sets, sorted sets, counters, hashes, TTL |
| `RedisSearchStateStore<T>` | Redis | `ICacheableStateStore<T>`, `ISearchableStateStore<T>` | JSON storage, FT search, sets, sorted sets, counters, hashes |
| `MySqlStateStore<T>` | MySQL | `IQueryableStateStore<T>`, `IJsonQueryableStateStore<T>` | EF Core, JSON path queries (no sets/sorted sets/counters/hashes) |
| `InMemoryStateStore<T>` | Memory | `ICacheableStateStore<T>` | Static shared stores, sets, sorted sets, counters, hashes, TTL via lazy cleanup |

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
         ├── ICacheableStateStore<T>  ◄── Sets, Sorted Sets, Counters, Hashes
         │        │                        (Redis, InMemory)
         │        ├── RedisStateStore<T>     (full support)
         │        ├── InMemoryStateStore<T>  (full support)
         │        │
         │        └── ISearchableStateStore<T> ◄── Full-text search (extends Cacheable)
         │                 └── RedisSearchStateStore<T>  (Cacheable + FT search)
         │
         └── IQueryableStateStore<T>  ◄── LINQ queries
                  │                        (MySQL only)
                  └── IJsonQueryableStateStore<T>
                           └── MySqlStateStore<T>

    IRedisOperations  ◄────────────── Lua scripts, transactions
         └── RedisOperations            (Redis only, null if InMemory)

  Backend Layout:
  ===============

    ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
    │ RedisStateStore │    │RedisSearchStore │    │  MySqlStateStore│
    │ + ICacheable    │    │ + ICacheable    │    │ + IQueryable    │
    │ (Sets+ZSets+    │    │ + ISearchable   │    │ + IJsonQueryable│
    │  Counters+Hash) │    │ (full support)  │    │                 │
    └────────┬────────┘    └────────┬────────┘    └────────┬────────┘
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
2. **StateMetadata population**: `GetStateResponse.Metadata` returns empty object; timestamps not retrieved from backend.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/177 -->

---

## Potential Extensions

1. **TTL support for MySQL**: Currently Redis-only. MySQL stores never expire data.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/182 -->
2. **Store migration tooling**: Move data between Redis and MySQL backends without downtime.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/190 -->
3. **Prefix query support**: Add SCAN-based prefix queries for Redis (with careful iteration limits).
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/194 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently identified.

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

### Design Considerations (Requires Planning)

1. **MySQL query loads all into memory**: `QueryAsync` and `QueryPagedAsync` in MySqlStateStore load all entries from the store, deserialize each one, then filter in memory. For very large stores this could be memory-intensive. Consider SQL-level filtering.
   <!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/251 -->

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.
