# Asset Implementation Map

> **Plugin**: lib-asset
> **Schema**: schemas/asset-api.yaml
> **Layer**: AppFeatures (L3)
> **Deep Dive**: [docs/plugins/ASSET.md](../plugins/ASSET.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-asset |
| Layer | L3 AppFeatures |
| Endpoints | 20 |
| State Stores | asset-statestore (Redis), asset-processor-pool (Redis) |
| Events Published | 14 (`asset.upload.requested`, `asset.upload.completed`, `asset.ready`, `asset.processing.queued`, `asset.processing.job.{poolType}`, `asset.processing.retry`, `asset.processing.completed`, `asset.bundle.create`, `asset.bundle.created`, `asset.bundle.updated`, `asset.bundle.deleted`, `asset.bundle.restored`, `asset.metabundle.created`, `asset.metabundle.job.queued`) |
| Events Consumed | 1 (`asset.metabundle.job.queued` — self-consumption) |
| Client Events | 8 |
| Background Services | 3 |

---

## State

**Store**: `asset-statestore` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{AssetKeyPrefix}{assetId}` | `InternalAssetRecord` | Core asset metadata (filename, contentType, size, hash, version, processing status, tags, realm, storage key) |
| `{UploadSessionKeyPrefix}{uploadId}` | `UploadSession` | In-progress upload tracking (target path, content type, multipart state); saved with TTL |
| `{BundleUploadSessionKeyPrefix}{uploadId}` | `BundleUploadSession` | Pre-made bundle upload sessions; saved with TTL |
| `{BundleKeyPrefix}{bundleId}` | `BundleMetadata` | Bundle manifest (asset list, owner, version, compression, lifecycle status, provenance) |
| `{BundleKeyPrefix}bundles-index:{realm}` | `List<string>` | Per-realm bundle ID index; realm falls back to `"_global"` when null |
| `{BundleKeyPrefix}deleted-bundles-index` | Sorted set (`ICacheableStateStore`) | Deferred permanent deletion queue; scored by `DeletedAt` unix timestamp |
| `{AssetIndexKeyPrefix}{realm}:{contentType}` | `List<string>` | Asset IDs indexed by realm and content type |
| `{AssetBundleIndexKeyPrefix}{assetId}` | `AssetBundleIndex` | Reverse index: which bundles contain this asset |
| `{MetabundleJobKeyPrefix}{jobId}` | `MetabundleJob` | Async metabundle job state (status, progress, result) |
| `{BundleVersionKeyPrefix}{bundleId}` | Sorted set (`ICacheableStateStore`) | Version history entries sorted by version number; capped at MaxBundleVersions |
| `{DownloadTokenKeyPrefix}{token}` | `BundleDownloadToken` | Short-lived download authorization tokens; saved with TTL |
| `{BundleCreationJobKeyPrefix}{jobId}` | `BundleCreationJob` | Bundle creation job state for pool delegation |

**Store**: `asset-processor-pool` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{poolType}:{nodeId}` | `ProcessorNodeState` | Per-node state (capacity, load, status, last heartbeat); saved with TTL |
| `{poolType}:index` | `ProcessorPoolIndex` | Node discovery index per pool type |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Persistence for uploads, assets, bundles, jobs, indexes, processor pool state |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 14 event topics |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Self-subscription to `asset.metabundle.job.queued` |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Distributed tracing spans |
| lib-orchestrator (`IOrchestratorClient`) | L3 | Hard | Processor pool scaling: `AcquireProcessorAsync`, `ReleaseProcessorAsync` |

**Internal abstractions** (not cross-service dependencies):
- `IAssetStorageProvider` / `MinioStorageProvider` — MinIO/S3 object storage (presigned URLs, streaming, multipart)
- `IAssetEventEmitter` / `AssetEventEmitter` — WebSocket client event push (wraps `IClientEventPublisher`)
- `IAssetProcessorPoolManager` / `AssetProcessorPoolManager` — Redis-backed processor node state tracking
- `IBundleConverter` / `BundleConverter` — `.bannou` / `.zip` format conversion with LZ4 compression
- `IMinioClient` — MinIO bucket operations and connectivity checks
- `IAmazonS3` — Pre-signed URL generation (workaround for MinIO SDK Content-Type signing bug)
- `IFFmpegService` / `FFmpegService` — Audio/video transcoding via FFmpeg subprocess

**Notes**:
- `IOrchestratorClient` is constructor-injected (hard dependency) despite being a peer L3 service. Asset will fail to start if Orchestrator is not registered.
- No lib-resource integration (no `x-references`). Asset is a leaf node — no external services consume its events.
- MinIO/S3 SDK usage is the documented T4 exception: lib-asset IS the storage infrastructure lib.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `asset.upload.requested` | `AssetUploadRequestedEvent` | `RequestUploadAsync` — on every successful presigned URL generation |
| `asset.upload.completed` | `AssetUploadCompletedEvent` | `CompleteUploadAsync` — after asset record saved and indexed |
| `asset.ready` | `AssetReadyEvent` | `CompleteUploadAsync` (no processing needed) or `AssetProcessingWorker` (after successful processing) |
| `asset.processing.queued` | `AssetProcessingQueuedEvent` | `CompleteUploadAsync` — only for large files exceeding `LargeFileThresholdMb` |
| `asset.processing.job.{poolType}` | `AssetProcessingJobDispatchedEvent` | `DelegateToProcessingPoolAsync` — dynamic topic suffix routes to pool-specific consumers |
| `asset.processing.retry` | `AssetProcessingRetryEvent` | `DelegateToProcessingPoolAsync` — when no processor available |
| `asset.processing.completed` | `AssetProcessingCompletedEvent` | `AssetProcessingWorker` — both success and failure paths |
| `asset.bundle.create` | `BundleCreationJobQueuedEvent` | `CreateBundleAsync` — when delegating to processing pool |
| `asset.bundle.created` | `BundleCreatedEvent` | `CreateBundleAsync` (inline path), metabundle job completion |
| `asset.bundle.updated` | `BundleUpdatedEvent` | `UpdateBundleAsync` — after metadata update |
| `asset.bundle.deleted` | `BundleDeletedEvent` | `DeleteBundleAsync` (soft), `BundleCleanupWorker` (permanent) |
| `asset.bundle.restored` | `BundleRestoredEvent` | `RestoreBundleAsync` |
| `asset.metabundle.created` | `MetabundleCreatedEvent` | `CreateMetabundleAsync` (sync path), `ProcessMetabundleJobAsync` (async job path) |
| `asset.metabundle.job.queued` | `MetabundleJobQueuedEvent` | `CreateMetabundleAsync` (async path only) — self-consumed for load distribution |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `asset.metabundle.job.queued` | `HandleMetabundleJobQueuedAsync` | Loads job from state; checks timeout and cancellation; sets status to Processing; streams source bundles and assembles metabundle via `StreamingBundleWriter`; saves job with result; pushes client event |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<AssetService>` | Structured logging |
| `AssetServiceConfiguration` | Typed configuration access (79 properties) |
| `IStateStoreFactory` | State store access (12+ typed stores from 2 definitions) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Self-subscription for async job processing |
| `ITelemetryProvider` | Distributed tracing spans |
| `IAssetEventEmitter` | WebSocket client event push (8 event methods) |
| `IAssetStorageProvider` | MinIO/S3 storage operations |
| `IOrchestratorClient` | Processor pool scaling |
| `IAssetProcessorPoolManager` | Processor node state tracking |
| `IBundleConverter` | `.bannou` / `.zip` format conversion |
| `AssetProcessingWorker` | Background job consumer with heartbeat and graceful drain |
| `BundleCleanupWorker` | Background purge of soft-deleted bundles |
| `ZipCacheCleanupWorker` | Background purge of expired ZIP cache entries |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| RequestUpload | POST /assets/upload/request | user | upload-session | asset.upload.requested |
| CompleteUpload | POST /assets/upload/complete | user | asset, upload-session, indexes | asset.upload.completed, asset.ready, asset.processing.queued |
| GetAsset | POST /assets/get | user | - | - |
| DeleteAsset | POST /assets/delete | admin | asset, indexes | - |
| ListAssetVersions | POST /assets/list-versions | user | - | - |
| SearchAssets | POST /assets/search | user | - | - |
| BulkGetAssets | POST /assets/bulk-get | user | - | - |
| CreateBundle | POST /bundles/create | user | bundle, indexes | asset.bundle.created, asset.bundle.create |
| GetBundle | POST /bundles/get | user | download-token | - |
| RequestBundleUpload | POST /bundles/upload/request | user | bundle-upload-session | - |
| CreateMetabundle | POST /bundles/metabundle/create | user | bundle, metabundle-job, indexes | asset.metabundle.created, asset.metabundle.job.queued |
| GetJobStatus | POST /bundles/job/status | user | - | - |
| CancelJob | POST /bundles/job/cancel | user | metabundle-job | - |
| ResolveBundles | POST /bundles/resolve | user | - | - |
| QueryBundlesByAsset | POST /bundles/query/by-asset | user | - | - |
| UpdateBundle | POST /bundles/update | user | bundle, version-history | asset.bundle.updated |
| DeleteBundle | POST /bundles/delete | user | bundle, realm-index, deleted-index | asset.bundle.deleted |
| RestoreBundle | POST /bundles/restore | user | bundle, realm-index, deleted-index | asset.bundle.restored |
| QueryBundles | POST /bundles/query | user | - | - |
| ListBundleVersions | POST /bundles/list-versions | user | - | - |

---

## Methods

### RequestUpload
POST /assets/upload/request | Roles: [user]

```
IF filename empty OR size <= 0 OR size > MaxUploadSizeMb OR contentType empty
  RETURN (400, null)
IF contentType is forbidden
  RETURN (400, null)
IF size > MultipartThresholdMb
  // Multipart upload path
  CALL _storageProvider.InitiateMultipartUploadAsync(bucket, tempPath)
ELSE
  // Single upload path
  CALL _storageProvider.GenerateUploadUrlAsync(bucket, tempPath, ttl)
WRITE _uploadSessionStore:{UploadSessionKeyPrefix}{uploadId} <- UploadSession from request  // with TTL
PUBLISH asset.upload.requested { uploadId, owner, filename, size, contentType, isMultipart }
RETURN (200, UploadResponse)
```

### CompleteUpload
POST /assets/upload/complete | Roles: [user]

```
READ _uploadSessionStore:{UploadSessionKeyPrefix}{uploadId}             -> 404 if null
IF session expired
  RETURN (400, null)
IF session.IsMultipart
  IF parts count != session.PartCount
    RETURN (400, null)
  CALL _storageProvider.CompleteMultipartUploadAsync(bucket, key, uploadId, parts)
ELSE
  CALL _storageProvider.ObjectExistsAsync(bucket, tempKey)              -> 404 if false
// Compute SHA-256 hash by streaming the object
CALL _storageProvider.GetObjectAsync(bucket, tempKey)                   // stream to hash
// Derive canonical storage key: assets/{contentType}/{basename}-{sha256}.{ext}
CALL _storageProvider.CopyObjectAsync(bucket, tempKey, finalKey)
CALL _storageProvider.DeleteObjectAsync(bucket, tempKey)
// Check deduplication — same content hash = same asset ID
READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
WRITE _internalAssetRecordStore:{AssetKeyPrefix}{assetId} <- InternalAssetRecord
// Index by realm + content type (optimistic concurrency retry loop)
READ _stringListIndexStore:{AssetIndexKeyPrefix}{realm}:{contentType} [with ETag]
ETAG-WRITE _stringListIndexStore:{AssetIndexKeyPrefix}{realm}:{contentType} <- updated list
  // retries up to IndexOptimisticRetryMaxAttempts on ETag mismatch
IF size > LargeFileThresholdMb AND contentType is processable
  PUBLISH asset.processing.queued { assetId, processingType }
  // see DelegateToProcessingPoolAsync helper
  CALL _processorPoolManager.GetAvailableCountAsync(poolType)
  IF available
    CALL _orchestratorClient.AcquireProcessorAsync(poolType)
    PUBLISH asset.processing.job.{poolType} { assetId, storageKey, processorId, leaseId }
  ELSE
    PUBLISH asset.processing.retry { assetId, retryCount, maxRetries }
ELSE
  PUBLISH asset.ready { assetId, bucket, key, contentHash }
PUBLISH asset.upload.completed { assetId, uploadId, owner, bucket, key, size, contentHash }
DELETE _uploadSessionStore:{UploadSessionKeyPrefix}{uploadId}
RETURN (200, AssetMetadata)
```

### GetAsset
POST /assets/get | Roles: [user]

```
READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}                -> 404 if null
CALL _storageProvider.GenerateDownloadUrlAsync(bucket, storageKey, ttl)
RETURN (200, AssetWithDownloadUrl)
```

### DeleteAsset
POST /assets/delete | Roles: [admin]

```
READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}                -> 404 if null
CALL _storageProvider.DeleteObjectAsync(bucket, storageKey)
DELETE _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
// Remove from realm+contentType index (optimistic concurrency retry loop)
READ _stringListIndexStore:{AssetIndexKeyPrefix}{realm}:{contentType} [with ETag]
ETAG-WRITE _stringListIndexStore:{AssetIndexKeyPrefix}{realm}:{contentType} <- updated list
RETURN (200, DeleteAssetResponse)
// NOTE: Does NOT clean up asset-bundle-index:{assetId} — stale reverse index entries persist
```

### ListAssetVersions
POST /assets/list-versions | Roles: [user]

```
READ _stringListIndexStore:{AssetIndexKeyPrefix}{realm}:{contentType}
IF null OR empty
  RETURN (404, null)
// Paginate results from index
FOREACH assetId in page
  READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
RETURN (200, AssetVersionList)
```

### SearchAssets
POST /assets/search | Roles: [user]

```
IF _assetMetadataSearchStore is available
  // RedisSearch full-text query path
  CALL _assetMetadataSearchStore.SearchAsync(query, filters)
ELSE
  // Fallback: load realm+contentType index, filter in-memory
  READ _stringListIndexStore:{AssetIndexKeyPrefix}{realm}:{contentType}
  FOREACH assetId in index
    READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
  // Filter by tags, content type in memory; paginate
RETURN (200, AssetSearchResult)
```

### BulkGetAssets
POST /assets/bulk-get | Roles: [user]

```
IF assetIds count > MaxBulkGetAssets
  RETURN (400, null)
FOREACH assetId in request.assetIds (parallel)
  READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
  IF found AND request.includeDownloadUrls
    CALL _storageProvider.GenerateDownloadUrlAsync(bucket, storageKey, ttl)
// Collect found assets and missing IDs
RETURN (200, BulkGetAssetsResponse)
```

### CreateBundle
POST /bundles/create | Roles: [user]

```
// Validate all asset IDs exist
FOREACH assetId in request.assetIds
  READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
IF any missing
  RETURN (400, null)
IF ProcessingMode == Pool
  // Delegate to processing pool
  WRITE _bundleCreationJobStore:{jobId} <- BundleCreationJob
  PUBLISH asset.bundle.create { jobId, bundleId, version, assetIds, compression }
  RETURN (200, CreateBundleResponse)  // status: Queued
// Inline assembly path
FOREACH assetId in request.assetIds
  CALL _storageProvider.GetObjectAsync(bucket, storageKey)              // stream assets
CALL _bundleConverter.BuildBundleAsync(assets, compression)
CALL _storageProvider.UploadObjectAsync(bucket, bundlePath, bundleBytes)
WRITE _bundleMetadataStore:{BundleKeyPrefix}{bundleId} <- BundleMetadata
// Index bundle by realm (optimistic concurrency retry)
READ _stringListIndexStore:{BundleKeyPrefix}bundles-index:{realm} [with ETag]
ETAG-WRITE _stringListIndexStore:{BundleKeyPrefix}bundles-index:{realm} <- updated list
// Index each asset's reverse lookup (optimistic concurrency retry per asset)
FOREACH assetId in request.assetIds
  READ _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId} [with ETag]
  ETAG-WRITE _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId} <- updated index
PUBLISH asset.bundle.created { bundleId, version, bucket, key, size, assetCount, compression, owner }
RETURN (200, CreateBundleResponse)
```

### GetBundle
POST /bundles/get | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}                   -> 404 if null
IF bundle.LifecycleStatus == Deleted
  RETURN (404, null)
IF request.format == Zip
  // Check or generate ZIP conversion cache
  CALL _storageProvider.ObjectExistsAsync(bucket, zipCachePath)
  IF not cached
    CALL _bundleConverter.ConvertToZipAsync(bundleStream)
    CALL _storageProvider.UploadObjectAsync(bucket, zipCachePath, zipBytes)
// Generate download token and presigned URL
CALL _storageProvider.GenerateDownloadUrlAsync(bucket, path, ttl)
WRITE _bundleDownloadTokenStore:{DownloadTokenKeyPrefix}{token} <- BundleDownloadToken  // with TTL
RETURN (200, BundleWithDownloadUrl)
```

### RequestBundleUpload
POST /bundles/upload/request | Roles: [user]

```
IF filename empty OR invalid extension (.bannou/.zip required)
  RETURN (400, null)
CALL _storageProvider.GenerateUploadUrlAsync(bucket, uploadPath, ttl)
WRITE _bundleUploadSessionStore:{BundleUploadSessionKeyPrefix}{uploadId} <- BundleUploadSession  // with TTL
RETURN (200, UploadResponse)
```

### CreateMetabundle
POST /bundles/metabundle/create | Roles: [user]

```
// Validate all source bundles exist and are active
FOREACH bundleId in request.sourceBundleIds
  READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
  IF null OR deleted
    RETURN (400, null)
// Count total assets across all source bundles + standalone assets
IF totalAssets > MetabundleAsyncAssetCountThreshold
  OR sourceBundleCount > MetabundleAsyncSourceBundleThreshold
  OR estimatedSize > MetabundleAsyncSizeBytesThreshold
  // Async path: queue job for background processing
  WRITE _metabundleJobStore:{MetabundleJobKeyPrefix}{jobId} <- MetabundleJob { status: Queued }
  PUBLISH asset.metabundle.job.queued { jobId, metabundleId, sourceBundleCount, assetCount }
  RETURN (200, CreateMetabundleResponse)  // with jobId for polling
ELSE
  // Sync path: assemble immediately via StreamingBundleWriter
  FOREACH sourceBundleId in request.sourceBundleIds
    CALL _storageProvider.GetObjectAsync(bucket, bundlePath)            // stream bundle
    // Extract and stream assets into new metabundle
  FOREACH standaloneAssetId in request.standaloneAssetIds
    CALL _storageProvider.GetObjectAsync(bucket, assetPath)
  CALL _storageProvider.UploadObjectAsync(bucket, metabundlePath, stream)
  WRITE _bundleMetadataStore:{BundleKeyPrefix}{metabundleId} <- BundleMetadata
  // Index metabundle by realm and per-asset reverse indexes
  PUBLISH asset.bundle.created { metabundleId, version, bucket, key, size, assetCount }
  PUBLISH asset.metabundle.created { metabundleId, sourceBundleCount, assetCount, realm }
  RETURN (200, CreateMetabundleResponse)  // with downloadUrl
```

### GetJobStatus
POST /bundles/job/status | Roles: [user]

```
READ _metabundleJobStore:{MetabundleJobKeyPrefix}{jobId}                -> 404 if null
RETURN (200, GetJobStatusResponse)
```

### CancelJob
POST /bundles/job/cancel | Roles: [user]

```
READ _metabundleJobStore:{MetabundleJobKeyPrefix}{jobId}                -> 404 if null
IF job.Status == Completed OR job.Status == Failed
  RETURN (400, null)
WRITE _metabundleJobStore:{MetabundleJobKeyPrefix}{jobId} <- job { status: Cancelled }
RETURN (200, CancelJobResponse)
// NOTE: Cancellation is advisory only — in-progress jobs may complete despite cancellation
```

### ResolveBundles
POST /bundles/resolve | Roles: [user]

```
IF assetIds count > MaxResolutionAssets
  RETURN (400, null)
// Build candidate set: for each asset, find which bundles contain it
FOREACH assetId in request.assetIds
  READ _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId}
// Greedy set-cover algorithm: pick bundle covering most uncovered assets, repeat
FOREACH iteration until all covered or no candidates
  // Select bundle with maximum uncovered asset coverage
  // Prefer metabundles when request.preferMetabundles is true
  READ _bundleMetadataStore:{BundleKeyPrefix}{bestBundleId}
  CALL _storageProvider.GenerateDownloadUrlAsync(bucket, path, ttl)
// For remaining uncovered assets, generate standalone download URLs if includeStandalone
FOREACH uncoveredAssetId
  READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
  CALL _storageProvider.GenerateDownloadUrlAsync(bucket, storageKey, ttl)
RETURN (200, ResolveBundlesResponse)
```

### QueryBundlesByAsset
POST /bundles/query/by-asset | Roles: [user]

```
READ _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId}
IF null OR empty
  RETURN (200, QueryBundlesByAssetResponse)  // empty results
FOREACH bundleId in index (paginated)
  READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
  // Filter by bundleType if specified
RETURN (200, QueryBundlesByAssetResponse)
```

### UpdateBundle
POST /bundles/update | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId} [with ETag]       -> 404 if null
IF bundle.LifecycleStatus == Deleted
  RETURN (400, null)
// Apply updates: name, description, tags (replace/add/remove)
// Increment MetadataVersion
// Append version record to history sorted set
WRITE _bundleVersionRecordCacheStore:{BundleVersionKeyPrefix}{bundleId}
  <- AddToSortedSetAsync(version, StoredBundleVersionRecord)
// Trim history to MaxBundleVersions
ETAG-WRITE _bundleMetadataStore:{BundleKeyPrefix}{bundleId} <- updated bundle
PUBLISH asset.bundle.updated { bundleId, version, previousVersion, changes, updatedBy }
RETURN (200, UpdateBundleResponse)
```

### DeleteBundle
POST /bundles/delete | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}                   -> 404 if null
IF bundle.LifecycleStatus == Deleted
  RETURN (400, null)
IF request.permanent
  // Immediate permanent deletion
  CALL _storageProvider.DeleteObjectAsync(bucket, storageKey)
  DELETE _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
  // Clean up per-asset reverse indexes
  FOREACH assetId in bundle.AssetIds
    READ _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId} [with ETag]
    ETAG-WRITE _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId} <- remove bundleId
  PUBLISH asset.bundle.deleted { bundleId, permanent: true }
ELSE
  // Soft delete: mark deleted, add to cleanup queue
  WRITE _bundleMetadataStore:{BundleKeyPrefix}{bundleId} <- bundle { LifecycleStatus: Deleted, DeletedAt: now }
  WRITE _bundleMetadataCacheStore:{BundleKeyPrefix}deleted-bundles-index
    <- AddToSortedSetAsync(deletedAt.ToUnixTimeSeconds(), bundleId)
  // Remove from realm index (optimistic concurrency retry)
  READ _stringListIndexStore:{BundleKeyPrefix}bundles-index:{realm} [with ETag]
  ETAG-WRITE _stringListIndexStore:{BundleKeyPrefix}bundles-index:{realm} <- remove bundleId
  PUBLISH asset.bundle.deleted { bundleId, permanent: false, retentionUntil }
RETURN (200, DeleteBundleResponse)
```

### RestoreBundle
POST /bundles/restore | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}                   -> 404 if null
IF bundle.LifecycleStatus != Deleted
  RETURN (400, null)
// Restore: clear deletion, re-index
WRITE _bundleMetadataStore:{BundleKeyPrefix}{bundleId} <- bundle { LifecycleStatus: Active, DeletedAt: null }
// Remove from deleted-bundles-index sorted set
WRITE _bundleMetadataCacheStore:{BundleKeyPrefix}deleted-bundles-index
  <- RemoveFromSortedSetAsync(bundleId)
// Add back to realm index (optimistic concurrency retry)
READ _stringListIndexStore:{BundleKeyPrefix}bundles-index:{realm} [with ETag]
ETAG-WRITE _stringListIndexStore:{BundleKeyPrefix}bundles-index:{realm} <- add bundleId
PUBLISH asset.bundle.restored { bundleId, restoredFromVersion }
RETURN (200, RestoreBundleResponse)
```

### QueryBundles
POST /bundles/query | Roles: [user]

```
READ _stringListIndexStore:{BundleKeyPrefix}bundles-index:{realm}
FOREACH bundleId in index
  READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
// Filter by: lifecycle status, tags, tagExists/tagNotExists, createdAfter/Before,
//            nameContains, owner, bundleType, includeDeleted
// Sort by sortField (CreatedAt/UpdatedAt/Name/Size) in sortOrder (Asc/Desc)
// Paginate by limit/offset
RETURN (200, QueryBundlesResponse)
```

### ListBundleVersions
POST /bundles/list-versions | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}                   -> 404 if null
// Load version history from sorted set
READ _bundleVersionRecordCacheStore:{BundleVersionKeyPrefix}{bundleId}
  <- GetSortedSetRangeByScoreAsync(offset, limit)
COUNT _bundleVersionRecordCacheStore:{BundleVersionKeyPrefix}{bundleId}
  <- GetSortedSetCountAsync()
RETURN (200, ListBundleVersionsResponse)
```

---

## Background Services

### BundleCleanupWorker
**Interval**: `BundleCleanupIntervalMinutes` (default: 60 min)
**Startup Delay**: `BundleCleanupStartupDelaySeconds` (default: 30s)
**Purpose**: Permanently deletes bundles that have been soft-deleted past the retention period

```
// Query deleted-bundles-index for entries older than DeletedBundleRetentionDays
READ _bundleMetadataCacheStore:{BundleKeyPrefix}deleted-bundles-index
  <- GetSortedSetRangeByScoreAsync(0, cutoffTimestamp)
FOREACH bundleId in expired entries
  READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
  IF found
    CALL _storageProvider.DeleteObjectAsync(bucket, storageKey)
    DELETE _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
    // Clean up per-asset reverse indexes
    FOREACH assetId in bundle.AssetIds
      READ _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId} [with ETag]
      ETAG-WRITE _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId} <- remove bundleId
    // Delete version history
    DELETE _bundleVersionRecordCacheStore:{BundleVersionKeyPrefix}{bundleId}
    PUBLISH asset.bundle.deleted { bundleId, permanent: true }
  // Remove from deleted-bundles-index
  WRITE _bundleMetadataCacheStore:{BundleKeyPrefix}deleted-bundles-index
    <- RemoveFromSortedSetAsync(bundleId)
```

### ZipCacheCleanupWorker
**Interval**: `ZipCacheCleanupIntervalMinutes` (default: 120 min)
**Startup Delay**: `ZipCacheCleanupStartupDelaySeconds` (default: 60s)
**Purpose**: Purges expired ZIP conversion cache entries from storage

```
CALL _storageProvider.ListObjectsAsync(bucket, BundleZipCachePathPrefix)
FOREACH object in results
  IF object.LastModified older than ZipCacheTtlHours
    CALL _storageProvider.DeleteObjectAsync(bucket, object.Key)
// No state store operations — pure storage cleanup
```

### AssetProcessingWorker
**Mode**: Conditional on `ProcessingMode` configuration (Api/Worker/Both)
**Purpose**: Background job consumer for asset processing with heartbeat and graceful shutdown

```
IF ProcessingMode == Api
  // Worker disabled in API-only mode
  RETURN

IF ProcessorNodeId is set
  // Processor node mode: register with pool, maintain heartbeat
  CALL _processorPoolManager.RegisterNodeAsync(poolType, nodeId, capacity)
  LOOP every ProcessorHeartbeatIntervalSeconds
    CALL _processorPoolManager.UpdateHeartbeatAsync(poolType, nodeId, currentLoad)
    IF idle for ProcessorIdleTimeoutSeconds consecutive heartbeats
      CALL IHostApplicationLifetime.StopApplication()  // self-terminate

  // Handle processing jobs dispatched via asset.processing.job.{poolType}
  // On job receipt:
  READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
  CALL processor.ProcessAsync(context)  // TextureProcessor/ModelProcessor/AudioProcessor
  WRITE _internalAssetRecordStore:{AssetKeyPrefix}{assetId} <- updated processingStatus
  CALL _orchestratorClient.ReleaseProcessorAsync(leaseId)
  PUBLISH asset.processing.completed { assetId, processingType, success }
  IF success
    PUBLISH asset.ready { assetId, bucket, key, contentHash }

  // On shutdown:
  CALL _processorPoolManager.SetDrainingAsync(poolType, nodeId)
  // Wait up to ShutdownDrainTimeoutMinutes for active jobs
  CALL _processorPoolManager.RemoveNodeAsync(poolType, nodeId)
```
