# Save-Load Plugin Deep Dive

> **Plugin**: lib-save-load
> **Schema**: schemas/save-load-api.yaml
> **Version**: 1.0.0
> **State Stores**: save-load-slots (MySQL), save-load-versions (MySQL), save-load-schemas (MySQL), save-load-cache (Redis), save-load-pending (Redis)

---

## Overview

Generic save/load system for game state persistence with polymorphic ownership, versioned saves, and schema migration. Handles the full lifecycle of save data: slot creation (namespaced by game+owner), writing save data with automatic compression, loading with hot cache acceleration, delta/incremental saves via JSON Patch (RFC 6902), schema version registration with forward migration paths, version pinning/promotion, rolling cleanup based on category-specific retention limits, export/import via ZIP archives through the Asset service, and content hash integrity verification. Features a two-tier storage architecture where saves are immediately acknowledged in Redis hot cache and asynchronously uploaded to MinIO via the Asset service through a background worker with circuit breaker protection. Supports five save categories (QUICK_SAVE, AUTO_SAVE, MANUAL_SAVE, CHECKPOINT, STATE_SNAPSHOT) each with distinct compression and retention defaults. Designed for multi-device cloud sync with conflict detection windowing. Owners can be accounts, characters, sessions, or realms (polymorphic association pattern).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for slots, versions, schemas; Redis for hot cache and pending queue |
| lib-state (`IDistributedLockProvider`) | Slot-level locks for concurrent version number allocation |
| lib-messaging (`IMessageBus`) | Publishing save lifecycle events; error event publishing |
| lib-asset (`IAssetClient` via mesh) | Upload/download save data blobs, thumbnail storage, asset deletion |
| `IHttpClientFactory` | Downloading save data from pre-signed Asset service URLs |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-escrow | Could use save-load for transaction state snapshots (potential, not currently wired) |
| lib-game-session | SESSION-owned saves are cleaned up with grace period after session ends |

---

## State Storage

**Stores**: 5 state stores

| Store | Backend | Purpose |
|-------|---------|---------|
| `save-load-slots` | MySQL | Slot metadata and ownership |
| `save-load-versions` | MySQL | Version manifests and delta chain metadata |
| `save-load-schemas` | MySQL | Registered save data schemas for migration |
| `save-load-cache` | Redis (`saveload:cache`) | Hot cache for recently accessed save data |
| `save-load-pending` | Redis (`saveload:pending`) | Async upload queue and circuit breaker state |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `slot:{gameId}:{ownerType}:{ownerId}:{slotName}` | `SaveSlotMetadata` | Slot definition and counters |
| `version:{slotId}:{versionNumber}` | `SaveVersionManifest` | Version metadata, asset refs, delta info |
| `{namespace}:{schemaVersion}` | `SaveSchemaDefinition` | Schema definition and migration patch |
| `hot:{slotId}:{versionNumber}` | `HotSaveEntry` | Cached save data (base64, may be compressed) |
| `hot:{slotId}:latest` | `HotSaveEntry` | Latest version shortcut key |
| `pending:{uploadId}` | `PendingUploadEntry` | Queued upload with data and retry state |
| `pending-upload-ids` (set) | `Set<string>` | Tracking set of pending upload IDs |
| `circuit:storage` | `CircuitBreakerState` | Circuit breaker state (Closed/Open/HalfOpen) |
| `save-rate:{gameId}:{ownerType}:{ownerId}:{minuteBucket}` | `string` (count) | Rate limiting counter (TTL 120s) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `save-load.save.created` | `SaveCreatedEvent` | New save version created (including promotions) |
| `save-load.save.loaded` | `SaveLoadedEvent` | Save data loaded from storage |
| `save-load.save.migrated` | `SaveMigratedEvent` | Save migrated to new schema version |
| `save-load.version.pinned` | `VersionPinnedEvent` | Version pinned as checkpoint |
| `save-load.version.unpinned` | `VersionUnpinnedEvent` | Version unpinned |
| `save-load.version.deleted` | `VersionDeletedEvent` | Version explicitly deleted |
| `save-load.cleanup.completed` | `CleanupCompletedEvent` | Scheduled cleanup cycle completed |
| `save-load.upload.queued` | `SaveQueuedEvent` | Save queued for async upload to MinIO |
| `save-load.upload.completed` | `SaveUploadCompletedEvent` | Async upload to MinIO succeeded |
| `save-load.upload.failed` | `SaveUploadFailedEvent` | Async upload failed after all retries |
| `save-load.circuit-breaker.state-changed` | `CircuitBreakerStateChangedEvent` | Storage circuit breaker state transition |

**Lifecycle Events** (auto-generated from `x-lifecycle`):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `save-slot.created` | `SaveSlotCreatedEvent` | Slot created |
| `save-slot.updated` | `SaveSlotUpdatedEvent` | Slot metadata updated |
| `save-slot.deleted` | `SaveSlotDeletedEvent` | Slot deleted |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxSaveSizeBytes` | `SAVE_LOAD_MAX_SAVE_SIZE_BYTES` | `104857600` (100MB) | Maximum single save size |
| `AutoCompressThresholdBytes` | `SAVE_LOAD_AUTO_COMPRESS_THRESHOLD_BYTES` | `1048576` (1MB) | Auto-compress saves above this size |
| `DefaultCompressionType` | `SAVE_LOAD_DEFAULT_COMPRESSION_TYPE` | `GZIP` | Default compression algorithm |
| `HotCacheTtlMinutes` | `SAVE_LOAD_HOT_CACHE_TTL_MINUTES` | `60` | Hot cache entry TTL |
| `AssetBucket` | `SAVE_LOAD_ASSET_BUCKET` | `game-saves` | MinIO bucket for save assets |
| `DefaultMaxVersionsQuickSave` | `SAVE_LOAD_DEFAULT_MAX_VERSIONS_QUICK_SAVE` | `1` | Max versions for QUICK_SAVE |
| `DefaultMaxVersionsAutoSave` | `SAVE_LOAD_DEFAULT_MAX_VERSIONS_AUTO_SAVE` | `5` | Max versions for AUTO_SAVE |
| `DefaultMaxVersionsManualSave` | `SAVE_LOAD_DEFAULT_MAX_VERSIONS_MANUAL_SAVE` | `10` | Max versions for MANUAL_SAVE |
| `DefaultMaxVersionsCheckpoint` | `SAVE_LOAD_DEFAULT_MAX_VERSIONS_CHECKPOINT` | `20` | Max versions for CHECKPOINT |
| `DefaultMaxVersionsStateSnapshot` | `SAVE_LOAD_DEFAULT_MAX_VERSIONS_STATE_SNAPSHOT` | `3` | Max versions for STATE_SNAPSHOT |
| `CleanupIntervalMinutes` | `SAVE_LOAD_CLEANUP_INTERVAL_MINUTES` | `60` | Scheduled cleanup interval |
| `CleanupStartupDelaySeconds` | `SAVE_LOAD_CLEANUP_STARTUP_DELAY_SECONDS` | `30` | Delay before cleanup starts |
| `CleanupControlPlaneOnly` | `SAVE_LOAD_CLEANUP_CONTROL_PLANE_ONLY` | `true` | Only run cleanup on control plane |
| `SessionCleanupGracePeriodMinutes` | `SAVE_LOAD_SESSION_CLEANUP_GRACE_PERIOD_MINUTES` | `5` | Grace period for SESSION saves |
| `MigrationsEnabled` | `SAVE_LOAD_MIGRATIONS_ENABLED` | `true` | Enable schema migrations |
| `MigrationMaxPatchOperations` | `SAVE_LOAD_MIGRATION_MAX_PATCH_OPERATIONS` | `1000` | Max JSON Patch ops per migration |
| `MaxSlotsPerOwner` | `SAVE_LOAD_MAX_SLOTS_PER_OWNER` | `100` | Maximum slots per owner |
| `MaxSavesPerMinute` | `SAVE_LOAD_MAX_SAVES_PER_MINUTE` | `10` | Rate limit per owner per minute |
| `MaxTotalSizeBytesPerOwner` | `SAVE_LOAD_MAX_TOTAL_SIZE_BYTES_PER_OWNER` | `1073741824` (1GB) | Max storage per owner |
| `BrotliCompressionLevel` | `SAVE_LOAD_BROTLI_COMPRESSION_LEVEL` | `6` | Brotli level (0-11) |
| `GzipCompressionLevel` | `SAVE_LOAD_GZIP_COMPRESSION_LEVEL` | `6` | GZIP level (1-9) |
| `DefaultCompressionByCategory` | - | (object) | Per-category compression overrides |
| `ThumbnailMaxSizeBytes` | `SAVE_LOAD_THUMBNAIL_MAX_SIZE_BYTES` | `262144` (256KB) | Max thumbnail size |
| `ThumbnailAllowedFormats` | `SAVE_LOAD_THUMBNAIL_ALLOWED_FORMATS` | `image/jpeg,image/webp,image/png` | Allowed thumbnail MIME types |
| `DeltaSavesEnabled` | `SAVE_LOAD_DELTA_SAVES_ENABLED` | `true` | Enable delta/incremental saves |
| `DefaultDeltaAlgorithm` | `SAVE_LOAD_DEFAULT_DELTA_ALGORITHM` | `JSON_PATCH` | Default delta algorithm |
| `MaxDeltaChainLength` | `SAVE_LOAD_MAX_DELTA_CHAIN_LENGTH` | `10` | Max deltas before forced collapse |
| `AutoCollapseEnabled` | `SAVE_LOAD_AUTO_COLLAPSE_ENABLED` | `true` | Auto-collapse during cleanup |
| `DeltaSizeThresholdPercent` | `SAVE_LOAD_DELTA_SIZE_THRESHOLD_PERCENT` | `50` | Store full if delta exceeds this % |
| `MinBaseSizeForDeltaThresholdBytes` | `SAVE_LOAD_MIN_BASE_SIZE_FOR_DELTA_THRESHOLD_BYTES` | `1024` | Min base size for threshold logic |
| `ConflictDetectionEnabled` | `SAVE_LOAD_CONFLICT_DETECTION_ENABLED` | `true` | Enable device conflict detection |
| `ConflictDetectionWindowMinutes` | `SAVE_LOAD_CONFLICT_DETECTION_WINDOW_MINUTES` | `5` | Conflict detection time window |
| `AsyncUploadEnabled` | `SAVE_LOAD_ASYNC_UPLOAD_ENABLED` | `true` | Queue uploads vs synchronous write |
| `PendingUploadTtlMinutes` | `SAVE_LOAD_PENDING_UPLOAD_TTL_MINUTES` | `60` | TTL for pending uploads in Redis |
| `MaxConcurrentUploads` | `SAVE_LOAD_MAX_CONCURRENT_UPLOADS` | `10` | Upload semaphore limit |
| `UploadBatchSize` | `SAVE_LOAD_UPLOAD_BATCH_SIZE` | `5` | Uploads per batch cycle |
| `UploadBatchIntervalMs` | `SAVE_LOAD_UPLOAD_BATCH_INTERVAL_MS` | `100` | Batch processing interval |
| `UploadRetryAttempts` | `SAVE_LOAD_UPLOAD_RETRY_ATTEMPTS` | `3` | Max retry attempts |
| `UploadRetryDelayMs` | `SAVE_LOAD_UPLOAD_RETRY_DELAY_MS` | `1000` | Base retry delay (exponential backoff) |
| `StorageCircuitBreakerEnabled` | `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_ENABLED` | `true` | Enable circuit breaker |
| `StorageCircuitBreakerThreshold` | `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_THRESHOLD` | `5` | Consecutive failures to open |
| `StorageCircuitBreakerResetSeconds` | `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_RESET_SECONDS` | `30` | Time before half-open attempt |
| `StorageCircuitBreakerHalfOpenAttempts` | `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_HALF_OPEN_ATTEMPTS` | `2` | Successes needed to close |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<SaveLoadService>` | Scoped | Structured logging |
| `SaveLoadServiceConfiguration` | Singleton | All 40+ config properties |
| `IStateStoreFactory` | Singleton | MySQL+Redis state store access |
| `IDistributedLockProvider` | Singleton | Slot-level locks for version allocation |
| `IMessageBus` | Scoped | Event publishing |
| `IAssetClient` | Scoped | Save data blob upload/download/delete |
| `IHttpClientFactory` | Singleton | HTTP client for pre-signed URL downloads |
| `IVersionDataLoader` | Scoped | Hot cache access, asset retrieval, delta chain reconstruction |
| `IVersionCleanupManager` | Scoped | Rolling cleanup, version deletion with asset cleanup |
| `ISaveExportImportManager` | Scoped | Export archives (ZIP creation), import with conflict resolution |
| `ISaveMigrationHandler` | Scoped | Schema registration, migration path discovery and application |

**Background Services** (registered as `IHostedService`):

| Service | Role |
|---------|------|
| `SaveUploadWorker` | Processes async upload queue from Redis to MinIO via Asset service |
| `CleanupService` | Scheduled cleanup of expired versions and empty slots |

**Internal Helpers** (not DI-registered, instantiated directly):

| Helper | Role |
|--------|------|
| `DeltaProcessor` | JSON Patch computation and application (RFC 6902) |
| `SchemaMigrator` | Migration path BFS and patch application |
| `CompressionHelper` | Static GZIP/Brotli compression/decompression |
| `ContentHasher` | Static SHA-256 hash computation and verification |
| `StorageCircuitBreaker` | Distributed circuit breaker state management (Redis-backed) |

Service lifetime is **Scoped** (per-request). Two background services run continuously.

---

## API Endpoints (Implementation Notes)

### Slot Operations (6 endpoints)

- **CreateSlot** (`/save-load/slot/create`): Validates slot does not already exist (composite key: gameId+ownerType+ownerId+slotName). Checks MaxSlotsPerOwner limit via queryable store. Resolves category-specific defaults for MaxVersions and CompressionType. Generates UUID slot ID. Saves to MySQL store. Returns SlotResponse.

- **GetSlot** (`/save-load/slot/get`): Direct key lookup by composite slot key. Returns 404 if not found. Maps internal SaveSlotMetadata to SlotResponse.

- **ListSlots** (`/save-load/slot/list`): Queries slots by ownerId+ownerType using queryable store. Optional gameId and category filters applied. Returns list of SlotResponse.

- **RenameSlot** (`/save-load/slot/rename`): Loads existing slot by composite key. Creates new slot entry with updated name. Deletes old key. Updates slot metadata timestamps.

- **DeleteSlot** (`/save-load/slot/delete`): Loads slot. Queries all versions. Deletes each version's asset via Asset service (best-effort). Deletes version manifests. Deletes slot metadata. Publishes version deleted events for each version.

- **BulkDeleteSlots** (`/save-load/slot/bulk-delete`): Takes array of slot identifiers. Iterates and performs DeleteSlot logic for each. Returns count of successfully deleted slots. Continues on individual failures.

### Save Operations (4 endpoints)

- **Save** (`/save-load/save`): Core save operation. Acquires slot-level distributed lock to prevent concurrent version allocation. Decodes base64 data. Computes SHA-256 content hash. Applies compression based on category defaults and AutoCompressThreshold. Checks MaxSaveSizeBytes. Allocates next version number. Stores in hot cache. Queues for async upload (PendingUploadEntry + tracking set). If device conflict detection enabled, flags conflicts within ConflictDetectionWindowMinutes. Runs rolling cleanup via IVersionCleanupManager. Publishes SaveCreatedEvent.

- **SaveDelta** (`/save-load/save-delta`): Delta/incremental save. Loads base version data (from hot cache or Asset service). Computes JSON Patch between base and new data via DeltaProcessor. Applies DeltaSizeThresholdPercent check (if delta larger than threshold% of base, stores as full save instead). Stores delta version manifest with IsDelta=true, BaseVersionNumber, DeltaAlgorithm. Same async upload pipeline as full saves.

- **CollapseDeltas** (`/save-load/collapse-deltas`): Reconstructs full data from delta chain (walks BaseVersionNumber references back to full snapshot, applies patches in order). Stores result as new full version. Useful for reducing load-time latency of long chains. Does NOT delete the delta versions (cleanup handles that).

- **Load** (`/save-load/load`): Loads save data from specified slot+version (or latest if version omitted). Tries hot cache first (HotSaveEntry by slotId+versionNumber). Falls back to Asset service download via pre-signed URL. Decompresses data based on CompressionType. Verifies content hash integrity. Caches result in hot store for future access. Publishes SaveLoadedEvent.

- **LoadWithDeltas** (`/save-load/load-with-deltas`): Loads save data, handling delta chain reconstruction transparently. If target version is a delta, walks the chain back to base snapshot via IVersionDataLoader.ReconstructFromDeltaChainAsync. Applies deltas in order. Returns fully reconstructed data.

### Query Operations (1 endpoint)

- **Query** (`/save-load/query`): Queries saves with filters (gameId, ownerType, ownerId, category, tags). Uses queryable store on slots. Returns matching slots with their latest version metadata.

### Version Operations (5 endpoints)

- **ListVersions** (`/save-load/version/list`): Finds slot by owner+name (uses FindSlotByOwnerAndNameAsync helper). Queries version manifests for slotId. Sorts by version number descending. Maps to VersionResponse list.

- **DeleteVersion** (`/save-load/version/delete`): Prevents deletion of pinned versions (returns BadRequest). Deletes version manifest. Deletes associated asset via Asset service. Deletes hot cache entry. Updates slot counters (VersionCount, TotalSizeBytes). Publishes VersionDeletedEvent.

- **PinVersion** (`/save-load/version/pin`): Sets IsPinned=true on version manifest. Optionally sets CheckpointName. Pinned versions are excluded from rolling cleanup. Publishes VersionPinnedEvent.

- **UnpinVersion** (`/save-load/version/unpin`): Sets IsPinned=false. Clears CheckpointName. Version becomes eligible for cleanup. Publishes VersionUnpinnedEvent.

- **PromoteVersion** (`/save-load/version/promote`): Promotes an old version to become the new latest. Acquires slot lock for version number allocation. Loads source version data (hot cache or Asset service). Creates new version manifest (copies metadata, adds "promotedFrom" marker). Stores in hot cache and queues for async upload. Increments slot counters. Runs rolling cleanup. Publishes SaveCreatedEvent.

### Migration Operations (3 endpoints)

- **RegisterSchema** (`/save-load/schema/register`): Delegates to ISaveMigrationHandler. Stores SaveSchemaDefinition with namespace, version, JSON Schema, previousVersion, and JSON Patch migration operations. Builds forward migration graph.

- **ListSchemas** (`/save-load/schema/list`): Delegates to ISaveMigrationHandler. Queries all schemas in a namespace. Returns list with version, previousVersion, hasMigration flag, and creation timestamp.

- **MigrateSave** (`/save-load/migrate`): Delegates to ISaveMigrationHandler. Uses SchemaMigrator to find migration path (BFS through version graph). Applies JSON Patch operations for each step. Creates new version with migrated data and updated SchemaVersion. Publishes SaveMigratedEvent.

### Transfer Operations (3 endpoints)

- **Copy** (`/save-load/copy`): Loads source version data. Creates target slot if needed (or uses existing). Creates new version in target slot with copied data. Independent of source (no references). Publishes SaveCreatedEvent on target.

- **Export** (`/save-load/export`): Delegates to ISaveExportImportManager. Queries matching slots. For each slot, loads latest version data. Creates ZIP archive with manifest.json and per-slot data files. Uploads archive to Asset service. Returns download URL.

- **Import** (`/save-load/import`): Delegates to ISaveExportImportManager. Downloads archive from Asset service. Extracts manifest and data files. Creates slots with conflict resolution (skip, overwrite, rename). Creates versions for each imported slot. Returns import statistics.

### Validation Operations (1 endpoint)

- **Verify** (`/save-load/verify`): Loads version data. Computes SHA-256 hash of loaded data. Compares against stored ContentHash in version manifest. Returns integrity status (match/mismatch) with hash details.

### Admin Operations (2 endpoints)

- **AdminCleanup** (`/save-load/admin/cleanup`): Manual cleanup with filters (ownerType, category, olderThanDays). Supports dryRun mode (counts without deleting). Skips pinned versions. Deletes version manifests and assets. Removes empty slots. Returns versionsDeleted, slotsDeleted, bytesFreed.

- **AdminStats** (`/save-load/admin/stats`): Aggregates statistics across all saves. Supports groupBy (owner_type, category, schema_version). Returns totalSlots, totalVersions, totalSizeBytes, pinnedVersions, and breakdown array.

---

## Visual Aid

```
Delta Chain System
====================

  Version 1 (Full Snapshot)      Version 2 (Delta)          Version 3 (Delta)
  ┌─────────────────────┐      ┌─────────────────────┐    ┌─────────────────────┐
  │ IsDelta: false      │      │ IsDelta: true       │    │ IsDelta: true       │
  │ BaseVersion: null   │ <─── │ BaseVersion: 1      │ <──│ BaseVersion: 2      │
  │ Data: {full JSON}   │      │ Data: [JSON Patch]  │    │ Data: [JSON Patch]  │
  │ Algorithm: null     │      │ Algorithm: JSON_PATCH│    │ Algorithm: JSON_PATCH│
  └─────────────────────┘      └─────────────────────┘    └─────────────────────┘

  Load Version 3:
    1. Walk chain: V3 -> V2 -> V1 (base found)
    2. Load V1 full data
    3. Apply V2 patch to V1 -> intermediate
    4. Apply V3 patch to intermediate -> final data

  CollapseDeltas(slotId, versionNumber=3):
    1. Reconstruct full data for V3 (as above)
    2. Store as new Version 4 (IsDelta: false)
    3. Original V1, V2, V3 remain (cleanup removes them later)


Slot / Version Hierarchy
==========================

  Owner (Account/Character/Session/Realm)
   │
   ├── Game: "arcadia"
   │    ├── Slot: "autosave-1" (Category: AUTO_SAVE, MaxVersions: 5)
   │    │    ├── Version 1: { hash: abc, size: 1024, pinned: false }
   │    │    ├── Version 2: { hash: def, size: 1100, pinned: false }
   │    │    ├── Version 3: { hash: ghi, size: 980, pinned: true, checkpoint: "boss-fight" }
   │    │    ├── Version 4: { hash: jkl, size: 1200, isDelta: true, base: 3 }
   │    │    └── Version 5: { hash: mno, size: 1150, isDelta: true, base: 4 }
   │    │
   │    ├── Slot: "manual-save-1" (Category: MANUAL_SAVE, MaxVersions: 10)
   │    │    └── Version 1: { hash: pqr, size: 5000 }
   │    │
   │    └── Slot: "quicksave" (Category: QUICK_SAVE, MaxVersions: 1)
   │         └── Version 1: { hash: stu, size: 800, compression: NONE }
   │
   └── Game: "fantasia"
        └── Slot: "world-state" (Category: STATE_SNAPSHOT, MaxVersions: 3)
             └── ...

  Key: slot:{gameId}:{ownerType}:{ownerId}:{slotName}


Save / Load Flow (Async Upload)
==================================

  Client                 SaveLoadService              Redis                    Background Worker      Asset Service
    │                         │                         │                           │                      │
    │  POST /save-load/save   │                         │                           │                      │
    │ ───────────────────────>│                         │                           │                      │
    │                         │  Acquire slot lock      │                           │                      │
    │                         │ ───────────────────────>│                           │                      │
    │                         │  <── lock acquired ─────│                           │                      │
    │                         │                         │                           │                      │
    │                         │  Compress + Hash data   │                           │                      │
    │                         │                         │                           │                      │
    │                         │  Save version manifest  │                           │                      │
    │                         │ ───────────────────────>│ (save-load-versions)      │                      │
    │                         │                         │                           │                      │
    │                         │  Store in hot cache     │                           │                      │
    │                         │ ───────────────────────>│ (save-load-cache)         │                      │
    │                         │                         │                           │                      │
    │                         │  Queue for upload       │                           │                      │
    │                         │ ───────────────────────>│ (save-load-pending + set) │                      │
    │                         │                         │                           │                      │
    │  <── 200 OK ────────────│  (immediate response)   │                           │                      │
    │  { uploadPending: true }│                         │                           │                      │
    │                         │                         │                           │                      │
    │                         │                         │  Poll pending set         │                      │
    │                         │                         │ <─────────────────────────│                      │
    │                         │                         │                           │                      │
    │                         │                         │                           │  RequestUpload       │
    │                         │                         │                           │ ────────────────────>│
    │                         │                         │                           │  <── presigned URL ──│
    │                         │                         │                           │                      │
    │                         │                         │                           │  PUT data to URL     │
    │                         │                         │                           │ ────────────────────>│
    │                         │                         │                           │  <── 200 OK ─────────│
    │                         │                         │                           │                      │
    │                         │                         │                           │  CompleteUpload      │
    │                         │                         │                           │ ────────────────────>│
    │                         │                         │                           │  <── assetId ────────│
    │                         │                         │                           │                      │
    │                         │                         │  Update manifest.AssetId  │                      │
    │                         │                         │ <─────────────────────────│                      │
    │                         │                         │  Delete pending entry     │                      │
    │                         │                         │ <─────────────────────────│                      │


Migration Pipeline
====================

  RegisterSchema("arcadia", "1.0", schema, prev: null, patch: null)
  RegisterSchema("arcadia", "1.1", schema, prev: "1.0", patch: [...])
  RegisterSchema("arcadia", "2.0", schema, prev: "1.1", patch: [...])

  Version Graph (adjacency):
    1.0 ──> 1.1 ──> 2.0

  MigrateSave(slot, targetVersion: "2.0"):
    1. Load version manifest -> SchemaVersion: "1.0"
    2. SchemaMigrator.FindMigrationPathAsync("arcadia", "1.0", "2.0")
       └── BFS: ["1.0", "1.1", "2.0"]
    3. For each step:
       a. Load schema definition for target version
       b. Apply MigrationPatchJson (JSON Patch RFC 6902)
    4. Store migrated data as new version (SchemaVersion: "2.0")
    5. Publish SaveMigratedEvent


Export / Import Format (ZIP Archive)
======================================

  export_{ownerId}_{timestamp}.zip
  ├── manifest.json
  │   {
  │     "gameId": "arcadia",
  │     "ownerId": "...",
  │     "ownerType": "ACCOUNT",
  │     "exportedAt": "2025-01-01T...",
  │     "formatVersion": 1,
  │     "slots": [
  │       {
  │         "slotId": "...",
  │         "slotName": "manual-save-1",
  │         "category": "MANUAL_SAVE",
  │         "versionNumber": 5,
  │         "schemaVersion": "1.1",
  │         "contentHash": "abc123...",
  │         "sizeBytes": 50000,
  │         "createdAt": "...",
  │         "metadata": { ... }
  │       },
  │       ...
  │     ]
  │   }
  ├── manual-save-1/
  │   └── data.bin          (uncompressed save data)
  ├── autosave-1/
  │   └── data.bin
  └── ...

  Import Conflict Resolution:
    SKIP       -> existing slot kept, import entry ignored
    OVERWRITE  -> existing slot deleted, import creates fresh
    RENAME     -> import creates with modified slot name


Circuit Breaker State Machine
================================

  ┌────────────────────────────────────────────────────────────┐
  │                                                            │
  │  CLOSED ──(N consecutive failures)──> OPEN                 │
  │    ^                                    │                  │
  │    │                                    │ (wait ResetSeconds)
  │    │                                    v                  │
  │    └──(success in half-open)── HALF_OPEN                   │
  │                                    │                       │
  │                                    │ (failure in half-open) │
  │                                    └──> OPEN               │
  │                                                            │
  └────────────────────────────────────────────────────────────┘

  State stored in Redis (save-load-pending store, key "circuit:storage")
  Multi-instance coordination via distributed state
```

---

## Stubs & Unimplemented Features

1. **BSDIFF delta algorithm**: DeltaProcessor throws `NotSupportedException` for BSDIFF. Defined in config as an option but not implemented. Intended for binary game state where JSON Patch is inappropriate.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/193 -->

2. **XDELTA delta algorithm**: Same as BSDIFF - stubbed with `NotSupportedException`. Listed as a supported algorithm in configuration but has no implementation.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/193 -->

3. **JSON Schema validation**: SchemaMigrator.ValidateAgainstSchema only verifies data is valid JSON. Full JSON Schema (draft-07) validation is not implemented. Comment notes potential use of JsonSchema.Net library.

4. **Auto-collapse during cleanup**: CleanupService logs delta chains that could be collapsed when AutoCollapseEnabled is true, but does not actually perform the collapse. It only identifies candidates.

5. **MaxTotalSizeBytesPerOwner quota**: Partially implemented - a per-SLOT check exists but it only checks the current slot's TotalSizeBytes, not the aggregate across all owner slots. Multi-slot owners can exceed the per-owner limit.

6. **Thumbnail upload and storage**: ThumbnailMaxSizeBytes and ThumbnailAllowedFormats are configured, and SaveVersionManifest has ThumbnailAssetId, but no thumbnail validation or upload logic exists in the Save endpoint.

7. **Conflict detection flagging**: ConflictDetectionEnabled and ConflictDetectionWindowMinutes are configured, and DeviceId is stored on SaveVersionManifest, but no conflict flag is returned to the client during save or load.

8. **SlotResponse display name**: ExportSlotEntry has a DisplayName field but SaveSlotMetadata does not store a display name.

---

## Potential Extensions

1. **Binary delta algorithms**: Implement BSDIFF or XDELTA for binary game state (meshes, textures, compiled world data) where JSON Patch is not applicable.

2. **Rate limiting status code**: Rate limiting is implemented but returns 400 Bad Request instead of 429 Too Many Requests. Change to return proper 429 status code for standard HTTP semantics.

3. **Storage quota enforcement**: Track cumulative storage per owner and reject saves that would exceed MaxTotalSizeBytesPerOwner. Could also emit a warning event at 80% threshold.

4. **Multi-device conflict resolution UI**: Return conflict metadata in save responses when ConflictDetectionWindow triggers. Allow clients to choose which version to keep.

5. **Streaming save/load**: For saves exceeding hot cache size limits, support chunked upload/download without holding entire blob in memory.

6. **Auto-collapse implementation**: During CleanupService runs, actually collapse identified delta chains rather than just logging them.

7. **Retention policy per-slot**: Allow slots to define custom retention policies beyond MaxVersions and RetentionDays (e.g., keep first version of each day, weekly snapshots).

---

## Known Quirks & Caveats

### Bugs

(None currently identified)

### Intentional Quirks

1. **Pinned versions excluded from MaxVersions count**: CleanupService calculates `effectiveMaxVersions = max(1, slot.MaxVersions - pinnedVersions.Count)`. Heavy pinning can reduce the effective unpinned retention to 1.

2. **Hot cache stores base64 data**: HotSaveEntry.Data is base64-encoded, not raw bytes. This increases Redis memory usage by approximately 33% but simplifies JSON serialization of the cache entry.

3. **Promote creates a new version, does not overwrite**: PromoteVersion creates a copy at a new version number rather than moving the old version to the latest position. The original version remains at its original number.

4. **Upload failure does not roll back version manifest**: If SaveUploadWorker permanently fails to upload (after UploadRetryAttempts), the version manifest remains with UploadStatus="PENDING" indefinitely. The save is loadable from hot cache until the cache entry expires.

5. **CleanupControlPlaneOnly uses default app name**: The cleanup service only runs when EffectiveAppId matches "bannou". In distributed deployments with custom app IDs, cleanup will not run automatically.

6. **Asset deletion is best-effort**: When deleting versions or slots, Asset service failures are caught and logged as warnings but do not fail the operation. Orphaned assets may accumulate if the Asset service is repeatedly unavailable.

7. **Rate limiting returns 400 instead of 429**: The rate limiting implementation (SaveLoadService.cs lines 487-505) returns `StatusCodes.BadRequest` when the per-minute limit is exceeded, not the standard HTTP 429 Too Many Requests. Uses Redis counter with key pattern `save-rate:{gameId}:{ownerType}:{ownerId}:{minuteBucket}` and 120-second TTL.

### Design Considerations

1. **Full table scan in AdminStats**: AdminStats queries all slots and all versions (`_ => true`). For large deployments with millions of saves, this will be extremely slow and memory-intensive.

2. **CleanupService queries all slots**: The scheduled cleanup loads all slots into memory each cycle. No pagination or cursor-based iteration is used.

3. **Delta chain reconstruction is synchronous walk**: Loading a delta version at the end of a chain requires sequential loads and patch applications. A chain of length 10 means 10 sequential data loads and 9 patch applications.

4. **Circuit breaker is per-instance on creation but Redis-backed**: StorageCircuitBreaker is instantiated per-call in SaveUploadWorker (not a singleton). State is coordinated via Redis, but each call creates a new instance and reads/writes state, creating potential for race conditions between workers.

5. **Export/import downloads all data into memory**: Export loads all version data and creates a ZIP in memory before uploading. Large exports (many slots, large saves) will consume significant server memory.

6. **No pagination on version listing**: ListVersions returns all versions for a slot without limit/offset. Slots with many versions (especially CHECKPOINT with MaxVersions=20) return all at once.

7. **Schema migration path is forward-only**: SchemaMigrator only builds forward adjacency (previousVersion -> currentVersion). There is no support for backward/downgrade migration. Version graph is a linked list, not a DAG.

8. **Pending upload tracking set grows without bounds**: If uploads fail and entries expire from TTL but are not removed from the tracking set, the set can grow with orphaned IDs. The worker cleans orphans only when processing finds them, not proactively.

9. **PerformRollingCleanupAsync scans from version 1**: Line 54 of VersionCleanupManager.cs iterates `for (var v = 1; v <= slot.LatestVersion ...)`, loading each version by number. For long-lived slots where many early versions were already cleaned up (returning null), this scans N missing keys before finding any existing version. No "earliest surviving version" is tracked in slot metadata.

10. **Circuit breaker is reinstantiated every processing cycle**: Line 102 of SaveUploadWorker.cs creates `new StorageCircuitBreaker(...)` on every `ProcessPendingUploadsAsync` call (every 100ms by default). While state is Redis-coordinated, each instantiation reads Redis state, and the frequent object creation is unnecessary overhead. Could be a singleton or cached instance.

11. **Storage quota check mixes raw and compressed sizes**: The save quota check compares `slot.TotalSizeBytes + body.Data.Length` against `MaxTotalSizeBytesPerOwner`. The `slot.TotalSizeBytes` tracks compressed sizes, but `body.Data.Length` is the raw uncompressed data. The check happens pre-compression so the compressed size is unknown. Options: check after compression (changes error timing), use a compression ratio estimate, or accept the conservative over-estimate.

12. **Storage quota check is per-slot, not per-owner**: The quota check only examines the current slot's TotalSizeBytes against `MaxTotalSizeBytesPerOwner`. An owner with multiple slots can exceed the per-owner limit since each slot is checked individually. True per-owner enforcement requires querying all slots for the owner before each save.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Needs Design Review

- **BSDIFF delta algorithm** - Library selection and design decisions needed. See [#193](https://github.com/beyond-immersion/bannou-service/issues/193). (2026-01-31)
- **XDELTA delta algorithm** - Consolidated with BSDIFF; same library selection and design questions apply. See [#193](https://github.com/beyond-immersion/bannou-service/issues/193). (2026-01-31)
