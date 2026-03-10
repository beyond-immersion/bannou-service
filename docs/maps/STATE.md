# State Implementation Map

> **Plugin**: lib-state
> **Schema**: schemas/state-api.yaml
> **Layer**: Infrastructure
> **Deep Dive**: [docs/plugins/STATE.md](../plugins/STATE.md)

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-state |
| Layer | L0 Infrastructure |
| Endpoints | 9 |
| State Stores | Self-managed (provides `IStateStoreFactory` for all services) |
| Events Published | 0 |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

## State

StateService does not own dedicated state stores. It **is** the state store infrastructure — providing `IStateStoreFactory` for all other Bannou services. Each endpoint resolves a store dynamically by name from the request body:

```
store = factory.GetStore<object>(request.storeName)
```

All store names and keys are caller-provided. The service validates store existence via `factory.HasStore(storeName)` before each operation.

**Redis Backend** (internal key structure):

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{prefix}:{key}` | JSON string | Primary value storage |
| `{prefix}:{key}:meta` | Hash (`version`, `created`, `updated`) | ETag/version metadata |

**MySQL/SQLite Backend** (table: `state_entries`):

| Column | Type | Purpose |
|--------|------|---------|
| `StoreName` + `Key` | VARCHAR(255) composite PK | Record identification |
| `ValueJson` | LONGTEXT | JSON-serialized value |
| `ETag` | VARCHAR(64) | SHA256[0:12] base64 hash |
| `Version` | INT | Concurrency token |
| `CreatedAt` / `UpdatedAt` | TIMESTAMP | Audit timestamps |

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-messaging (`IMessageBus`) | L0 | Hard (lazy) | Error event publishing via `TryPublishErrorAsync` in `StateStoreFactory` |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation in `StateStoreFactory` |

**Special notes:**

- State is a **leaf node** — every service depends on it via `IStateStoreFactory`; it depends on nothing above L0
- State **self-provides** `IStateStoreFactory` and `IDistributedLockProvider` as Singletons
- `IMessageBus` is resolved lazily (`sp.GetService<IMessageBus>()` at registration time, nullable) because State loads at L0 position 0, before Messaging at position 1
- `IMessageBus` is optional in `StateStoreFactory` — if null, error events are silently skipped

## Events Published

This plugin publishes no domain events. Error events (`ServiceErrorEvent`) are published via `TryPublishErrorAsync` for operational visibility on infrastructure failures only, deduplicated by `store+operation+errorType` within a configurable window (default: 60s).

## Events Consumed

This plugin does not consume external events.

## DI Services

| Service | Role |
|---------|------|
| `ILogger<StateService>` | Structured logging |
| `StateServiceConfiguration` | Typed configuration access |
| `IServiceProvider` | Lazy `IMessageBus` resolution (L0 load-order constraint) |
| `IStateStoreFactory` | Dynamic store resolution per request |

**Plugin-registered Singletons** (via `StateServicePlugin`):

| Service | Implementation | Role |
|---------|----------------|------|
| `IStateStoreFactory` | `StateStoreFactory` | Creates/caches typed store instances, manages Redis/MySQL connections |
| `IDistributedLockProvider` | `RedisDistributedLockProvider` | Distributed mutex via Redis `SET NX EX` with Lua-based safe unlock |

**Helper Services**:

| Class | Role |
|-------|------|
| `RedisStateStore<T>` | Redis-backed store: string ops, sets, sorted sets, counters, hashes, TTL |
| `RedisSearchStateStore<T>` | Redis+FT store: JSON storage, FT.SEARCH queries, extends cacheable |
| `MySqlStateStore<T>` | MySQL-backed store: EF Core, JSON path queries, SHA256 ETags |
| `SqliteStateStore<T>` | SQLite-backed store: same interface as MySQL, WAL mode, file-per-store |
| `InMemoryStateStore<T>` | ConcurrentDictionary-backed store: testing/minimal deployments |
| `RedisOperations` | Raw Redis primitives: INCR/DECR, HGET/HSET, EXPIRE, Lua scripts |
| `RedisLuaScripts` | Static loader for embedded Lua scripts (`TryCreate.lua`, `TryUpdate.lua`) |

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetState | POST /state/get | [] | - | - |
| SaveState | POST /state/save | [] | entry | - |
| DeleteState | POST /state/delete | [] | entry | - |
| QueryState | POST /state/query | [] | - | - |
| BulkGetState | POST /state/bulk-get | [] | - | - |
| BulkSaveState | POST /state/bulk-save | [] | entries | - |
| BulkExistsState | POST /state/bulk-exists | [] | - | - |
| BulkDeleteState | POST /state/bulk-delete | [] | entries | - |
| ListStores | POST /state/list-stores | [] | - | - |

## Methods

### GetState
POST /state/get | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
READ {storeName}:{key} [with ETag] -> 404 if null
RETURN (200, GetStateResponse { value, etag })
```

### SaveState
POST /state/save | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
IF options?.etag is non-empty
 ETAG-WRITE {storeName}:{key} <- value -> 409 if ETag mismatch
 RETURN (200, SaveStateResponse { etag })
ELSE
 WRITE {storeName}:{key} <- value with options
 RETURN (200, SaveStateResponse { etag })
```

### DeleteState
POST /state/delete | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
DELETE {storeName}:{key}
// Idempotent: returns 200 whether key existed or not (status code communicates result)
RETURN (200)
```

### QueryState
POST /state/query | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
IF backend is MySQL/SQLite
 // Only first sort field is used; additional fields silently ignored
 QUERY {storeName} WHERE conditions ORDER BY sort[0] PAGED(page, pageSize)
 RETURN (200, QueryStateResponse { results, totalCount, page, pageSize })
ELSE IF backend is Redis with search enabled
 // FT.SEARCH on indexName (default: "{storeName}-idx")
 QUERY {storeName} WHERE query PAGED(page * pageSize, pageSize)
 RETURN (200, QueryStateResponse { results, totalCount, page, pageSize })
ELSE
 // Redis without search — query not supported
 RETURN (400, null)
```

### BulkGetState
POST /state/bulk-get | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
READ {storeName}:[keys] (bulk)
FOREACH key in request.keys
 IF key in bulk results
 item { key, found: true, value, etag }
 ELSE
 item { key, found: false }
RETURN (200, BulkGetStateResponse { items })
```

### BulkSaveState
POST /state/bulk-save | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
WRITE {storeName}:[items] (bulk) <- items with options
RETURN (200, BulkSaveStateResponse { results: [{ key, etag }] })
```

### BulkExistsState
POST /state/bulk-exists | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
READ {storeName}:[keys] (exists check)
// Returns only keys that exist; absent keys omitted from response
RETURN (200, BulkExistsStateResponse { existingKeys })
```

### BulkDeleteState
POST /state/bulk-delete | Roles: []

```
IF NOT factory.HasStore(storeName) -> 404
DELETE {storeName}:[keys] (bulk)
RETURN (200, BulkDeleteStateResponse { deletedCount })
```

### ListStores
POST /state/list-stores | Roles: []

```
IF backendFilter provided
 storeNames = factory.GetStoreNames(backendFilter)
ELSE
 storeNames = factory.GetStoreNames()
FOREACH name in storeNames
 backend = factory.GetBackendType(name)
 IF includeStats
 keyCount = factory.GetKeyCountAsync(name)
 // Redis: null (SCAN too slow), MySQL/SQLite: COUNT(*), InMemory: O(1)
RETURN (200, ListStoresResponse { stores: [{ name, backend, keyCount? }] })
```

## Background Services

No background services.
