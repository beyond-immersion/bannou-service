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
| Endpoints | 12 |
| State Stores | Self-managed (provides `IStateStoreFactory` for all services) |
| Events Published | 3 (migration lifecycle) |
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

**Error events**: `ServiceErrorEvent` published via `TryPublishErrorAsync` for operational visibility on infrastructure failures, deduplicated by `store+operation+errorType` within a configurable window (default: 60s).

**Migration events** (published by `StateMigrationHelper` during execute operations only — not dry-run):

| Topic | Event | When |
|-------|-------|------|
| `state.migration.started` | `StateMigrationStartedEvent` | After validation, before first batch |
| `state.migration.completed` | `StateMigrationCompletedEvent` | After all batches complete successfully |
| `state.migration.failed` | `StateMigrationFailedEvent` | On unrecoverable error during migration |

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
| MigrateDryRun | POST /state/migrate/dry-run | [role: admin] | - | - |
| MigrateExecute | POST /state/migrate/execute | [role: admin] | destination store | `state.migration.started`, `state.migration.completed`, `state.migration.failed` |
| MigrateVerify | POST /state/migrate/verify | [role: admin] | - | - |

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

### MigrateDryRun
POST /state/migrate/dry-run | Roles: [role: admin]

Delegates to `StateMigrationHelper.AnalyzeStoreAsync`.

```
IF NOT factory.HasStore(storeName) -> 404
currentBackend = factory.GetBackendType(storeName)
IF currentBackend == destinationBackend -> canMigrate: false, warning
IF store.indirectOnly -> canMigrate: false, warning
IF store.enableSearch -> incompatible: "RedisSearch index"
IF currentBackend != Redis -> keyCount = factory.GetKeyCountAsync(storeName)
ELSE -> keyCount = null, warning "Redis count unavailable"
warnings += "ETag format will change"
IF currentBackend is Memory -> canMigrate: false, warning "unsupported source"
RETURN (200, MigrateDryRunResponse { storeName, currentBackend, destinationBackend, keyValueEntryCount, incompatibleFeatures, warnings, canMigrate })
```

### MigrateExecute
POST /state/migrate/execute | Roles: [role: admin]

Delegates to `StateMigrationHelper.ExecuteMigrationAsync`.

```
IF NOT factory.HasStore(storeName) -> 404
IF currentBackend == destinationBackend -> 400
IF currentBackend is Memory -> 400
IF store.indirectOnly -> 400
sourceStore = factory.GetStore<object>(storeName)
destStore = factory.CreateStoreWithBackend<object>(storeName, destBackend)
PUBLISH state.migration.started { storeName, sourceBackend, destinationBackend, startedAt }
TRY
  IF source is MySQL/SQLite
    LOOP paged via JsonQueryPagedAsync(null, offset, batchSize)
      SaveBulkAsync to destination
      offset += batchSize
    UNTIL empty page
  ELSE IF source is Redis
    SCAN "{prefix}:*" via factory.ScanKeysAsync
    FILTER OUT ":meta" suffix keys
    STRIP prefix to get logical keys
    GetAsync from source, batch SaveBulkAsync to destination
  PUBLISH state.migration.completed { storeName, entriesMigrated, durationMs, completedAt }
  RETURN (200, MigrateExecuteResponse { storeName, entriesMigrated, durationMs })
CATCH
  PUBLISH state.migration.failed { storeName, entriesProcessedBeforeFailure, error, failedAt }
  THROW (generated controller handles 500)
```

### MigrateVerify
POST /state/migrate/verify | Roles: [role: admin]

Delegates to `StateMigrationHelper.VerifyMigrationAsync`.

```
IF NOT factory.HasStore(storeName) -> 404
sourceKeyCount = factory.GetKeyCountAsync(storeName)  // null for Redis
destStore = factory.CreateStoreWithBackend<object>(storeName, destBackend)
IF destStore is IQueryableStateStore -> destKeyCount = CountAsync()
ELSE -> destKeyCount = null  // Redis destination can't count
countsMatch = (both non-null) ? sourceCount == destCount : null
RETURN (200, MigrateVerifyResponse { storeName, sourceKeyCount, destinationKeyCount, countsMatch })
```

## DI Services (Migration)

| Service | Role |
|---------|------|
| `StateMigrationHelper` | Scoped — migration analysis, execution, and verification |

**StateMigrationHelper dependencies**:

| Dependency | Resolution | Purpose |
|------------|-----------|---------|
| `IStateStoreFactory` | Constructor (cast to `StateStoreFactory`) | Internal factory methods: `CreateStoreWithBackend`, `ScanKeysAsync`, `GetKeyPrefix`, `GetStoreConfiguration` |
| `StateServiceConfiguration` | Constructor | `MigrationBatchSize` |
| `IMessageBus` | Lazy via `IServiceProvider` | Migration event publishing |
| `ILogger<StateMigrationHelper>` | Constructor | Structured logging |
| `ITelemetryProvider` | Constructor | Span instrumentation |

## Background Services

No background services.
