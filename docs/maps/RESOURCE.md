# Resource Implementation Map

> **Plugin**: lib-resource
> **Schema**: schemas/resource-api.yaml
> **Layer**: AppFoundation
> **Deep Dive**: [docs/plugins/RESOURCE.md](../plugins/RESOURCE.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-resource |
| Layer | L1 AppFoundation |
| Endpoints | 17 |
| State Stores | resource-refcounts (Redis), resource-grace (Redis), resource-cleanup (Redis), resource-compress (Redis), resource-archives (MySQL), resource-snapshots (Redis) |
| Events Published | 6 (resource.grace-period.started, resource.cleanup.callback-failed, resource.compressed, resource.compress.callback-failed, resource.decompressed, resource.snapshot.created) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `resource-refcounts` (Backend: Redis, ICacheableStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{resourceType}:{resourceId}:sources` | Set of `ResourceReferenceEntry` | All entities referencing this resource; equality by SourceType+SourceId |

**Store**: `resource-grace` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{resourceType}:{resourceId}:grace` | `GracePeriodRecord` | Records when refcount became zero; cleared on new reference or cleanup |

**Store**: `resource-cleanup` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `callback:{resourceType}:{sourceType}` | `CleanupCallbackDefinition` | Cleanup callback registration (service, endpoint, template, onDeleteAction) |
| `callback-index:{resourceType}` | Set of `string` | Source types with registered callbacks for this resource type |
| `callback-resource-types` | Set of `string` | Master index of all resource types with callbacks |

**Store**: `resource-compress` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `compress-callback:{resourceType}:{sourceType}` | `CompressCallbackDefinition` | Compression callback registration (endpoints, priority, template) |
| `compress-callback-index:{resourceType}` | Set of `string` | Source types with compression callbacks for this resource type |
| `compress-callback-resource-types` | Set of `string` | Master index of resource types with compression callbacks |

**Store**: `resource-archives` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `archive:{resourceType}:{resourceId}` | `ResourceArchiveModel` | Bundled compressed archive; version tracked within model; overwrites on re-compression |

**Store**: `resource-snapshots` (Backend: Redis, with TTL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `snap:{snapshotId}` | `ResourceSnapshotModel` | Ephemeral snapshot; auto-expired by Redis TTL |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Acquires 6 state stores; inline `GetCacheableStore<string>` for set index operations |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Cleanup locks (`cleanup:{resourceType}:{resourceId}`) and compression locks (`compress:{resourceType}:{resourceId}`) |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing all 6 service events |
| lib-mesh (`IServiceNavigator`) | L0 | Hard | Executing cleanup/compression/decompression callbacks via `ExecutePreboundApiAsync` / `ExecutePreboundApiBatchAsync` |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation in helper methods |

**DI Provider interface consumed**: `IEnumerable<ISeededResourceProvider>` — higher-layer plugins implement this to expose embedded resources (ABML behaviors, templates). Resource discovers providers via DI collection and aggregates them through the seeded resource endpoints.

**Special notes**:
- Resource has zero service client dependencies. All cross-service coordination happens via `IServiceNavigator` executing opaque callback endpoint definitions stored as data in Redis. This satisfies L1 hierarchy isolation — Resource never knows what services it calls.
- Account is exempt from lib-resource per T28 privacy exception.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `resource.grace-period.started` | `ResourceGracePeriodStartedEvent` | UnregisterReference when refcount drops to 0 |
| `resource.cleanup.callback-failed` | `ResourceCleanupCallbackFailedEvent` | ExecuteCleanup per failed CASCADE/DETACH callback |
| `resource.compressed` | `ResourceCompressedEvent` | ExecuteCompress on successful archive creation |
| `resource.compress.callback-failed` | `ResourceCompressCallbackFailedEvent` | ExecuteCompress or ExecuteSnapshot per failed callback |
| `resource.decompressed` | `ResourceDecompressedEvent` | ExecuteDecompress when at least one callback succeeds |
| `resource.snapshot.created` | `ResourceSnapshotCreatedEvent` | ExecuteSnapshot on successful snapshot creation |

---

## Events Consumed

This plugin does not consume external events. `ResourceServiceEvents.cs` registers no event consumers.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<ResourceService>` | Structured logging |
| `ResourceServiceConfiguration` | Typed configuration access (10 properties) |
| `IStateStoreFactory` | State store access (6 stores + inline cacheable stores for set indexes) |
| `IDistributedLockProvider` | Cleanup and compression distributed locks |
| `IMessageBus` | Event publishing (6 topics) |
| `IServiceNavigator` | Prebound API callback execution for cleanup/compression/decompression |
| `ITelemetryProvider` | Span instrumentation in helper methods |
| `IEventConsumer` | Event fan-out registration (none currently registered) |
| `IEnumerable<ISeededResourceProvider>` | DI provider collection for seeded resource listing/retrieval |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| RegisterReference | POST /resource/register | developer | refs, grace | - |
| UnregisterReference | POST /resource/unregister | developer | refs, grace | resource.grace-period.started |
| CheckReferences | POST /resource/check | developer | - | - |
| ListReferences | POST /resource/list | developer | - | - |
| DefineCleanupCallback | POST /resource/cleanup/define | admin | cleanup-callback, cleanup-index | - |
| ExecuteCleanup | POST /resource/cleanup/execute | developer | grace, refs | resource.cleanup.callback-failed |
| ListCleanupCallbacks | POST /resource/cleanup/list | developer | - | - |
| RemoveCleanupCallback | POST /resource/cleanup/remove | admin | cleanup-callback, cleanup-index | - |
| DefineCompressCallback | POST /resource/compress/define | admin | compress-callback, compress-index | - |
| ExecuteCompress | POST /resource/compress/execute | developer | archive | resource.compress.callback-failed, resource.compressed |
| ExecuteDecompress | POST /resource/decompress/execute | admin | - | resource.decompressed |
| ListCompressCallbacks | POST /resource/compress/list | developer | - | - |
| GetArchive | POST /resource/archive/get | developer | - | - |
| ExecuteSnapshot | POST /resource/snapshot/execute | developer | snapshot | resource.compress.callback-failed, resource.snapshot.created |
| GetSnapshot | POST /resource/snapshot/get | developer | - | - |
| ListSeededResources | POST /resource/seeded/list | [] | - | - |
| GetSeededResource | POST /resource/seeded/get | [] | - | - |

---

## Methods

### RegisterReference
POST /resource/register | Roles: [developer]

```
WRITE _refStore:{resourceType}:{resourceId}:sources <- entry  // set add, atomic; deduplicates by SourceType+SourceId
IF added
  DELETE _graceStore:{resourceType}:{resourceId}:grace  // clear zero-timestamp
READ _refStore:{resourceType}:{resourceId}:sources  // set count -> refCount
RETURN (200, RegisterReferenceResponse { newRefCount, alreadyRegistered: !added })
```

---

### UnregisterReference
POST /resource/unregister | Roles: [developer]

```
DELETE _refStore:{resourceType}:{resourceId}:sources <- entry  // set remove; equality ignores RegisteredAt
READ _refStore:{resourceType}:{resourceId}:sources  // set count -> refCount
IF removed AND refCount == 0
  WRITE _graceStore:{resourceType}:{resourceId}:grace <- GracePeriodRecord { ZeroTimestamp: now }
  PUBLISH resource.grace-period.started { resourceType, resourceId, gracePeriodEndsAt, timestamp }
RETURN (200, UnregisterReferenceResponse { newRefCount, wasRegistered: removed, gracePeriodStartedAt })
```

---

### CheckReferences
POST /resource/check | Roles: [developer]

```
READ _refStore:{resourceType}:{resourceId}:sources  // set count -> refCount
READ _refStore:{resourceType}:{resourceId}:sources  // full set -> entries
READ _graceStore:{resourceType}:{resourceId}:grace
IF refCount == 0 AND graceRecord != null
  // isCleanupEligible = now >= graceRecord.ZeroTimestamp + config.DefaultGracePeriodSeconds
  IF isCleanupEligible
    // gracePeriodEndsAt = null (already passed)
  ELSE
    // gracePeriodEndsAt = graceRecord.ZeroTimestamp + gracePeriod
RETURN (200, CheckReferencesResponse { refCount, isCleanupEligible, sources, gracePeriodEndsAt, lastZeroTimestamp })
```

// Also called internally by ExecuteCleanup for pre-check and under-lock re-validation.

---

### ListReferences
POST /resource/list | Roles: [developer]

```
READ _refStore:{resourceType}:{resourceId}:sources  // full set -> entries
IF filterSourceType specified
  // filter entries by SourceType
// totalCount = count of filtered (pre-limit) entries
// apply limit via Take(body.Limit)
RETURN (200, ListReferencesResponse { references, totalCount })
```

---

### DefineCleanupCallback
POST /resource/cleanup/define | Roles: [admin]

```
READ _cleanupStore:callback:{resourceType}:{sourceType}  // check for existing -> previouslyDefined
WRITE _cleanupStore:callback:{resourceType}:{sourceType} <- CleanupCallbackDefinition
  // serviceName defaults to sourceType; onDeleteAction defaults to Cascade
WRITE _cleanupStore:callback-index:{resourceType} <- sourceType  // set add
WRITE _cleanupStore:callback-resource-types <- resourceType  // set add
RETURN (200, DefineCleanupResponse { registered: true, previouslyDefined })
```

---

### ExecuteCleanup
POST /resource/cleanup/execute | Roles: [developer]

```
// Gather all callbacks for this resourceType
READ _cleanupStore:callback-index:{resourceType}  // set -> sourceTypes
FOREACH sourceType
  READ _cleanupStore:callback:{resourceType}:{sourceType}

// Internal reference check (reuses CheckReferences logic)
READ _refStore:{resourceType}:{resourceId}:sources  // count + full set
READ _graceStore:{resourceType}:{resourceId}:grace

// Separate callbacks by OnDeleteAction
// restrictCallbacks = callbacks where OnDeleteAction == Restrict
// cascadeDetachCallbacks = callbacks where OnDeleteAction == Cascade or Detach

IF dryRun
  // Analyze RESTRICT violations, unresolved refs, grace period
  // Return preview with hypothetical results
  RETURN (200, ExecuteCleanupResponse { success, dryRun: true, callbackResults })

IF any RESTRICT callbacks with active references of that sourceType
  RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Blocked by RESTRICT policy" })

IF unresolved references without registered callbacks
  RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Unhandled references" })

// gracePeriodSeconds: 0 skips grace check (used by ExecuteCompress with deleteSourceData)
IF grace period not elapsed AND gracePeriodSeconds != 0
  RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Grace period active" })

LOCK _refStore:cleanup:{resourceType}:{resourceId}
  // -> 200 { success: false, abortReason: "Failed to acquire cleanup lock" } if lock fails

  // Re-validate under lock (race protection)
  READ _refStore:{resourceType}:{resourceId}:sources  // count
  IF count changed
    READ _refStore:{resourceType}:{resourceId}:sources  // full set
    // Re-check RESTRICT violations with updated refs

  // Execute only CASCADE and DETACH callbacks (not RESTRICT)
  CALL _navigator.ExecutePreboundApiBatchAsync(cascadeDetachCallbacks, Parallel, timeout)

  FOREACH failed callback
    PUBLISH resource.cleanup.callback-failed { resourceType, resourceId, sourceType, serviceName, endpoint, statusCode, errorMessage, timestamp }

  IF AllRequired policy AND any failure
    RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Callback failed with ALL_REQUIRED policy" })

  // Cleanup succeeded (or BestEffort with partial success)
  DELETE _graceStore:{resourceType}:{resourceId}:grace
  DELETE _refStore:{resourceType}:{resourceId}:sources  // delete entire set

RETURN (200, ExecuteCleanupResponse { success: true, callbackResults, cleanupDurationMs })
```

---

### ListCleanupCallbacks
POST /resource/cleanup/list | Roles: [developer]

```
IF resourceType specified
  READ _cleanupStore:callback-index:{resourceType}  // set -> sourceTypes
  FOREACH sourceType
    READ _cleanupStore:callback:{resourceType}:{sourceType}
  IF sourceType filter also specified
    // filter results by sourceType
ELSE
  READ _cleanupStore:callback-resource-types  // master set -> resourceTypes
  FOREACH resourceType
    READ _cleanupStore:callback-index:{resourceType}  // set -> sourceTypes
    FOREACH sourceType
      READ _cleanupStore:callback:{resourceType}:{sourceType}
RETURN (200, ListCleanupCallbacksResponse { callbacks, totalCount })
```

---

### RemoveCleanupCallback
POST /resource/cleanup/remove | Roles: [admin]

```
READ _cleanupStore:callback:{resourceType}:{sourceType}
  -> (200, RemoveCleanupCallbackResponse { wasRegistered: false, removedAt: null }) if null  // idempotent
DELETE _cleanupStore:callback:{resourceType}:{sourceType}
DELETE _cleanupStore:callback-index:{resourceType} <- sourceType  // set remove
READ _cleanupStore:callback-index:{resourceType}  // check if set is now empty
IF empty
  DELETE _cleanupStore:callback-resource-types <- resourceType  // set remove from master index
RETURN (200, RemoveCleanupCallbackResponse { wasRegistered: true, removedAt: now })
```

---

### DefineCompressCallback
POST /resource/compress/define | Roles: [admin]

```
READ _compressStore:compress-callback:{resourceType}:{sourceType}  // check existing -> previouslyDefined
WRITE _compressStore:compress-callback:{resourceType}:{sourceType} <- CompressCallbackDefinition
  // serviceName defaults to sourceType; decompressEndpoint/template are optional
WRITE _compressStore:compress-callback-index:{resourceType} <- sourceType  // set add
WRITE _compressStore:compress-callback-resource-types <- resourceType  // set add
RETURN (200, DefineCompressCallbackResponse { registered: true, previouslyDefined })
```

---

### ExecuteCompress
POST /resource/compress/execute | Roles: [developer]

```
// Get all compression callbacks, sorted by Priority ASC
READ _compressStore:compress-callback-index:{resourceType}  // set -> sourceTypes
FOREACH sourceType
  READ _compressStore:compress-callback:{resourceType}:{sourceType}
// Sort by Priority ASC

IF no callbacks
  RETURN (200, ExecuteCompressResponse { success: false, abortReason: "No compression callbacks registered" })

IF dryRun
  RETURN (200, ExecuteCompressResponse { success: true, dryRun: true, callbackResults })

LOCK _compressStore:compress:{resourceType}:{resourceId}
  // -> 200 { success: false, abortReason: "Failed to acquire compression lock" } if lock fails

  FOREACH callback (sorted by priority, sequential)
    CALL _navigator.ExecutePreboundApiAsync(callback.CompressEndpoint, context["resourceId"], timeout)
    IF success
      // GZip compress response body, Base64 encode, compute SHA256 checksum
      // Add to archive entries
    ELSE
      PUBLISH resource.compress.callback-failed { eventId, timestamp, resourceType, resourceId, sourceType, serviceName, endpoint, statusCode, errorMessage }
      IF AllRequired policy
        RETURN (200, ExecuteCompressResponse { success: false, abortReason: "Callback failed with ALL_REQUIRED" })

  IF no successful callbacks
    RETURN (200, ExecuteCompressResponse { success: false, abortReason: "No successful compression callbacks" })

  // Determine version
  READ _archiveStore:archive:{resourceType}:{resourceId}  // existing archive for version
  // version = (existing?.Version ?? 0) + 1

  WRITE _archiveStore:archive:{resourceType}:{resourceId} <- ResourceArchiveModel { archiveId, version, entries, createdAt }

  IF deleteSourceData
    // Internal self-call with gracePeriodSeconds: 0
    CALL ExecuteCleanupAsync({ resourceType, resourceId, gracePeriodSeconds: 0, cleanupPolicy: deleteSourceDataPolicy })
    IF cleanup succeeded
      WRITE _archiveStore:archive:{resourceType}:{resourceId} <- archive { sourceDataDeleted: true }

  PUBLISH resource.compressed { eventId, timestamp, resourceType, resourceId, archiveId, sourceDataDeleted, entriesCount }

RETURN (200, ExecuteCompressResponse { success: true, archiveId, sourceDataDeleted, callbackResults })
```

---

### ExecuteDecompress
POST /resource/decompress/execute | Roles: [admin]

```
READ _archiveStore:archive:{resourceType}:{resourceId}
  -> (200, ExecuteDecompressResponse { success: false, abortReason: "No archive found" }) if null

IF archiveId specified AND archive.ArchiveId != archiveId
  RETURN (200, ExecuteDecompressResponse { success: false, abortReason: "Archive ID mismatch" })

// Get compression callbacks for decompression endpoints
READ _compressStore (via GetCompressCallbacksAsync)

FOREACH entry in archive.Entries (sequential, per-entry try/catch)
  IF no callback registered for entry.SourceType
    // Add failure result, continue
  ELSE IF callback.DecompressEndpoint is null/empty
    // Add failure result, continue
  ELSE
    CALL _navigator.ExecutePreboundApiAsync(callback.DecompressEndpoint, context["resourceId", "data"=entry.Data], timeout)

IF any callback succeeded
  PUBLISH resource.decompressed { eventId, timestamp, resourceType, resourceId, archiveId, entriesCount, succeededSourceTypes, failedSourceTypes }

RETURN (200, ExecuteDecompressResponse { success: allSucceeded, archiveId, callbackResults })
```

---

### ListCompressCallbacks
POST /resource/compress/list | Roles: [developer]

```
IF resourceType specified
  READ _compressStore:compress-callback-index:{resourceType}  // set -> sourceTypes
  FOREACH sourceType
    READ _compressStore:compress-callback:{resourceType}:{sourceType}
  IF sourceType filter also specified
    // filter results by sourceType
ELSE
  READ _compressStore:compress-callback-resource-types  // master set -> resourceTypes
  FOREACH resourceType
    READ _compressStore:compress-callback-index:{resourceType}
    FOREACH sourceType
      READ _compressStore:compress-callback:{resourceType}:{sourceType}
RETURN (200, ListCompressCallbacksResponse { callbacks, totalCount })
```

---

### GetArchive
POST /resource/archive/get | Roles: [developer]

```
READ _archiveStore:archive:{resourceType}:{resourceId}
  -> (200, GetArchiveResponse { found: false }) if null
IF archiveId specified AND archive.ArchiveId != archiveId
  RETURN (200, GetArchiveResponse { found: false })
RETURN (200, GetArchiveResponse { found: true, archive })
```

---

### ExecuteSnapshot
POST /resource/snapshot/execute | Roles: [developer]

```
// Get compression callbacks (reuses compress callback infrastructure)
READ _compressStore:compress-callback-index:{resourceType}  // set -> sourceTypes
FOREACH sourceType
  READ _compressStore:compress-callback:{resourceType}:{sourceType}
// Sort by Priority ASC

IF filterSourceTypes specified
  // Filter callbacks to matching source types only (case-insensitive)

IF no callbacks (after filtering)
  RETURN (200, ExecuteSnapshotResponse { success: false, abortReason: "No callbacks" })

IF dryRun
  RETURN (200, ExecuteSnapshotResponse { success: true, dryRun: true, callbackResults })

// Clamp TTL: ttl = clamp(body.TtlSeconds ?? config.SnapshotDefaultTtlSeconds, config.SnapshotMinTtlSeconds, config.SnapshotMaxTtlSeconds)

FOREACH callback (sorted by priority, sequential)
  CALL _navigator.ExecutePreboundApiAsync(callback.CompressEndpoint, context["resourceId"], timeout)
  IF success
    // GZip compress + Base64 encode + SHA256 checksum (same as ExecuteCompress)
  ELSE
    PUBLISH resource.compress.callback-failed { eventId, timestamp, resourceType, resourceId, sourceType, serviceName, endpoint, statusCode, errorMessage }
    IF AllRequired policy
      RETURN (200, ExecuteSnapshotResponse { success: false, abortReason: "Callback failed with ALL_REQUIRED" })

IF no successful callbacks
  RETURN (200, ExecuteSnapshotResponse { success: false, abortReason: "No successful snapshot callbacks" })

// snapshotId = new Guid (unique key, no lock needed)
// snapshotType = body.SnapshotType ?? "default"
WRITE _snapshotStore:snap:{snapshotId} <- ResourceSnapshotModel [TTL: ttlSeconds]
PUBLISH resource.snapshot.created { eventId, timestamp, resourceType, resourceId, snapshotId, snapshotType, expiresAt, entriesCount }
RETURN (200, ExecuteSnapshotResponse { success: true, snapshotId, expiresAt, callbackResults })
```

---

### GetSnapshot
POST /resource/snapshot/get | Roles: [developer]

```
READ _snapshotStore:snap:{snapshotId}
  -> (200, GetSnapshotResponse { found: false }) if null  // may have TTL-expired
IF filterSourceTypes specified
  // Filter snapshot entries by source type (case-insensitive)
RETURN (200, GetSnapshotResponse { found: true, snapshot })
```

---

### ListSeededResources
POST /resource/seeded/list | Roles: []

```
FOREACH provider in _seededProviders (sequential, per-provider try/catch)
  IF resourceType filter specified AND provider.ResourceType != filter
    // skip
  CALL provider.ListSeededAsync()
  // SizeBytes always null in summaries (efficiency)
RETURN (200, ListSeededResourcesResponse { resources, totalCount })
```

---

### GetSeededResource
POST /resource/seeded/get | Roles: []

```
// Filter _seededProviders by ResourceType (case-insensitive)
IF no matching providers
  RETURN (200, GetSeededResourceResponse { found: false })
FOREACH provider in matchingProviders (sequential, per-provider try/catch)
  CALL provider.GetSeededAsync(identifier)
  IF result != null
    // Convert content to Base64, map metadata
    RETURN (200, GetSeededResourceResponse { found: true, resource })
  // If provider throws, log warning and continue to next
RETURN (200, GetSeededResourceResponse { found: false })
```

---

## Background Services

No background services.
