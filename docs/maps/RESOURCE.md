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
| Endpoints | 26 |
| State Stores | resource-refcounts (Redis), resource-grace (Redis), resource-cleanup (Redis), resource-compress (Redis), resource-archives (MySQL), resource-snapshots (Redis), resource-migrate (Redis), resource-transactions (MySQL), resource-provisions (MySQL) |
| Events Published | 16 (resource.grace-period.started, resource.cleanup.callback-failed, resource.compressed, resource.compress.callback-failed, resource.decompressed, resource.snapshot.created, resource.migrated, resource.migrate.callback-failed, resource.transaction.created, resource.transaction.committed, resource.transaction.aborted, resource.transaction.auto-committed, resource.transaction.auto-aborted, resource.transaction.commit-failed, resource.transaction.compensation-exhausted, resource.transaction.validation-exhausted) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 1 (TransactionRecoveryWorker) |

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

**Store**: `resource-migrate` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `callback:{resourceType}:{sourceType}` | `MigrateCallbackDefinition` | Migrate callback registration (service, endpoint, template) |
| `callback-index:{resourceType}` | Set of `string` | Source types with registered migrate callbacks for this resource type |

**Store**: `resource-transactions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tx:{transactionId}` | `ResourceTransactionModel` | Provisioning transaction record: owner, parent resource, status, TTL, validation state, serialized completionValidation PreboundApi |

**Store**: `resource-provisions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `prov:{provisionId}` | `ResourceProvisionModel` | Individual provision within a transaction: resource type/ID, sequence number, status, serialized compensation/verification PreboundApi |
| `prov-tx:{transactionId}` | `string` (JSON list of provisionIds) | Provisions belonging to a transaction (ordered by sequenceNumber) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Acquires 7 state stores; inline `GetCacheableStore<string>` for set index operations |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Cleanup locks (`cleanup:{resourceType}:{resourceId}`) and compression locks (`compress:{resourceType}:{resourceId}`) |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing all 8 service events |
| lib-mesh (`IServiceNavigator`) | L0 | Hard | Executing cleanup/compression/decompression/migration callbacks via `ExecutePreboundApiAsync` / `ExecutePreboundApiBatchAsync` |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation in helper methods |

**DI Provider interface consumed**: `IEnumerable<ISeededResourceProvider>` — higher-layer plugins implement this to expose embedded resources (ABML behaviors, templates). Resource discovers providers via DI collection and aggregates them through the seeded resource endpoints.

**Special notes**:
- Resource has zero service client dependencies. All cross-service coordination happens via `IServiceNavigator` executing opaque callback endpoint definitions stored as data in Redis. This satisfies L1 hierarchy isolation — Resource never knows what services it calls.
- Account is exempt from lib-resource reference tracking (privacy constraint). Services with account-owned data MUST instead subscribe to `account.deleted` per FOUNDATION TENETS's Account Deletion Cleanup Obligation.

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
| `resource.migrated` | `ResourceMigratedEvent` | ExecuteMigrate on success |
| `resource.migrate.callback-failed` | `ResourceMigrateCallbackFailedEvent` | ExecuteMigrate per failed callback |
| `resource.transaction.created` | `ResourceTransactionCreatedEvent` | BeginTransaction |
| `resource.transaction.committed` | `ResourceTransactionCommittedEvent` | CommitTransaction (or worker auto-commit) |
| `resource.transaction.aborted` | `ResourceTransactionAbortedEvent` | AbortTransaction (or worker auto-abort) when all compensations complete |
| `resource.transaction.auto-committed` | `ResourceTransactionAutoCommittedEvent` | TransactionRecoveryWorker TTL validation → entity exists |
| `resource.transaction.auto-aborted` | `ResourceTransactionAutoAbortedEvent` | TransactionRecoveryWorker TTL validation → entity not found |
| `resource.transaction.commit-failed` | `ResourceTransactionCommitFailedEvent` | TransactionRecoveryWorker commit resume retries exhausted |
| `resource.transaction.compensation-exhausted` | `ResourceTransactionCompensationExhaustedEvent` | TransactionRecoveryWorker compensation retries exhausted |
| `resource.transaction.validation-exhausted` | `ResourceTransactionValidationExhaustedEvent` | TransactionRecoveryWorker TTL validation retries exhausted |

---

## Events Consumed

This plugin does not consume external events. `ResourceServiceEvents.cs` registers no event consumers.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<ResourceService>` | Structured logging |
| `ResourceServiceConfiguration` | Typed configuration access (10 properties) |
| `IStateStoreFactory` | State store access (7 stores + inline cacheable stores for set indexes) |
| `IDistributedLockProvider` | Cleanup and compression distributed locks |
| `IMessageBus` | Event publishing (8 topics) |
| `IServiceNavigator` | Prebound API callback execution for cleanup/compression/decompression/migration |
| `ITelemetryProvider` | Span instrumentation in helper methods |
| `IEventConsumer` | Event fan-out registration (none currently registered) |
| `IEnumerable<ISeededResourceProvider>` | DI provider collection for seeded resource listing/retrieval |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| RegisterReference | POST /resource/register | generated | [] | refs, grace | - |
| UnregisterReference | POST /resource/unregister | generated | [] | refs, grace | resource.grace-period.started |
| CheckReferences | POST /resource/check | generated | [] | - | - |
| ListReferences | POST /resource/list | generated | [] | - | - |
| DefineCleanupCallback | POST /resource/cleanup/define | generated | [] | cleanup-callback, cleanup-index | - |
| ExecuteCleanup | POST /resource/cleanup/execute | generated | [] | grace, refs | resource.cleanup.callback-failed |
| ListCleanupCallbacks | POST /resource/cleanup/list | generated | [] | - | - |
| RemoveCleanupCallback | POST /resource/cleanup/remove | generated | [] | cleanup-callback, cleanup-index | - |
| DefineCompressCallback | POST /resource/compress/define | generated | [] | compress-callback, compress-index | - |
| ExecuteCompress | POST /resource/compress/execute | generated | [] | archive | resource.compress.callback-failed, resource.compressed |
| ExecuteDecompress | POST /resource/decompress/execute | generated | [] | - | resource.decompressed |
| ListCompressCallbacks | POST /resource/compress/list | generated | [] | - | - |
| GetArchive | POST /resource/archive/get | generated | [] | - | - |
| ExecuteSnapshot | POST /resource/snapshot/execute | generated | [] | snapshot | resource.compress.callback-failed, resource.snapshot.created |
| GetSnapshot | POST /resource/snapshot/get | generated | [] | - | - |
| ListSeededResources | POST /resource/seeded/list | generated | [] | - | - |
| GetSeededResource | POST /resource/seeded/get | generated | [] | - | - |
| DefineMigrateCallback | POST /resource/migrate/define | generated | [] | migrate-callback, migrate-index | - |
| ExecuteMigrate | POST /resource/migrate/execute | generated | [] | - | resource.migrated, resource.migrate.callback-failed |
| ListMigrateCallbacks | POST /resource/migrate/list | generated | [] | - | - |
| BeginTransaction | POST /resource/transaction/begin | generated | [] | transactions | resource.transaction.created |
| RegisterProvision | POST /resource/transaction/register-provision | generated | [] | provisions, prov-tx | - |
| ConfirmProvision | POST /resource/transaction/confirm-provision | generated | [] | provisions | - |
| CommitTransaction | POST /resource/transaction/commit | generated | [] | transactions, provisions, refs | resource.transaction.committed |
| AbortTransaction | POST /resource/transaction/abort | generated | [] | transactions, provisions | resource.transaction.aborted |
| GetTransactionStatus | POST /resource/transaction/status | generated | admin | - | - |

---

## Methods

### RegisterReference
POST /resource/register | Roles: []

```
WRITE _refStore:{resourceType}:{resourceId}:sources <- entry // set add, atomic; deduplicates by SourceType+SourceId
IF added
 DELETE _graceStore:{resourceType}:{resourceId}:grace // clear zero-timestamp
READ _refStore:{resourceType}:{resourceId}:sources // set count -> refCount
RETURN (200, RegisterReferenceResponse { newRefCount, alreadyRegistered: !added })
```

---

### UnregisterReference
POST /resource/unregister | Roles: []

```
DELETE _refStore:{resourceType}:{resourceId}:sources <- entry // set remove; equality ignores RegisteredAt
READ _refStore:{resourceType}:{resourceId}:sources // set count -> refCount
IF removed AND refCount == 0
 WRITE _graceStore:{resourceType}:{resourceId}:grace <- GracePeriodRecord { ZeroTimestamp: now }
 PUBLISH resource.grace-period.started { resourceType, resourceId, gracePeriodEndsAt, timestamp }
RETURN (200, UnregisterReferenceResponse { newRefCount, wasRegistered: removed, gracePeriodStartedAt })
```

---

### CheckReferences
POST /resource/check | Roles: []

```
READ _refStore:{resourceType}:{resourceId}:sources // set count -> refCount
READ _refStore:{resourceType}:{resourceId}:sources // full set -> entries
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
POST /resource/list | Roles: []

```
READ _refStore:{resourceType}:{resourceId}:sources // full set -> entries
IF filterSourceType specified
 // filter entries by SourceType
// totalCount = count of filtered (pre-limit) entries
// apply limit via Take(body.Limit)
RETURN (200, ListReferencesResponse { references, totalCount })
```

---

### DefineCleanupCallback
POST /resource/cleanup/define | Roles: []

```
READ _cleanupStore:callback:{resourceType}:{sourceType} // check for existing -> previouslyDefined
WRITE _cleanupStore:callback:{resourceType}:{sourceType} <- CleanupCallbackDefinition
 // serviceName defaults to sourceType; onDeleteAction defaults to Cascade
WRITE _cleanupStore:callback-index:{resourceType} <- sourceType // set add
WRITE _cleanupStore:callback-resource-types <- resourceType // set add
RETURN (200, DefineCleanupResponse { previouslyDefined })
// 200 confirms registration; no `registered` boolean needed.
```

---

### ExecuteCleanup
POST /resource/cleanup/execute | Roles: []

```
// Gather all callbacks for this resourceType
READ _cleanupStore:callback-index:{resourceType} // set -> sourceTypes
FOREACH sourceType
 READ _cleanupStore:callback:{resourceType}:{sourceType}

// Internal reference check (reuses CheckReferences logic)
READ _refStore:{resourceType}:{resourceId}:sources // count + full set
READ _graceStore:{resourceType}:{resourceId}:grace

// Separate callbacks by OnDeleteAction
// restrictCallbacks = callbacks where OnDeleteAction == Restrict
// cascadeDetachCallbacks = callbacks where OnDeleteAction == Cascade or Detach

IF dryRun
 // Analyze RESTRICT violations, unresolved refs, grace period
 // Return preview with hypothetical results
 RETURN (200, ExecuteCleanupResponse { success: false, dryRun: true, callbackResults })

IF any RESTRICT callbacks with active references of that sourceType
 RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Blocked by RESTRICT policy" })

IF unresolved references without registered callbacks
 RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Unhandled references" })

// gracePeriodSeconds: 0 skips grace check (used by ExecuteCompress with deleteSourceData)
IF grace period not elapsed AND gracePeriodSeconds != 0
 RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Grace period active" })

LOCK _refStore:cleanup:{resourceType}:{resourceId}
 IF lock fails
 RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "Failed to acquire cleanup lock" })

 // Re-validate under lock (race protection)
 READ _refStore:{resourceType}:{resourceId}:sources // count
 IF count changed
 READ _refStore:{resourceType}:{resourceId}:sources // full set
 // Re-check RESTRICT violations with updated refs

 // Execute only CASCADE and DETACH callbacks (not RESTRICT)
 CALL _navigator.ExecutePreboundApiBatchAsync(cascadeDetachCallbacks, Parallel, timeout)

 FOREACH failed callback
 PUBLISH resource.cleanup.callback-failed { resourceType, resourceId, sourceType, serviceName, endpoint, statusCode, errorMessage, timestamp }

 IF AllRequired policy AND any failure
 RETURN (200, ExecuteCleanupResponse { success: false, abortReason: "AllRequired callback failed", callbackResults })

 // Cleanup succeeded (or BestEffort with partial success)
 DELETE _graceStore:{resourceType}:{resourceId}:grace
 DELETE _refStore:{resourceType}:{resourceId}:sources // delete entire set

RETURN (200, ExecuteCleanupResponse { success: true, callbackResults, cleanupDurationMs })
// All outcomes return 200; success/failure communicated via response body flags.
```

---

### ListCleanupCallbacks
POST /resource/cleanup/list | Roles: []

```
IF resourceType specified
 READ _cleanupStore:callback-index:{resourceType} // set -> sourceTypes
 FOREACH sourceType
 READ _cleanupStore:callback:{resourceType}:{sourceType}
 IF sourceType filter also specified
 // filter results by sourceType
ELSE
 READ _cleanupStore:callback-resource-types // master set -> resourceTypes
 FOREACH resourceType
 READ _cleanupStore:callback-index:{resourceType} // set -> sourceTypes
 FOREACH sourceType
 READ _cleanupStore:callback:{resourceType}:{sourceType}
RETURN (200, ListCleanupCallbacksResponse { callbacks, totalCount })
```

---

### RemoveCleanupCallback
POST /resource/cleanup/remove | Roles: []

```
READ _cleanupStore:callback:{resourceType}:{sourceType}
IF null
 RETURN (200, RemoveCleanupCallbackResponse { wasRegistered: false, removedAt: null })
DELETE _cleanupStore:callback:{resourceType}:{sourceType}
DELETE _cleanupStore:callback-index:{resourceType} <- sourceType // set remove
READ _cleanupStore:callback-index:{resourceType} // check if set is now empty
IF empty
 DELETE _cleanupStore:callback-resource-types <- resourceType // set remove from master index
RETURN (200, RemoveCleanupCallbackResponse { wasRegistered: true, removedAt: now })
```

---

### DefineCompressCallback
POST /resource/compress/define | Roles: []

```
READ _compressStore:compress-callback:{resourceType}:{sourceType} // check existing -> previouslyDefined
WRITE _compressStore:compress-callback:{resourceType}:{sourceType} <- CompressCallbackDefinition
 // serviceName defaults to sourceType; decompressEndpoint/template are optional
WRITE _compressStore:compress-callback-index:{resourceType} <- sourceType // set add
WRITE _compressStore:compress-callback-resource-types <- resourceType // set add
RETURN (200, DefineCompressCallbackResponse { previouslyDefined })
// 200 confirms registration; no `registered` boolean needed.
```

---

### ExecuteCompress
POST /resource/compress/execute | Roles: []

```
// Get all compression callbacks, sorted by Priority ASC
READ _compressStore:compress-callback-index:{resourceType} // set -> sourceTypes
FOREACH sourceType
 READ _compressStore:compress-callback:{resourceType}:{sourceType}
// Sort by Priority ASC

IF no callbacks
 RETURN (200, ExecuteCompressResponse { success: false, abortReason: "No compression callbacks registered" })

IF dryRun
 RETURN (200, ExecuteCompressResponse { success: false, dryRun: true, callbackResults })

LOCK _compressStore:compress:{resourceType}:{resourceId}
 IF lock fails
 RETURN (200, ExecuteCompressResponse { success: false, abortReason: "Failed to acquire compression lock" })

 FOREACH callback (sorted by priority, sequential)
 CALL _navigator.ExecutePreboundApiAsync(callback.CompressEndpoint, context["resourceId"], timeout)
 IF success
 // GZip compress response body, Base64 encode, compute SHA256 checksum
 // Add to archive entries
 ELSE
 PUBLISH resource.compress.callback-failed { eventId, timestamp, resourceType, resourceId, sourceType, serviceName, endpoint, statusCode, errorMessage }
 IF AllRequired policy
 RETURN (200, ExecuteCompressResponse { success: false, abortReason: "AllRequired callback failed", callbackResults })

 IF no successful callbacks
 RETURN (200, ExecuteCompressResponse { success: false, abortReason: "No successful compression callbacks" })

 // Determine version
 READ _archiveStore:archive:{resourceType}:{resourceId} // existing archive for version
 // version = (existing?.Version ?? 0) + 1

 WRITE _archiveStore:archive:{resourceType}:{resourceId} <- ResourceArchiveModel { archiveId, version, entries, createdAt }

 IF deleteSourceData
 // Internal self-call with gracePeriodSeconds: 0
 CALL ExecuteCleanupAsync({ resourceType, resourceId, gracePeriodSeconds: 0, cleanupPolicy: deleteSourceDataPolicy })
 IF cleanup succeeded
 WRITE _archiveStore:archive:{resourceType}:{resourceId} <- archive { sourceDataDeleted: true }

 PUBLISH resource.compressed { eventId, timestamp, resourceType, resourceId, archiveId, sourceDataDeleted, entriesCount }

RETURN (200, ExecuteCompressResponse { success: true, archiveId, sourceDataDeleted, callbackResults })
// All outcomes return 200; success/failure communicated via response body flags.
```

---

### ExecuteDecompress
POST /resource/decompress/execute | Roles: []

```
READ _archiveStore:archive:{resourceType}:{resourceId}
IF null
 RETURN (200, ExecuteDecompressResponse { success: false, abortReason: "No archive found" })

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
// All outcomes return 200; success = all entries decompressed; callbackResults has per-entry detail.
```

---

### ListCompressCallbacks
POST /resource/compress/list | Roles: []

```
IF resourceType specified
 READ _compressStore:compress-callback-index:{resourceType} // set -> sourceTypes
 FOREACH sourceType
 READ _compressStore:compress-callback:{resourceType}:{sourceType}
 IF sourceType filter also specified
 // filter results by sourceType
ELSE
 READ _compressStore:compress-callback-resource-types // master set -> resourceTypes
 FOREACH resourceType
 READ _compressStore:compress-callback-index:{resourceType}
 FOREACH sourceType
 READ _compressStore:compress-callback:{resourceType}:{sourceType}
RETURN (200, ListCompressCallbacksResponse { callbacks, totalCount })
```

---

### GetArchive
POST /resource/archive/get | Roles: []

```
READ _archiveStore:archive:{resourceType}:{resourceId}
IF null
 RETURN (200, GetArchiveResponse { found: false })
IF archiveId specified AND archive.ArchiveId != archiveId
 RETURN (200, GetArchiveResponse { found: false })
RETURN (200, GetArchiveResponse { found: true, archive })
// All outcomes return 200; found flag indicates presence.
```

---

### ExecuteSnapshot
POST /resource/snapshot/execute | Roles: []

```
// Get compression callbacks (reuses compress callback infrastructure)
READ _compressStore:compress-callback-index:{resourceType} // set -> sourceTypes
FOREACH sourceType
 READ _compressStore:compress-callback:{resourceType}:{sourceType}
// Sort by Priority ASC

IF filterSourceTypes specified
 // Filter callbacks to matching source types only (case-insensitive)

IF no callbacks (after filtering)
 RETURN (200, ExecuteSnapshotResponse { success: false, abortReason: "No callbacks" })

IF dryRun
 RETURN (200, ExecuteSnapshotResponse { success: false, dryRun: true, callbackResults })

// Clamp TTL: ttl = clamp(body.TtlSeconds ?? config.SnapshotDefaultTtlSeconds, config.SnapshotMinTtlSeconds, config.SnapshotMaxTtlSeconds)

FOREACH callback (sorted by priority, sequential)
 CALL _navigator.ExecutePreboundApiAsync(callback.CompressEndpoint, context["resourceId"], timeout)
 IF success
 // GZip compress + Base64 encode + SHA256 checksum (same as ExecuteCompress)
 ELSE
 PUBLISH resource.compress.callback-failed { eventId, timestamp, resourceType, resourceId, sourceType, serviceName, endpoint, statusCode, errorMessage }
 IF AllRequired policy
 RETURN (200, ExecuteSnapshotResponse { success: false, abortReason: "AllRequired callback failed", callbackResults })

IF no successful callbacks
 RETURN (200, ExecuteSnapshotResponse { success: false, abortReason: "No successful snapshot callbacks" })

// snapshotId = new Guid (unique key, no lock needed)
// snapshotType = body.SnapshotType ?? "default"
WRITE _snapshotStore:snap:{snapshotId} <- ResourceSnapshotModel [TTL: ttlSeconds]
PUBLISH resource.snapshot.created { eventId, timestamp, resourceType, resourceId, snapshotId, snapshotType, expiresAt, entriesCount }
RETURN (200, ExecuteSnapshotResponse { success: true, snapshotId, expiresAt, callbackResults })
// All outcomes return 200; success/failure communicated via response body flags.
```

---

### GetSnapshot
POST /resource/snapshot/get | Roles: []

```
READ _snapshotStore:snap:{snapshotId}
IF null
 RETURN (200, GetSnapshotResponse { found: false }) // may have TTL-expired
IF filterSourceTypes specified
 // Filter snapshot entries by source type (case-insensitive)
RETURN (200, GetSnapshotResponse { found: true, snapshot })
// All outcomes return 200; found flag indicates presence.
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
// All outcomes return 200; found flag indicates presence.
```

---

### DefineMigrateCallback
POST /resource/migrate/define | Roles: []

```
READ _migrateStore:callback:{resourceType}:{sourceType} // check for existing -> previouslyDefined
WRITE _migrateStore:callback:{resourceType}:{sourceType} <- MigrateCallbackDefinition
 // serviceName defaults to sourceType
WRITE _migrateStore:callback-index:{resourceType} <- sourceType // set add
RETURN (200, DefineMigrateCallbackResponse { previouslyDefined })
// 200 confirms registration; no `registered` boolean needed.
```

---

### ExecuteMigrate
POST /resource/migrate/execute | Roles: []

```
// Gather all migrate callbacks for this resourceType
READ _migrateStore:callback-index:{resourceType} // set -> sourceTypes
FOREACH sourceType
 READ _migrateStore:callback:{resourceType}:{sourceType}

IF no callbacks
 RETURN (200, ExecuteMigrateResponse { success: false, abortReason: "No migrate callbacks registered" })

LOCK _refStore:migrate:{resourceType}:{sourceResourceId}
 IF lock fails
 RETURN (200, ExecuteMigrateResponse { success: false, abortReason: "Failed to acquire migrate lock" })

 FOREACH callback (sequential)
 // Substitute {{sourceResourceId}} and {{targetResourceId}} in PayloadTemplate
 CALL _navigator.ExecutePreboundApiAsync(callback.CallbackEndpoint, substitutedPayload, timeout)
 IF failure
  PUBLISH resource.migrate.callback-failed { eventId, timestamp, resourceType, sourceResourceId, targetResourceId, sourceType, serviceName, endpoint, statusCode, errorMessage }

 IF AllRequired policy AND any failure
 RETURN (200, ExecuteMigrateResponse { success: false, abortReason: "AllRequired callback failed", callbackResults })

 IF no successful callbacks
 RETURN (200, ExecuteMigrateResponse { success: false, abortReason: "No successful migrate callbacks" })

 PUBLISH resource.migrated { eventId, timestamp, resourceType, sourceResourceId, targetResourceId, callbacksSucceeded, callbacksFailed }

RETURN (200, ExecuteMigrateResponse { success: true, callbackResults })
// All outcomes return 200; success/failure communicated via response body flags.
```

---

### ListMigrateCallbacks
POST /resource/migrate/list | Roles: []

```
IF resourceType specified
 READ _migrateStore:callback-index:{resourceType} // set -> sourceTypes
 FOREACH sourceType
 READ _migrateStore:callback:{resourceType}:{sourceType}
 IF sourceType filter also specified
 // filter results by sourceType
ELSE
 // enumerate all registered resource types from callback-index keys
 // (no master index — iterate known resource types)
RETURN (200, ListMigrateCallbacksResponse { callbacks, totalCount })
```

---

### BeginTransaction
POST /resource/transaction/begin | Roles: []

```
// Clamp TTL to configured range
effectiveTtl = clamp(body.TtlSeconds ?? config.TransactionDefaultTtlSeconds, 10, config.TransactionMaxTtlSeconds)
// Serialize completionValidation PreboundApi for storage (preserves registration-time field values)
serializedValidation = body.CompletionValidation != null ? BannouJson.Serialize(body.CompletionValidation) : null
WRITE _transactionStore:tx:{newTransactionId} <- ResourceTransactionModel {
  TransactionId: newGuid, OwnerService: body.OwnerService,
  ParentResourceType: body.ParentResourceType, ParentResourceId: body.ParentResourceId,
  Status: Active, CreatedAt: now, UpdatedAt: now,
  TtlSeconds: effectiveTtl, ExpectedProvisionCount: body.ExpectedProvisionCount,
  ValidationAttempts: 0, CompletionValidation: serializedValidation
}
PUBLISH resource.transaction.created { transactionId, ownerService, parentResourceType, parentResourceId, ttlSeconds }
RETURN (200, BeginTransactionResponse { transactionId, status: Active, ttlSeconds: effectiveTtl, expiresAt: now + effectiveTtl })
```

---

### RegisterProvision
POST /resource/transaction/register-provision | Roles: []

```
READ _transactionStore:tx:{body.TransactionId}                        -> 404 if null
IF transaction.Status != Active                                       -> 400
// Determine sequence number from existing provisions
READ _provisionStore:prov-tx:{body.TransactionId}                     // list of provisionIds
sequenceNumber = existingProvisions.Count
// Serialize compensation and verification PreboundApi for storage
serializedCompensation = BannouJson.Serialize(body.Compensation)
serializedVerification = body.Verification != null ? BannouJson.Serialize(body.Verification) : null
WRITE _provisionStore:prov:{newProvisionId} <- ResourceProvisionModel {
  ProvisionId: newGuid, TransactionId: body.TransactionId,
  SequenceNumber: sequenceNumber,
  ResourceType: body.ResourceType, ResourceId: body.ResourceId,
  Status: Pending, RegisteredAt: now,
  CompensationAttempts: 0,
  Compensation: serializedCompensation, Verification: serializedVerification
}
WRITE _provisionStore:prov-tx:{body.TransactionId} <- append newProvisionId
RETURN (200, RegisterProvisionResponse { provisionId, sequenceNumber, status: Pending })
```

---

### ConfirmProvision
POST /resource/transaction/confirm-provision | Roles: []

```
READ _transactionStore:tx:{body.TransactionId}                        -> 404 if null
IF transaction.Status != Active                                       -> 400
// Find provision by resourceId within this transaction
READ _provisionStore:prov-tx:{body.TransactionId}                     // list of provisionIds
FOREACH provisionId in list
  READ _provisionStore:prov:{provisionId}
  IF provision.ResourceId == body.ResourceId                          -> found
IF not found                                                          -> 404
IF provision.Status != Pending                                        -> 400
provision.Status = Provisioned
provision.ProvisionedAt = now
WRITE _provisionStore:prov:{provision.ProvisionId} <- updated
RETURN (200, ConfirmProvisionResponse { provisionId, status: Provisioned })
```

---

### CommitTransaction
POST /resource/transaction/commit | Roles: []

```
READ _transactionStore:tx:{body.TransactionId} [with ETag]            -> 404 if null
IF transaction.Status != Active                                       -> 400

// Phase 1: Transition to Committing (single atomic write — crash-safe checkpoint)
transaction.Status = Committing
transaction.UpdatedAt = now
ETAG-WRITE _transactionStore:tx:{body.TransactionId}                  -> 409 if ETag conflict (R10)

// Phase 2: Register references one by one, checkpointing each provision
READ _provisionStore:prov-tx:{body.TransactionId}                     // ordered list
FOREACH provisionId in list (by sequenceNumber)
  READ _provisionStore:prov:{provisionId}
  IF provision.Status != Provisioned                                  -> skip (already registered or not confirmed)
  // Register as permanent reference via existing internal path
  CALL RegisterReferenceInternal({
    ResourceType: transaction.ParentResourceType,
    ResourceId: transaction.ParentResourceId,
    SourceType: provision.ResourceType,
    SourceId: provision.ResourceId.ToString()
  })
  provision.Status = ReferenceRegistered
  WRITE _provisionStore:prov:{provisionId} <- updated                 // checkpoint

referencesRegistered = count of provisions transitioned to ReferenceRegistered

// Phase 3: Transition to Committed
transaction.Status = Committed
transaction.UpdatedAt = now
WRITE _transactionStore:tx:{body.TransactionId} <- updated
PUBLISH resource.transaction.committed { transactionId, ownerService, parentResourceType, parentResourceId, provisionCount: referencesRegistered }
RETURN (200, CommitTransactionResponse { transactionId, status: Committed, referencesRegistered })
```

---

### AbortTransaction
POST /resource/transaction/abort | Roles: []

```
READ _transactionStore:tx:{body.TransactionId} [with ETag]            -> 404 if null
IF transaction.Status NOT IN [Active, Aborting]                       -> 400

// Transition to Aborting
transaction.Status = Aborting
transaction.UpdatedAt = now
ETAG-WRITE _transactionStore:tx:{body.TransactionId}                  -> 409 if ETag conflict (R10)

// Compensate provisions in reverse sequence order
READ _provisionStore:prov-tx:{body.TransactionId}
SORT provisions by SequenceNumber DESCENDING
compensatedCount = 0, failedCount = 0, pendingCount = 0
FOREACH provisionId in reversed list
  READ _provisionStore:prov:{provisionId}
  IF provision.Status == Pending
    // Resource was never created — mark as compensated directly (no-op)
    provision.Status = Compensated
    provision.CompensatedAt = now
    WRITE _provisionStore:prov:{provisionId} <- updated
    pendingCount++
    CONTINUE
  IF provision.Status IN [ReferenceRegistered, Provisioned, CompensationFailed]
    // Execute compensation via prebound API
    compensationApi = BannouJson.Deserialize<PreboundApi>(provision.Compensation)
    context = { "provisionResourceId": provision.ResourceId.ToString() }
    result = CALL _navigator.ExecutePreboundApiAsync(compensationApi, context)
    // Apply response transformation if configured on the PreboundApi
    transformedResult = ResponseTransformer.Transform(result.StatusCode, result.ResponseBody, compensationApi.ResponseTransformation)
    IF transformedResult.IsSuccess OR result.StatusCode == 404
      // 404 = resource doesn't exist = compensation succeeded (per planning doc)
      provision.Status = Compensated
      provision.CompensatedAt = now
      compensatedCount++
    ELSE
      provision.Status = CompensationFailed
      provision.CompensationAttempts++
      provision.LastCompensationError = result.ErrorMessage ?? transformedResult.Payload
      failedCount++
    WRITE _provisionStore:prov:{provisionId} <- updated

IF failedCount == 0
  transaction.Status = Aborted
  transaction.UpdatedAt = now
  WRITE _transactionStore:tx:{body.TransactionId} <- updated
  PUBLISH resource.transaction.aborted { transactionId, ownerService, parentResourceType, parentResourceId, compensatedCount, failedCount: 0 }
// ELSE: remain Aborting — worker will retry failed compensations

RETURN (200, AbortTransactionResponse { transactionId, status: transaction.Status, compensatedCount, failedCount, pendingCount })
```

---

### GetTransactionStatus
POST /resource/transaction/status | Roles: [admin]

```
READ _transactionStore:tx:{body.TransactionId}                        -> 404 if null
READ _provisionStore:prov-tx:{body.TransactionId}
provisions = []
FOREACH provisionId in list
  READ _provisionStore:prov:{provisionId}
  provisions.add(ProvisionDetail from provision model)
RETURN (200, TransactionStatusResponse {
  transactionId, ownerService, parentResourceType, parentResourceId,
  status, createdAt, updatedAt, ttlSeconds,
  expiresAt: createdAt + ttlSeconds,
  expectedProvisionCount, validationAttempts,
  provisions: sorted by sequenceNumber
})
```

---

## Background Services

### TransactionRecoveryWorker
**Interval**: `config.TransactionRecoveryWorkerIntervalSeconds` (default: 30s)
**Startup Delay**: `config.TransactionRecoveryWorkerStartupDelaySeconds` (default: 15s)
**Purpose**: Recovers transactions that were not properly committed or aborted — TTL validation, commit resume, compensation retry, metadata retention purge.

Follows the canonical BackgroundService polling loop pattern (FOUNDATION TENETS T6):
- Startup delay with cancellation handler
- Double-catch filter: `OperationCanceledException when stoppingToken.IsCancellationRequested` before generic `Exception`
- Per-cycle telemetry span
- Error event publishing via `WorkerErrorPublisher.TryPublishWorkerErrorAsync`
- Store access: resolve `IStateStoreFactory` from DI scope once per cycle, call `GetStore<T>()` for each needed store immediately, pass store references as parameters

```
// Scoped: resolve transaction + provision stores once per cycle
// Per-item error isolation on every transaction processed (IMPLEMENTATION TENETS T7)

// ─── Scan 1: TTL Validation (Active transactions past expiry) ───
QUERY _transactionStore WHERE $.Status == Active
FOREACH transaction (per-item try-catch)
  expiresAt = transaction.CreatedAt + TimeSpan.FromSeconds(transaction.TtlSeconds)
  IF now < expiresAt -> skip (not yet expired)

  // Count current provisions
  READ _provisionStore:prov-tx:{transaction.TransactionId}
  provisionCount = list.Count

  IF transaction.ExpectedProvisionCount != null AND provisionCount < transaction.ExpectedProvisionCount
    // Orchestrator crashed before finishing provisioning — auto-abort
    CALL AbortTransactionAsync({ TransactionId: transaction.TransactionId, Reason: "TTL expired, provision count not met" })
    PUBLISH resource.transaction.auto-aborted { ... }
    CONTINUE

  IF transaction.ValidationAttempts >= config.TransactionValidationMaxRetries
    // Validation exhausted — remain Active for admin
    PUBLISH resource.transaction.validation-exhausted { ..., validationAttempts }
    CONTINUE

  IF transaction.CompletionValidation == null
    // No validation check — conservative auto-abort
    CALL AbortTransactionAsync({ TransactionId: transaction.TransactionId, Reason: "TTL expired, no completion validation" })
    PUBLISH resource.transaction.auto-aborted { ... }
    CONTINUE

  // Execute completion validation via prebound API
  validationApi = BannouJson.Deserialize<PreboundApi>(transaction.CompletionValidation)
  context = { "parentResourceId": transaction.ParentResourceId.ToString() }
  result = CALL _navigator.ExecutePreboundApiAsync(validationApi, context)
  transformedResult = ResponseTransformer.Transform(result.StatusCode, result.ResponseBody, validationApi.ResponseTransformation)

  IF transformedResult.Outcome == TransientFailure
    // Unreachable — increment attempts and retry next cycle
    transaction.ValidationAttempts++
    transaction.UpdatedAt = now
    WRITE _transactionStore:tx:{transaction.TransactionId} <- updated
    CONTINUE

  IF transformedResult.IsSuccess
    // Entity exists — auto-commit
    CALL CommitTransactionAsync({ TransactionId: transaction.TransactionId })
    PUBLISH resource.transaction.auto-committed { ... }
  ELSE
    // Entity does not exist (4xx) — auto-abort
    CALL AbortTransactionAsync({ TransactionId: transaction.TransactionId, Reason: "TTL validation: entity not found" })
    PUBLISH resource.transaction.auto-aborted { ... }

// ─── Scan 2: Resume Commit (Committing transactions with uncheckpointed provisions) ───
QUERY _transactionStore WHERE $.Status == Committing
FOREACH transaction (per-item try-catch)
  // Resume from the first provision not yet ReferenceRegistered
  // Same logic as CommitTransaction Phase 2, starting from checkpoint
  READ _provisionStore:prov-tx:{transaction.TransactionId}
  unregistered = provisions WHERE Status == Provisioned
  IF unregistered is empty
    // All registered — finalize to Committed
    transaction.Status = Committed; transaction.UpdatedAt = now
    WRITE _transactionStore; PUBLISH resource.transaction.committed
    CONTINUE
  // Attempt to register remaining references
  // On failure: increment internal retry counter
  // If retries exhausted (config.TransactionCommitMaxRetries):
  //   PUBLISH resource.transaction.commit-failed
  //   transition to Aborting (compensate instead of commit)

// ─── Scan 3: Compensation Retry (Aborting transactions with CompensationFailed provisions) ───
QUERY _transactionStore WHERE $.Status == Aborting
FOREACH transaction (per-item try-catch)
  READ _provisionStore:prov-tx:{transaction.TransactionId}
  failed = provisions WHERE Status == CompensationFailed
  IF failed is empty
    // All compensated — finalize to Aborted
    transaction.Status = Aborted; transaction.UpdatedAt = now
    WRITE _transactionStore; PUBLISH resource.transaction.aborted
    CONTINUE
  FOREACH provision in failed (reverse sequence order)
    IF provision.CompensationAttempts >= config.TransactionCompensationMaxRetries
      // Exhausted — leave as CompensationFailed, publish error
      PUBLISH resource.transaction.compensation-exhausted { ... failedProvisionCount }
      CONTINUE
    // Exponential backoff: skip if not enough time has passed since last attempt
    backoffSeconds = config.TransactionCompensationBackoffBaseSeconds * (2 ^ (provision.CompensationAttempts - 1))
    // Retry compensation (same logic as AbortTransaction per-provision)
  IF all failed provisions now exhausted or compensated
    transaction.Status = Aborted; transaction.UpdatedAt = now
    WRITE _transactionStore

// ─── Scan 4: Metadata Retention Purge ───
QUERY _transactionStore WHERE $.Status IN [Committed, Aborted]
FOREACH transaction (per-item try-catch)
  age = now - transaction.UpdatedAt
  IF age.TotalDays < config.TransactionRetentionDays -> skip
  // Purge transaction + all provisions
  READ _provisionStore:prov-tx:{transaction.TransactionId}
  FOREACH provisionId: DELETE _provisionStore:prov:{provisionId}
  DELETE _provisionStore:prov-tx:{transaction.TransactionId}
  DELETE _transactionStore:tx:{transaction.TransactionId}
```

---

## Non-Standard Implementation Patterns

**All-200 response pattern (existing endpoints)**: Unlike most Bannou services that use HTTP status codes (404, 409, etc.) to communicate business failures, Resource's existing endpoints (reference, cleanup, compress, migrate, snapshot, seeded) return `200 OK` for all outcomes and use structured response flags (`success`, `found`, `abortReason`) to communicate results. This is intentional — Resource's callers are other Bannou services performing orchestration, and they need structured callback results (per-entry success/failure details, abort reasons, duration metrics) that cannot be expressed through HTTP status codes alone.

**Transaction endpoints use proper status codes**: The new transaction management endpoints (begin, register-provision, confirm-provision, commit, abort, status) use standard HTTP status codes (400, 404, 409) for business failures. This is a deliberate break from the all-200 pattern for two reasons: (1) transaction callers need clear, immediate signal on whether an operation succeeded without parsing response bodies for success flags; (2) transaction operations are simpler request/response pairs, not multi-callback orchestrations where per-callback detail necessitates structured responses. Per the planning document R8: "Either existing Resource moves to proper status codes, or new endpoints follow the existing convention. This is an implementation-time decision."

**Migrate callbacks mirror cleanup callbacks**: The `resource-migrate` store uses the same key patterns and indexing strategy as `resource-cleanup`. Migration callbacks substitute `{{sourceResourceId}}` and `{{targetResourceId}}` in payload templates (vs. cleanup's `{{resourceId}}`), enabling dependent services to re-point their references from one resource to another.

**Transaction PreboundApi fields stored as serialized JSON**: Transaction and provision models store `PreboundApi` objects as `BannouJson.Serialize()`'d strings in MySQL. This preserves all fields at registration-time values — if `PreboundApi` gains new fields (TimeoutSeconds, Headers, etc.), stored transactions retain their registration-time values instead of silently inheriting new defaults. Deserialized via `BannouJson.Deserialize<PreboundApi>()` at execution time.

**Response transformation in compensation and validation**: When executing compensation or completion validation prebound APIs, the worker runs `ResponseTransformer.Transform()` on the raw response BEFORE interpreting the result. This allows the prebound API creator to declaratively define what "success" and "failure" mean in their context (e.g., a 200 with `{"error": "..."}` in the body can be transformed to a failure). If no `ResponseTransformation` is configured on the `PreboundApi`, the raw response passes through unchanged.

**Optimistic concurrency on transaction state transitions (R10)**: Concurrent requests to the same transaction (e.g., orchestrator calls Abort while worker auto-aborts) are handled by `GetWithETagAsync` + `TrySaveAsync` on the transaction record. The loser gets Conflict and retries or no-ops (state already transitioned).
