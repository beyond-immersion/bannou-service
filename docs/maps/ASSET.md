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
| Endpoints | 19 |
| State Stores | asset-statestore (Redis), asset-processor-pool (Redis) |
| Events Published | 13 (`asset.upload.requested`, `asset.upload.completed`, `asset.ready`, `asset.processing.queued`, `asset.processing.job.{poolType}`, `asset.processing.retry`, `asset.processing.completed`, `asset.bundle.create`, `asset.bundle.created`, `asset.bundle.updated`, `asset.bundle.deleted`, `asset.metabundle.created`, `asset.metabundle.job.queued`) |
| Events Consumed | 1 (`asset.metabundle.job.queued` — self-consumption) |
| Client Events | 8 |
| Background Services | 2 |

---

## State

**Store**: `asset-statestore` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{AssetKeyPrefix}{assetId}` | `InternalAssetRecord` | Core asset metadata (filename, contentType, size, hash, version, processing status, tags, realm, storage key) |
| `{UploadSessionKeyPrefix}{uploadId}` | `UploadSession` | In-progress upload tracking (target path, content type, multipart state); saved with TTL |
| `{BundleUploadSessionKeyPrefix}{uploadId}` | `BundleUploadSession` | Pre-made bundle upload sessions; saved with TTL |
| `{BundleKeyPrefix}{bundleId}` | `BundleMetadata` | Bundle manifest (asset list, createdBy, version, compression, lifecycle status, provenance) |
| `{BundleKeyPrefix}bundles-index:{realm}` | `List<string>` | Per-realm bundle ID index; realm falls back to `"_global"` when null |
| `{AssetIndexKeyPrefix}type:{assetType}` | `List<string>` | Asset IDs indexed by asset type |
| `{AssetIndexKeyPrefix}realm:{realm}` | `List<string>` | Asset IDs indexed by realm |
| `{AssetIndexKeyPrefix}tag:{tag}` | `List<string>` | Asset IDs indexed by tag |
| `{AssetBundleIndexKeyPrefix}{assetId}` | `AssetBundleIndex` | Reverse index: which bundles contain this asset |
| `{MetabundleJobKeyPrefix}{jobId}` | `MetabundleJob` | Async metabundle job state (status, progress, result) |
| `bundle-version:{bundleId}:{versionNumber}` | `StoredBundleVersionRecord` | Individual version history entry |
| `bundle-version-index:{bundleId}` | `Set<int>` (`ICacheableStateStore`) | Version number set for a bundle |
| `bundle-owner-index:{createdBy}` | `Set<string>` (`ICacheableStateStore`) | Bundle IDs owned by a creator (used by QueryBundles) |
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
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 13 event topics |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Self-subscription to `asset.metabundle.job.queued` |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Distributed tracing spans |
| lib-orchestrator (`IOrchestratorClient`) | L3 | Soft | Processor pool scaling: `ScalePoolAsync`, `AcquireProcessorAsync` — resolved via `IServiceProvider.GetService<T>()` with graceful degradation |

**Internal abstractions** (not cross-service dependencies):
- `IAssetStorageProvider` / `MinioStorageProvider` — MinIO/S3 object storage (presigned URLs, streaming, multipart)
- `IAssetEventEmitter` / `AssetEventEmitter` — WebSocket client event push (wraps `IClientEventPublisher`)
- `IAssetProcessorPoolManager` / `AssetProcessorPoolManager` — Redis-backed processor node state tracking
- `IBundleConverter` / `BundleConverter` — `.bannou` / `.zip` format conversion with LZ4 compression
- `IMinioClient` — MinIO bucket operations and connectivity checks
- `IAmazonS3` — Pre-signed URL generation (workaround for MinIO SDK Content-Type signing bug)
- `IFFmpegService` / `FFmpegService` — Audio/video transcoding via FFmpeg subprocess

**Notes**:
- `IOrchestratorClient` is resolved via `IServiceProvider.GetService<T>()` (soft dependency) per SERVICE HIERARCHY L3 rules. Asset degrades gracefully when Orchestrator is absent by skipping pool-based processing.
- No lib-resource integration (no `x-references`). Asset is a leaf node — no external services consume its events.
- MinIO/S3 SDK usage is the documented exception: lib-asset IS the storage infrastructure lib.

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
| `asset.bundle.deleted` | `BundleDeletedEvent` | `DeleteBundleAsync` (permanent) |
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
| `AssetServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (13 typed stores from 2 definitions); not stored as field |
| `IMessageBus` | Event publishing (13 event topics) |
| `IEventConsumer` | Self-subscription for `asset.metabundle.job.queued`; not stored as field |
| `ITelemetryProvider` | Distributed tracing spans |
| `IAssetEventEmitter` | WebSocket client event push (8 client event types) |
| `IAssetStorageProvider` | MinIO/S3 storage operations (presigned URLs, streaming, multipart) |
| `IServiceProvider` | Soft resolution of `IOrchestratorClient` |
| `IAssetProcessorPoolManager` | Processor node state tracking |
| `IBundleConverter` | `.bannou` / `.zip` format conversion |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| RequestUpload | POST /assets/upload/request | generated | user | upload-session | asset.upload.requested |
| CompleteUpload | POST /assets/upload/complete | generated | user | asset, upload-session, indexes | asset.upload.completed, asset.ready, asset.processing.queued |
| GetAsset | POST /assets/get | generated | user | - | - |
| DeleteAsset | POST /assets/delete | generated | admin | asset, indexes | - |
| ListAssetVersions | POST /assets/list-versions | generated | user | - | - |
| SearchAssets | POST /assets/search | generated | user | - | - |
| BulkGetAssets | POST /assets/bulk-get | generated | user | - | - |
| CreateBundle | POST /bundles/create | generated | user | bundle, indexes | asset.bundle.created, asset.bundle.create |
| GetBundle | POST /bundles/get | generated | user | download-token | - |
| RequestBundleUpload | POST /bundles/upload/request | generated | user | bundle-upload-session | - |
| CreateMetabundle | POST /bundles/metabundle/create | generated | user | bundle, metabundle-job, indexes | asset.metabundle.created, asset.metabundle.job.queued |
| GetJobStatus | POST /bundles/job/status | generated | user | - | - |
| CancelJob | POST /bundles/job/cancel | generated | user | metabundle-job | - |
| ResolveBundles | POST /bundles/resolve | generated | user | - | - |
| QueryBundlesByAsset | POST /bundles/query/by-asset | generated | user | - | - |
| UpdateBundle | POST /bundles/update | generated | user | bundle, version-history | asset.bundle.updated |
| DeleteBundle | POST /bundles/delete | generated | user | bundle, indexes, version-history | asset.bundle.deleted |
| QueryBundles | POST /bundles/query | generated | user | - | - |
| ListBundleVersions | POST /bundles/list-versions | generated | user | - | - |

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
WRITE _uploadSessionStore:{UploadSessionKeyPrefix}{uploadId} <- UploadSession from request // with TTL
PUBLISH asset.upload.requested { uploadId, createdBy, filename, size, contentType, isMultipart }
RETURN (200, UploadResponse)
```

### CompleteUpload
POST /assets/upload/complete | Roles: [user]

```
READ _uploadSessionStore:{UploadSessionKeyPrefix}{uploadId} -> 404 if null
IF session expired
 RETURN (400, null)
IF session.IsMultipart
 IF parts count != session.PartCount
 RETURN (400, null)
 CALL _storageProvider.CompleteMultipartUploadAsync(bucket, key, uploadId, parts)
ELSE
 CALL _storageProvider.ObjectExistsAsync(bucket, tempKey) -> 404 if false
// Compute SHA-256 hash by streaming the object
CALL _storageProvider.GetObjectAsync(bucket, tempKey) // stream to hash
// Derive canonical storage key: assets/{contentType}/{basename}-{sha256}.{ext}
CALL _storageProvider.CopyObjectAsync(bucket, tempKey, finalKey)
CALL _storageProvider.DeleteObjectAsync(bucket, tempKey)
// Check deduplication — same content hash = same asset ID
READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
WRITE _internalAssetRecordStore:{AssetKeyPrefix}{assetId} <- InternalAssetRecord
// Index by type, realm, and tags (optimistic concurrency retry loop per dimension)
// IndexAssetAsync writes to _stringListIndexStore:
//   asset-index:type:{assetType}, asset-index:realm:{realm}, asset-index:tag:{tag}
// Each index write retries up to IndexOptimisticRetryMaxAttempts on ETag mismatch
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
PUBLISH asset.upload.completed { assetId, uploadId, createdBy, bucket, key, size, contentHash }
DELETE _uploadSessionStore:{UploadSessionKeyPrefix}{uploadId}
RETURN (200, AssetMetadata)
```

### GetAsset
POST /assets/get | Roles: [user]

```
READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId} -> 404 if null
CALL _storageProvider.GenerateDownloadUrlAsync(bucket, storageKey, ttl)
RETURN (200, AssetWithDownloadUrl)
```

### DeleteAsset
POST /assets/delete | Roles: [admin]

```
READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId} -> 404 if null
CALL _storageProvider.DeleteObjectAsync(bucket, storageKey)
DELETE _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
// Remove from type/realm/tag indexes (optimistic concurrency retry per index)
RETURN (200, DeleteAssetResponse)
// NOTE: Does NOT publish any event. Does NOT clean up asset-bundle-index — stale entries persist
```

### ListAssetVersions
POST /assets/list-versions | Roles: [user]

```
READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId} -> 404 if null
// List object versions from storage provider
CALL _storageProvider.ListVersionsAsync(bucket, storageKey)
// Paginate in-memory
RETURN (200, AssetVersionList)
```

### SearchAssets
POST /assets/search | Roles: [user]

```
IF _assetMetadataSearchStore is available
 // RedisSearch full-text query path
 CALL _assetMetadataSearchStore.SearchAsync("assetMetadataIndex", query, filters)
 // Over-fetches then filters tags in-memory
ELSE
 // Fallback: load type index, filter in-memory
 READ _stringListIndexStore:{AssetIndexKeyPrefix}type:{assetType}
 FOREACH assetId in index
 READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
 // Filter by realm, tags, content type in memory; paginate
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
 RETURN (200, CreateBundleResponse) // status: Queued
// Inline assembly path
FOREACH assetId in request.assetIds
 CALL _storageProvider.GetObjectAsync(bucket, storageKey) // stream assets
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
PUBLISH asset.bundle.created { bundleId, version, bucket, key, size, assetCount, compression, createdBy }
RETURN (200, CreateBundleResponse)
```

### GetBundle
POST /bundles/get | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId} -> 404 if null
IF request.format == Zip
 // Check or generate ZIP conversion cache
 CALL _storageProvider.ObjectExistsAsync(bucket, zipCachePath)
 IF not cached
 CALL _bundleConverter.ConvertToZipAsync(bundleStream)
 CALL _storageProvider.UploadObjectAsync(bucket, zipCachePath, zipBytes)
// Generate download token and presigned URL
CALL _storageProvider.GenerateDownloadUrlAsync(bucket, path, ttl)
WRITE _bundleDownloadTokenStore:{DownloadTokenKeyPrefix}{token} <- BundleDownloadToken // with TTL
RETURN (200, BundleWithDownloadUrl)
```

### RequestBundleUpload
POST /bundles/upload/request | Roles: [user]

```
IF filename empty OR invalid extension (.bannou/.zip required)
 RETURN (400, null)
CALL _storageProvider.GenerateUploadUrlAsync(bucket, uploadPath, ttl)
WRITE _bundleUploadSessionStore:{BundleUploadSessionKeyPrefix}{uploadId} <- BundleUploadSession // with TTL
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
 RETURN (200, CreateMetabundleResponse) // with jobId for polling
ELSE
 // Sync path: assemble immediately via StreamingBundleWriter
 FOREACH sourceBundleId in request.sourceBundleIds
 CALL _storageProvider.GetObjectAsync(bucket, bundlePath) // stream bundle
 // Extract and stream assets into new metabundle
 FOREACH standaloneAssetId in request.standaloneAssetIds
 CALL _storageProvider.GetObjectAsync(bucket, assetPath)
 CALL _storageProvider.UploadObjectAsync(bucket, metabundlePath, stream)
 WRITE _bundleMetadataStore:{BundleKeyPrefix}{metabundleId} <- BundleMetadata
 // Index metabundle by realm and per-asset reverse indexes
 PUBLISH asset.bundle.created { metabundleId, version, bucket, key, size, assetCount }
 PUBLISH asset.metabundle.created { metabundleId, sourceBundleCount, assetCount, realm }
 RETURN (200, CreateMetabundleResponse) // with downloadUrl
```

### GetJobStatus
POST /bundles/job/status | Roles: [user]

```
READ _metabundleJobStore:{MetabundleJobKeyPrefix}{jobId} -> 404 if null
RETURN (200, GetJobStatusResponse)
```

### CancelJob
POST /bundles/job/cancel | Roles: [user]

```
READ _metabundleJobStore:{MetabundleJobKeyPrefix}{jobId} -> 404 if null
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
 RETURN (200, QueryBundlesByAssetResponse) // empty results
FOREACH bundleId in index (paginated)
 READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
 // Filter by bundleType if specified
RETURN (200, QueryBundlesByAssetResponse)
```

### UpdateBundle
POST /bundles/update | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId} [with ETag] -> 404 if null
IF bundle.LifecycleStatus == Deleted
 RETURN (400, null)
// Apply updates: name, description, tags (replace/add/remove)
// Increment MetadataVersion
// Save individual version record and add version number to set index
WRITE _bundleVersionRecordCacheStore:bundle-version:{bundleId}:{versionNumber} <- StoredBundleVersionRecord
WRITE _bundleVersionRecordCacheStore:bundle-version-index:{bundleId} <- AddToSetAsync(versionNumber)
ETAG-WRITE _bundleMetadataStore:{BundleKeyPrefix}{bundleId} <- updated bundle
PUBLISH asset.bundle.updated { bundleId, version, previousVersion, changes, updatedBy }
RETURN (200, UpdateBundleResponse)
```

### DeleteBundle
POST /bundles/delete | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId} -> 404 if null
// Immediate hard delete per FOUNDATION TENETS (no soft-delete)
CALL _storageProvider.DeleteObjectAsync(bucket, storageKey) // swallows failure, logs warning
DELETE _bundleMetadataStore:{BundleKeyPrefix}{bundleId}
// Clean up per-asset reverse indexes
FOREACH assetId in bundle.AssetIds
 READ _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId}
 WRITE _assetBundleIndexStore:{AssetBundleIndexKeyPrefix}{assetId} <- remove bundleId
// Delete version history entries
READ _bundleVersionRecordCacheStore:bundle-version-index:{bundleId} <- GetSetAsync
FOREACH version in versionNumbers
 DELETE _bundleVersionRecordCacheStore:bundle-version:{bundleId}:{version}
DELETE _bundleVersionRecordCacheStore:bundle-version-index:{bundleId}
PUBLISH asset.bundle.deleted { bundleId, reason, deletedBy, realm }
RETURN (200, null) // bare StatusCodes return
```

### QueryBundles
POST /bundles/query | Roles: [user]

```
// Requires createdBy filter — returns empty if absent
IF createdBy is empty
 RETURN (200, QueryBundlesResponse) // empty
READ _bundleMetadataCacheStore:bundle-owner-index:{createdBy} <- GetSetAsync
READ _bundleMetadataCacheStore <- GetBulkAsync(bundle keys for all IDs in set)
// Filter by: lifecycle status, tags, tagExists/tagNotExists, createdAfter/Before,
// nameContains, bundleType
// Sort by sortField (CreatedAt/UpdatedAt/Name/Size) in sortOrder (Asc/Desc)
// Paginate by limit/offset (capped at MaxQueryLimit)
RETURN (200, QueryBundlesResponse)
```

### ListBundleVersions
POST /bundles/list-versions | Roles: [user]

```
READ _bundleMetadataStore:{BundleKeyPrefix}{bundleId} -> 404 if null
// Load version numbers from set index, then bulk-load version records
READ _bundleVersionRecordCacheStore:bundle-version-index:{bundleId} <- GetSetAsync
// Paginate version numbers, then bulk-load the page
READ _bundleVersionRecordCacheStore <- GetBulkAsync(bundle-version:{bundleId}:{version} for each in page)
RETURN (200, ListBundleVersionsResponse)
```

---

## Background Services

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
 CALL IHostApplicationLifetime.StopApplication() // self-terminate

 // Handle processing jobs dispatched via asset.processing.job.{poolType}
 // On job receipt:
 READ _internalAssetRecordStore:{AssetKeyPrefix}{assetId}
 CALL processor.ProcessAsync(context) // TextureProcessor/ModelProcessor/AudioProcessor
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

---

## Non-Standard Implementation Patterns

### Plugin Lifecycle (OnStartAsync)

MinIO connectivity check with exponential backoff — the plugin will fail to start if MinIO is unreachable:

```
LOOP up to MinioStartupMaxRetries (default: 30)
 CALL _minioClient.BucketExistsAsync(StorageBucket)
 IF success
   IF bucket does not exist
     CALL _minioClient.MakeBucketAsync(StorageBucket)
   RETURN true
 ELSE
   // Delay scales up: retryDelayMs * min(attempt, 5), max 10s default
   await Task.Delay(scaledDelay)
IF all retries exhausted
 RETURN false // service will not start
```

### MinioWebhookHandler (Orphaned)

`Webhooks/MinioWebhookHandler.cs` contains a webhook handler for MinIO S3 event notifications but has no visible route registration in the plugin. The class exists with DI constructor parameters but is not registered as a service and has no `MapPost`/`MapGet` binding. This may be dead code or may be registered externally.
