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

6. **Unused configuration properties**: `DefaultConsistency`, `EnableMetrics`, and `EnableTracing` are defined in the configuration schema but never referenced in service code. Either wire up to actual functionality or remove from schema.

7. **Hardcoded retry delay minimum**: The retry delay uses a hardcoded minimum of 1000ms in `StateStoreFactory.cs:121`. Should be a configuration property (`MinRetryDelayMs`).

8. **ConnectionRetryCount not in schema**: `ConnectionRetryCount` has a hardcoded default of `10` in `StateStoreFactoryConfiguration` but is not exposed in the configuration schema. Should be added to `state-configuration.yaml`.

9. **Infrastructure-level error events**: `RedisDistributedLockProvider.LockAsync` catches exceptions and logs them but does not call `TryPublishErrorAsync`. Injecting `IMessageBus` into infrastructure code requires careful consideration of circular dependencies and whether infrastructure libs should publish events at all.
