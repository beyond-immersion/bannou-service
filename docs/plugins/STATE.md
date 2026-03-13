# State Plugin Deep Dive

> **Plugin**: lib-state
> **Schema**: schemas/state-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: Self (manages all state stores for the platform)
> **Implementation Map**: [docs/maps/STATE.md](../maps/STATE.md)
> **Short**: Unified state persistence (Redis/MySQL/SQLite/InMemory) with optimistic concurrency and specialized interfaces

---

## Overview

The State service (L0 Infrastructure) provides all Bannou services with unified access to Redis and MySQL backends through a repository-pattern API. Operates in a dual role: as the `IStateStoreFactory` infrastructure library used by every service for state persistence, and as an HTTP API for debugging and administration. Supports four backends (Redis for ephemeral/session data, MySQL for durable/queryable data, SQLite for self-hosted durable storage, InMemory for testing) with optimistic concurrency via ETags, TTL support, and specialized interfaces for cache operations, LINQ queries, JSON path queries, and full-text search. See the Visual Aid section for the full interface tree and backend support matrix.

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

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `UseInMemory` | `STATE_USE_INMEMORY` | `false` | Use in-memory stores for testing. Mutually exclusive with `UseSqlite`. |
| `UseSqlite` | `STATE_USE_SQLITE` | `false` | Use SQLite file storage instead of MySQL for SQL-backed stores. Redis-configured stores use in-memory. Mutually exclusive with `UseInMemory`. |
| `SqliteDataPath` | `STATE_SQLITE_DATA_PATH` | `"./data/state"` | Directory path for SQLite database files. Each MySQL-configured store gets its own `.db` file. |
| `RedisConnectionString` | `STATE_REDIS_CONNECTION_STRING` | `"bannou-redis:6379"` | Redis host:port |
| `MySqlConnectionString` | `STATE_MYSQL_CONNECTION_STRING` | `"server=bannou-mysql;..."` | Full MySQL connection string |
| `ConnectionTimeoutSeconds` | `STATE_CONNECTION_TIMEOUT_SECONDS` | `60` | Database connection timeout |
| `ConnectionRetryCount` | `STATE_CONNECTION_RETRY_COUNT` | `10` | Max MySQL connection retry attempts |
| `MinRetryDelayMs` | `STATE_MIN_RETRY_DELAY_MS` | `1000` | Min delay between MySQL retry attempts |
| `InMemoryFallbackLimit` | `STATE_INMEMORY_FALLBACK_LIMIT` | `10000` | Max entries for in-memory query fallback (throws if exceeded) |
| `EnableErrorEventPublishing` | `STATE_ENABLE_ERROR_EVENT_PUBLISHING` | `true` | Publish error events when state store operations fail |
| `ErrorEventDeduplicationWindowSeconds` | `STATE_ERROR_EVENT_DEDUPLICATION_WINDOW_SECONDS` | `60` | Time window for deduplicating identical error events |

**Note:** `DefaultConsistency`, `EnableMetrics`, and `EnableTracing` were removed as dead config. Telemetry is now controlled centrally via lib-telemetry. Consistency is specified per-request via `StateOptions.Consistency`.

### Error Event Publishing

When infrastructure errors occur (Redis connection failures, timeouts, etc.), the state stores can publish `ServiceErrorEvent` messages via `IMessageBus.TryPublishErrorAsync`. This provides observability into state store health without impacting caller latency.

**Deduplication**: To prevent event storms during infrastructure failures, events are deduplicated using a key-based time window. Events with the same `storeName + operation + errorType` are published at most once per deduplication window (default: 60 seconds).

**Fire-and-Forget**: Error publishing is non-blocking (`_ = _errorPublisher?.Invoke(...)`) and does not affect the exception behavior - stores still throw as before.

**Disable**: Set `STATE_ENABLE_ERROR_EVENT_PUBLISHING=false` to disable error event publishing entirely.

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
                  │                        (MySQL, SQLite)
                  └── IJsonQueryableStateStore<T>
                           ├── MySqlStateStore<T>
                           └── SqliteStateStore<T>

    IRedisOperations  ◄────────────── Lua scripts, transactions
         └── RedisOperations            (Redis only, null if InMemory)

  Backend Layout:
  ===============

    ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
    │ RedisStateStore │    │RedisSearchStore │    │  MySqlStateStore│    │SqliteStateStore │
    │ + ICacheable    │    │ + ICacheable    │    │ + IQueryable    │    │ + IQueryable    │
    │ (Sets+ZSets+    │    │ + ISearchable   │    │ + IJsonQueryable│    │ + IJsonQueryable│
    │  Counters+Hash) │    │ (full support)  │    │                 │    │ (file-backed)   │
    └────────┬────────┘    └────────┬────────┘    └────────┬────────┘    └────────┬────────┘
             │                      │                      │                      │
    ┌────────▼────────┐    ┌────────▼────────┐    ┌────────▼────────┐    ┌────────▼────────┐
    │     Redis       │    │   Redis + FT    │    │      MySQL      │    │     SQLite      │
    │  (String/Sets)  │    │   (JSON/Index)  │    │   (StateEntry)  │    │  (per-store .db)│
    └─────────────────┘    └─────────────────┘    └─────────────────┘    └─────────────────┘
```

**Key Design**: `ISearchableStateStore<T>` extends `ICacheableStateStore<T>` because all searchable stores are Redis-based and therefore support all cacheable operations (sets, sorted sets, counters, hashes). `SqliteStateStore<T>` implements the same `IJsonQueryableStateStore<T>` interface as `MySqlStateStore<T>`, providing a drop-in replacement for self-hosted deployments without MySQL infrastructure.

**Backend Support Matrix**:

| Interface | Redis | MySQL | SQLite | InMemory | RedisSearch |
|-----------|:-----:|:-----:|:------:|:--------:|:-----------:|
| `IStateStore<T>` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sets) | ✅ | ❌ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sorted Sets) | ✅ | ❌ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Counters) | ✅ | ❌ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Hashes) | ✅ | ❌ | ❌ | ✅ | ✅ |
| `IQueryableStateStore<T>` | ❌ | ✅ | ✅ | ❌ | ❌ |
| `IJsonQueryableStateStore<T>` | ❌ | ✅ | ✅ | ❌ | ❌ |
| `ISearchableStateStore<T>` | ❌ | ❌ | ❌ | ❌ | ✅ |
| `IRedisOperations` | ✅ | ❌ | ❌ | ❌ | ❌ |

---

## Stubs & Unimplemented Features

None.

---

## Potential Extensions

None currently identified.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently identified.

### Intentional Quirks (Documented Behavior)

1. **Sync-over-async in GetStore()**: `StateStoreFactory.GetStore<T>()` calls `.GetAwaiter().GetResult()` if initialization hasn't completed. This supports services that call GetStore in constructors but can deadlock in async contexts. A warning is logged when this occurs. Prefer `GetStoreAsync<T>()` or call `InitializeAsync()` at startup.

2. **ETag format inconsistency**: Redis uses a `long` version counter as ETag (incremented on each save). MySQL and SQLite use `SHA256(key:json)[0:12]` (base64). Same logical concept, different formats across backends. Services should treat ETags as opaque strings.

3. **Shared static stores in InMemory mode**: `InMemoryStateStore` uses static `ConcurrentDictionary` instances keyed by store name. `GetStore<TypeA>("store")` and `GetStore<TypeB>("store")` see the same underlying data (serialized as JSON). Enables cross-type access but can cause test pollution if not cleared between tests.

4. **MySQL JSON query operators**: `Contains` and `FullText` both use `LIKE %value%`. These are simplified implementations, not true full-text search on MySQL.

5. **RedisSearchStateStore falls back to string storage**: The `GetAsync` and `GetWithETagAsync` methods catch `WRONGTYPE` errors and fall back to `StringGet` for backwards compatibility with keys stored as strings before search was enabled.

6. **No per-operation state change events**: The State service intentionally does not publish lifecycle events for individual state mutations (save/delete). Publishing events for every operation would be prohibitively expensive given the high volume across all services. Error events are published via `TryPublishErrorAsync` for operational visibility. Migration events (`state.migration.started`, `state.migration.completed`, `state.migration.failed`) are published for admin-initiated backend transitions — these are rare operational events, not per-operation tracking.

7. **No store-level access control**: Any service can access any store via `IStateStoreFactory.GetStore<T>(anyName)`. This is intentional - enforcement was considered and rejected for these reasons:
   - All services are in the same trust boundary (same codebase, same deployment)
   - Adding access control would require ambient context or passed parameters to identify the "current service" at call time
   - Significant performance overhead for every state store access
   - Would break the clean DI pattern where services just inject `IStateStoreFactory`
   - The generated `StateStoreDefinitions` constants guide correct usage via convention
   - This is internal infrastructure, not an external API requiring authorization

8. **Lazy IMessageBus resolution in StateService**: `StateService` cannot inject `IMessageBus` in its constructor because State (L0) loads before Messaging (L0) in infrastructure load order. Instead, `MessageBus` is a lazily-resolved property via `_serviceProvider.GetRequiredService<IMessageBus>()`. By contrast, `StateStoreFactory` receives `IMessageBus` optionally via `GetService<IMessageBus>()` during DI registration in `StateServicePlugin`, since that runs after all `ConfigureServices` calls.

9. **Asymmetric connection retry between MySQL and Redis**: MySQL initialization retries up to `ConnectionRetryCount` times with configurable delay. Redis initialization does not retry. This is intentional: StackExchange.Redis has built-in auto-reconnect functionality that handles connection failures and reconnection automatically. EF Core/MySQL does not have this capability, so explicit retry logic is required for MySQL only.

10. **MySQL expression translator limitations**: `QueryAsync` and `QueryPagedAsync` attempt to translate LINQ expressions to SQL-level `JSON_EXTRACT` queries. Supported patterns include:
    - Simple comparisons: `x.Field == value`, `x.Field != value`, `x.Field > value`
    - Null comparisons: `x.Field == null`, `x.Field != null`
    - String operations: `x.Field.Contains("s")`, `x.Field.StartsWith("s")`, `x.Field.EndsWith("s")`
    - Collection membership: `ids.Contains(x.Id)` (generates SQL `IN` clause)
    - Boolean property access: `x.IsActive` (treats as `x.IsActive == true`)

    Unsupported patterns fall back to in-memory filtering with a configurable limit (`InMemoryFallbackLimit`, default 10000). Exceeding this limit throws `InvalidOperationException` to prevent OOM. Use `JsonQueryAsync` with explicit `QueryCondition` objects for complex queries on large datasets.

11. **MySQL stores reject TTL requests**: Passing `StateOptions.Ttl` to `SaveAsync` or `SaveBulkAsync` on a MySQL store throws `InvalidOperationException`. This is by design: MySQL stores are for durable/queryable data that should not auto-expire. For ephemeral data requiring TTL, use a Redis-backed store instead. This enforces the architectural separation between Redis (ephemeral/session) and MySQL (durable/queryable) data.

### Design Considerations (Requires Planning)

None currently identified.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.
