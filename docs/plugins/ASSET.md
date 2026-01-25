# Asset Plugin Deep Dive

> **Plugin**: lib-asset
> **Schema**: schemas/asset-api.yaml
> **Version**: 1.0.0
> **State Stores**: asset-statestore (Redis), asset-processor-pool (Redis)

---

## Overview

The Asset service provides storage, versioning, and distribution of large binary assets (textures, audio, 3D models) using MinIO/S3-compatible object storage. It never routes raw asset data through the WebSocket gateway; instead, it issues pre-signed URLs so clients upload/download directly to the storage backend. The service also manages bundles (grouped assets in a custom `.bannou` format with LZ4 compression), metabundles (merged super-bundles), and a distributed processor pool for content-type-specific transcoding and optimization.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for upload sessions, asset records, bundle metadata, job state, processor pool state |
| lib-messaging (`IMessageBus`) | Publishing asset lifecycle events and processing job dispatch |
| lib-messaging (`IEventConsumer`) | Self-consumption of `asset.metabundle.job.queued` for async processing |
| lib-mesh (`IOrchestratorClient`) | Processor pool scaling and node acquisition/release |
| `IAssetEventEmitter` (internal) | WebSocket session-targeted client notifications via `IClientEventPublisher` |
| MinIO SDK (`IMinioClient`) | Bucket operations, object listing, webhook handling |
| AWS SDK (`IAmazonS3`) | Pre-signed URL generation (workaround for MinIO SDK Content-Type signing bug) |
| `IBundleConverter` | `.bannou` ↔ `.zip` format conversion with LZ4 compression |
| `IAssetProcessorPoolManager` | Redis-based distributed processor node tracking |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-behavior | Calls `GetAssetAsync` via `IAssetClient` to load compiled ABML behavior bytecode |
| lib-behavior (`BehaviorBundleManager`) | Manages bundles of behavior assets via `IAssetClient` |
| lib-actor (`BehaviorDocumentCache`) | Fetches and caches behavior documents from asset storage |
| lib-documentation | Stores/retrieves documentation archive assets via `IAssetClient` |
| lib-mapping | Stores/retrieves map data and spatial assets via `IAssetClient` |
| lib-save-load | Stores versioned game save data, handles export/import/migration/cleanup via `IAssetClient` |
| sdk: asset-loader-server (`BannouMeshAssetSource`) | Server-side asset loading via mesh invocation |
| sdk: asset-loader-client (`BannouWebSocketAssetSource`) | Client-side asset loading via WebSocket protocol |

No external services subscribe to asset events; all event consumption is internal.

---

## State Storage

### Store: `asset-statestore` (Backend: Redis, prefix: `asset`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `asset:{assetId}` | `InternalAssetRecord` | Core asset metadata (filename, contentType, size, hash, version, processing status, tags, realm) |
| `upload:{uploadId}` | `UploadSession` | Tracks in-progress uploads (target path, content type, multipart state, expiry) |
| `bundle:{bundleId}` | `BundleMetadata` | Bundle manifest (asset list, owner, version, compression, lifecycle state, provenance) |
| `bundle-version:{bundleId}:{version}` | `StoredBundleVersionRecord` | Historical bundle version snapshot |
| `bundle-version-index:{bundleId}` | `List<int>` | Set of version numbers for a bundle |
| `bundle-owner-index:{owner}` | `List<string>` | All bundle IDs owned by an entity |
| `bundle-upload:{uploadId}` | `BundleUploadSession` | Tracks pre-made bundle upload sessions |
| `bundle-download:{token}` | `BundleDownloadToken` | Short-lived download authorization tokens |
| `bundle-job:{jobId}` | `BundleCreationJob` | Inline bundle creation job state |
| `metabundle-job:{jobId}` | `MetabundleJob` | Async metabundle job state (progress, status, error) |
| `asset-index:type:{contentType}` | `List<string>` | Asset IDs indexed by content type |
| `asset-index:realm:{realmId}` | `List<string>` | Asset IDs indexed by realm |
| `asset-index:tag:{tag}` | `List<string>` | Asset IDs indexed by tag |
| `{realm}:asset-bundles:{assetId}` | `AssetBundleIndex` | Reverse index: which bundles contain this asset |

Key prefixes are configurable via `AssetServiceConfiguration` (see Configuration section).

### Store: `asset-processor-pool` (Backend: Redis, prefix: `asset:pool`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{poolType}:{nodeId}` | `ProcessorNodeState` | Per-node state (capacity, load, status, last heartbeat) |
| `{poolType}:index` | `List<string>` | Node discovery index per pool type |

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `asset.upload.requested` | Upload URL generated for client |
| `asset.upload.completed` | Webhook confirms object created in storage (also published by MinioWebhookHandler) |
| `asset.bundle.create` | Bundle creation job starts |
| `asset.bundle.created` | Bundle successfully assembled and stored |
| `asset.bundle.updated` | Bundle metadata modified |
| `asset.bundle.deleted` | Bundle soft-deleted |
| `asset.bundle.restored` | Soft-deleted bundle restored |
| `asset.metabundle.created` | Metabundle assembly complete |
| `asset.metabundle.job.queued` | Async metabundle job queued (self-consumed) |
| `asset.processing.retry` | Processing retry scheduled after failure |
| `asset.processing.job.{poolType}` | Processing job dispatched to specific pool |
| `asset.processing.completed` | Processing worker finishes a job (success or failure) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `asset.metabundle.job.queued` | `HandleMetabundleJobQueuedAsync` | Loads job from state, streams source bundles, assembles metabundle, updates progress 0→100%, emits completion event |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `StorageProvider` | `ASSET_STORAGE_PROVIDER` | `minio` | Storage backend type (minio/s3/r2/azure/filesystem) |
| `StorageBucket` | `ASSET_STORAGE_BUCKET` | `bannou-assets` | Primary bucket name |
| `StorageEndpoint` | `ASSET_STORAGE_ENDPOINT` | `minio:9000` | Internal storage endpoint (host:port) |
| `StoragePublicEndpoint` | `ASSET_STORAGE_PUBLIC_ENDPOINT` | *nullable* | Public endpoint for pre-signed URLs (Docker/K8s split) |
| `StorageAccessKey` | `ASSET_STORAGE_ACCESS_KEY` | `minioadmin` | Storage access key |
| `StorageSecretKey` | `ASSET_STORAGE_SECRET_KEY` | `minioadmin` | Storage secret key |
| `StorageRegion` | `ASSET_STORAGE_REGION` | `us-east-1` | Storage region |
| `StorageForcePathStyle` | `ASSET_STORAGE_FORCE_PATH_STYLE` | `true` | Path-style URLs (required for MinIO) |
| `StorageUseSsl` | `ASSET_STORAGE_USE_SSL` | `false` | TLS for storage connections |
| `TokenTtlSeconds` | `ASSET_TOKEN_TTL_SECONDS` | `3600` | Pre-signed upload URL TTL |
| `DownloadTokenTtlSeconds` | `ASSET_DOWNLOAD_TOKEN_TTL_SECONDS` | `900` | Pre-signed download URL TTL |
| `MaxUploadSizeMb` | `ASSET_MAX_UPLOAD_SIZE_MB` | `500` | Max upload size |
| `MultipartThresholdMb` | `ASSET_MULTIPART_THRESHOLD_MB` | `50` | Threshold for multipart upload activation |
| `MultipartPartSizeMb` | `ASSET_MULTIPART_PART_SIZE_MB` | `16` | Part size for multipart uploads |
| `MaxResolutionAssets` | `ASSET_MAX_RESOLUTION_ASSETS` | `500` | Max asset IDs per resolution request |
| `MaxBulkGetAssets` | `ASSET_MAX_BULK_GET_ASSETS` | `100` | Max asset IDs per bulk get request |
| `LargeFileThresholdMb` | `ASSET_LARGE_FILE_THRESHOLD_MB` | `50` | Size threshold for processing pool delegation |
| `ProcessingPoolType` | `ASSET_PROCESSING_POOL_TYPE` | `asset-processor` | Orchestrator pool identifier |
| `ProcessingMode` | `ASSET_PROCESSING_MODE` | `both` | Service mode: api/worker/both |
| `WorkerPool` | `ASSET_WORKER_POOL` | *nullable* | Worker pool ID (worker mode only) |
| `ProcessorNodeId` | `ASSET_PROCESSOR_NODE_ID` | *nullable* | Unique node ID (set by orchestrator) |
| `ProcessorIdleTimeoutSeconds` | `ASSET_PROCESSOR_IDLE_TIMEOUT_SECONDS` | `300` | Idle auto-termination threshold |
| `ProcessorHeartbeatIntervalSeconds` | `ASSET_PROCESSOR_HEARTBEAT_INTERVAL_SECONDS` | `30` | Heartbeat emission interval |
| `ProcessorMaxConcurrentJobs` | `ASSET_PROCESSOR_MAX_CONCURRENT_JOBS` | `10` | Max concurrent jobs per node |
| `ProcessorHeartbeatTimeoutSeconds` | `ASSET_PROCESSOR_HEARTBEAT_TIMEOUT_SECONDS` | `90` | Unhealthy threshold for missing heartbeats |
| `FfmpegPath` | `ASSET_FFMPEG_PATH` | *nullable* | FFmpeg binary path (system PATH if unset) |
| `FfmpegWorkingDirectory` | `ASSET_FFMPEG_WORKING_DIR` | `/tmp/bannou-ffmpeg` | FFmpeg temp file directory |
| `AudioOutputFormat` | `ASSET_AUDIO_OUTPUT_FORMAT` | `mp3` | Default audio transcode format (mp3/opus/aac) |
| `AudioBitrateKbps` | `ASSET_AUDIO_BITRATE_KBPS` | `192` | Default audio bitrate |
| `AudioPreserveLossless` | `ASSET_AUDIO_PRESERVE_LOSSLESS` | `true` | Keep original alongside transcoded |
| `BundleCompressionDefault` | `ASSET_BUNDLE_COMPRESSION_DEFAULT` | `lz4` | Default bundle compression (lz4/lzma/none) |
| `ZipCacheTtlHours` | `ASSET_ZIP_CACHE_TTL_HOURS` | `24` | TTL for cached ZIP conversions |
| `DeletedBundleRetentionDays` | `ASSET_DELETED_BUNDLE_RETENTION_DAYS` | `30` | Soft-delete retention period |
| `MinioWebhookSecret` | `ASSET_MINIO_WEBHOOK_SECRET` | *nullable* | Webhook validation secret (disabled if unset) |
| `TempUploadPathPrefix` | `ASSET_TEMP_UPLOAD_PATH_PREFIX` | `temp` | Bucket path for upload staging |
| `FinalAssetPathPrefix` | `ASSET_FINAL_ASSET_PATH_PREFIX` | `assets` | Bucket path for finalized assets |
| `BundleCurrentPathPrefix` | `ASSET_BUNDLE_CURRENT_PATH_PREFIX` | `bundles/current` | Bucket path for bundles |
| `BundleZipCachePathPrefix` | `ASSET_BUNDLE_ZIP_CACHE_PATH_PREFIX` | `bundles/zip-cache` | Bucket path for ZIP cache |
| `BundleUploadPathPrefix` | `ASSET_BUNDLE_UPLOAD_PATH_PREFIX` | `bundles/uploads` | Bucket path for bundle upload staging |
| `ProcessorAvailabilityMaxWaitSeconds` | `ASSET_PROCESSOR_AVAILABILITY_MAX_WAIT_SECONDS` | `60` | Max wait for processor availability |
| `ProcessorAvailabilityPollIntervalSeconds` | `ASSET_PROCESSOR_AVAILABILITY_POLL_INTERVAL_SECONDS` | `2` | Poll interval while waiting for processor |
| `ProcessingMaxRetries` | `ASSET_PROCESSING_MAX_RETRIES` | `5` | Max processing retry attempts |
| `ProcessingRetryDelaySeconds` | `ASSET_PROCESSING_RETRY_DELAY_SECONDS` | `30` | Delay between retries |
| `UploadSessionKeyPrefix` | `ASSET_UPLOAD_SESSION_KEY_PREFIX` | `upload:` | State store key prefix for uploads |
| `AssetKeyPrefix` | `ASSET_KEY_PREFIX` | `asset:` | State store key prefix for assets |
| `AssetIndexKeyPrefix` | `ASSET_INDEX_KEY_PREFIX` | `asset-index:` | State store key prefix for indexes |
| `BundleKeyPrefix` | `ASSET_BUNDLE_KEY_PREFIX` | `bundle:` | State store key prefix for bundles |
| `TextureProcessorPoolType` | `ASSET_TEXTURE_PROCESSOR_POOL_TYPE` | `texture-processor` | Pool type for textures |
| `ModelProcessorPoolType` | `ASSET_MODEL_PROCESSOR_POOL_TYPE` | `model-processor` | Pool type for 3D models |
| `AudioProcessorPoolType` | `ASSET_AUDIO_PROCESSOR_POOL_TYPE` | `audio-processor` | Pool type for audio |
| `DefaultProcessorPoolType` | `ASSET_DEFAULT_PROCESSOR_POOL_TYPE` | `asset-processor` | Fallback pool type |
| `AdditionalProcessableContentTypes` | `ASSET_ADDITIONAL_PROCESSABLE_CONTENT_TYPES` | *nullable* | Comma-separated extra processable MIME types |
| `AdditionalExtensionMappings` | `ASSET_ADDITIONAL_EXTENSION_MAPPINGS` | *nullable* | Comma-separated `.ext=type` pairs |
| `AdditionalForbiddenContentTypes` | `ASSET_ADDITIONAL_FORBIDDEN_CONTENT_TYPES` | *nullable* | Comma-separated extra forbidden MIME types |
| `IndexOptimisticRetryMaxAttempts` | `ASSET_INDEX_OPTIMISTIC_RETRY_MAX_ATTEMPTS` | `5` | Max optimistic concurrency retries for indexes |
| `IndexOptimisticRetryBaseDelayMs` | `ASSET_INDEX_OPTIMISTIC_RETRY_BASE_DELAY_MS` | `10` | Base delay between retries (multiplied by attempt) |
| `MetabundleAsyncSourceBundleThreshold` | `ASSET_METABUNDLE_ASYNC_SOURCE_BUNDLE_THRESHOLD` | `3` | Source bundle count triggering async mode |
| `MetabundleAsyncAssetCountThreshold` | `ASSET_METABUNDLE_ASYNC_ASSET_COUNT_THRESHOLD` | `50` | Asset count triggering async mode |
| `MetabundleAsyncSizeBytesThreshold` | `ASSET_METABUNDLE_ASYNC_SIZE_BYTES_THRESHOLD` | `104857600` | Total size (bytes) triggering async mode (~100MB) |
| `MetabundleJobTtlSeconds` | `ASSET_METABUNDLE_JOB_TTL_SECONDS` | `86400` | Job status retention after completion |
| `MetabundleJobKeyPrefix` | `ASSET_METABUNDLE_JOB_KEY_PREFIX` | `metabundle-job:` | State store key prefix for metabundle jobs |
| `MetabundleJobTimeoutSeconds` | `ASSET_METABUNDLE_JOB_TIMEOUT_SECONDS` | `3600` | Max job duration before forced failure |
| `StreamingMaxMemoryMb` | `ASSET_STREAMING_MAX_MEMORY_MB` | `100` | Max memory for streaming operations |
| `StreamingPartSizeMb` | `ASSET_STREAMING_PART_SIZE_MB` | `50` | Part size for streaming multipart uploads |
| `StreamingMaxConcurrentSourceStreams` | `ASSET_STREAMING_MAX_CONCURRENT_SOURCE_STREAMS` | `2` | Concurrent source bundle streams during assembly |
| `StreamingCompressionBufferKb` | `ASSET_STREAMING_COMPRESSION_BUFFER_KB` | `16384` | LZ4 compression buffer size (~16MB) |
| `StreamingProgressUpdateIntervalAssets` | `ASSET_STREAMING_PROGRESS_UPDATE_INTERVAL_ASSETS` | `10` | Assets processed before progress update |
| `ProcessingJobMaxWaitSeconds` | `ASSET_PROCESSING_JOB_MAX_WAIT_SECONDS` | `60` | Max wait for synchronous processing job |
| `ProcessingQueueCheckIntervalSeconds` | `ASSET_PROCESSING_QUEUE_CHECK_INTERVAL_SECONDS` | `30` | Queue poll interval when idle |
| `ProcessingBatchIntervalSeconds` | `ASSET_PROCESSING_BATCH_INTERVAL_SECONDS` | `5` | Delay between batch processing attempts |
| `ShutdownDrainTimeoutMinutes` | `ASSET_SHUTDOWN_DRAIN_TIMEOUT_MINUTES` | `2` | Max queue drain time on shutdown |
| `ShutdownDrainIntervalSeconds` | `ASSET_SHUTDOWN_DRAIN_INTERVAL_SECONDS` | `2` | Poll interval during drain |
| `DefaultBundleCacheTtlHours` | `ASSET_DEFAULT_BUNDLE_CACHE_TTL_HOURS` | `24` | Default bundle cache entry TTL |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<AssetService>` | Scoped | Structured logging |
| `AssetServiceConfiguration` | Singleton | Typed configuration access (63 properties) |
| `IStateStoreFactory` | Singleton | Access to asset-statestore and asset-processor-pool |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Self-subscription for async job processing |
| `IAssetEventEmitter` / `AssetEventEmitter` | Scoped | WebSocket session-targeted client notifications (8 event methods) |
| `IAssetStorageProvider` / `MinioStorageProvider` | Singleton | Object storage abstraction (presigned URLs, streaming, multipart) |
| `IOrchestratorClient` | Scoped | Processor pool scaling via orchestrator service |
| `IAssetProcessorPoolManager` / `AssetProcessorPoolManager` | Singleton | Redis-backed processor node state tracking |
| `IBundleConverter` / `BundleConverter` | Singleton | `.bannou` ↔ `.zip` format conversion |
| `BundleValidator` | Singleton | Bundle manifest validation |
| `IMinioClient` | Singleton | MinIO bucket operations and connectivity checks |
| `IAmazonS3` / `AmazonS3Client` | Singleton | Pre-signed URL generation (Content-Type signing workaround) |
| `IFFmpegService` / `FFmpegService` | Singleton | Audio/video transcoding via FFmpeg subprocess |
| `AssetProcessorRegistry` | Singleton | Content-type → processor routing |
| `TextureProcessor` | Singleton | Texture processing (format conversion, mipmap generation) |
| `ModelProcessor` | Singleton | 3D model processing (validation, optimization) |
| `AudioProcessor` | Singleton | Audio transcoding (WAV/FLAC → MP3/Opus/AAC via FFmpeg) |
| `AssetProcessingWorker` | HostedService | Background job consumer with heartbeat, graceful drain |
| `AssetMetrics` | Singleton | OpenTelemetry counters/histograms (meter: `BeyondImmersion.Bannou.Asset`) |
| `ContentTypeRegistry` | — | MIME type management with config-extensible allow/block lists |
| `MinioWebhookHandler` | — | S3 event notification handler for upload completion |
| `StreamingBundleWriter` | Per-use | Memory-bounded streaming metabundle assembly |

---

## API Endpoints (Implementation Notes)

### Assets (7 endpoints)

**Upload Flow** (`RequestUpload` → `CompleteUpload`): Two-phase upload. `RequestUpload` validates content type against forbidden list, generates upload ID, creates presigned PUT URL (or multipart initiation for files > `MultipartThresholdMb`), stores `UploadSession` in Redis. `CompleteUpload` is called after client uploads directly to MinIO; it computes SHA-256 hash of the stored object, derives a deterministic asset ID from the hash, copies from temp path to final path, creates `InternalAssetRecord`, updates type/realm/tag indexes using ETag-based optimistic concurrency, and conditionally delegates to processing pool if file is large and content type is processable.

**GetAsset**: Looks up asset record, generates presigned download URL with configurable TTL (`DownloadTokenTtlSeconds`). URL rewriting substitutes internal MinIO endpoint with `StoragePublicEndpoint` for Docker/K8s environments.

**SearchAssets**: Queries asset indexes (`asset-index:type:`, `asset-index:realm:`, `asset-index:tag:`) then intersects results. Returns paginated metadata without download URLs.

**BulkGetAssets**: Batch metadata lookup with optional download URL generation. Limited to `MaxBulkGetAssets` per request.

### Bundles (13 endpoints)

**CreateBundle**: Validates all referenced assets exist, determines inline vs. queued processing based on asset count. Inline path uses `StreamingBundleWriter` to assemble `.bannou` file (LZ4-compressed manifest + index + asset data) via server-side multipart upload. Stores bundle metadata with version 1, updates owner index and per-asset reverse indexes.

**UpdateBundle / DeleteBundle / RestoreBundle**: Standard lifecycle with versioning. Delete is soft-delete (sets `DeletedAt`, retains for `DeletedBundleRetentionDays`). Restore clears deletion timestamp.

**RequestBundleUpload**: For pre-made bundles uploaded by clients. Generates presigned URL; on completion, validates `.bannou` format integrity before accepting.

**ResolveBundles**: Given a set of desired asset IDs, computes the optimal set of bundles to download using a greedy set-cover algorithm. Prefers metabundles (larger coverage per download). Returns download plan with bundle IDs and per-bundle asset coverage.

**QueryBundlesByAsset**: Reverse lookup via `{realm}:asset-bundles:{assetId}` index.

### Metabundles (2 endpoints + 2 job endpoints)

**CreateMetabundle**: Merges multiple source bundles (and/or standalone assets) into a single super-bundle. Uses three-threshold decision for sync vs. async: source bundle count (`MetabundleAsyncSourceBundleThreshold`), total asset count (`MetabundleAsyncAssetCountThreshold`), estimated size (`MetabundleAsyncSizeBytesThreshold`). Async path queues `MetabundleJobQueuedEvent` and returns job ID for polling.

**GetJobStatus / CancelJob**: Poll async job progress (0-100%) or request cancellation. Job records retained for `MetabundleJobTtlSeconds` after completion.

---

## Visual Aid

```
Upload Flow & Processing Pipeline
==================================

Client                    Asset Service                     MinIO Storage
  │                           │                                │
  │─── RequestUpload ────────►│                                │
  │                           │── create UploadSession ──►Redis│
  │◄── presigned PUT URL ─────│                                │
  │                           │                                │
  │─── PUT (direct upload) ──────────────────────────────────►│
  │                           │                                │
  │                           │◄── S3 webhook ────────────────│
  │                           │  (MinioWebhookHandler)         │
  │                           │                                │
  │─── CompleteUpload ───────►│                                │
  │                           │── SHA-256 hash ───────────────►│ (stream object)
  │                           │── copy temp → final ──────────►│
  │                           │── update indexes ──►Redis      │
  │                           │                                │
  │                           │─── size > LargeFileThreshold?  │
  │                           │    AND processable type?       │
  │                           │         │                      │
  │                           │    ┌────┴────┐                 │
  │                           │   YES        NO               │
  │                           │    │          │                │
  │                           │    ▼          ▼                │
  │                           │ Dispatch    Done               │
  │                           │ to Pool                        │
  │                           │    │                           │
  │                           │    ▼                           │
  │                           │ AssetProcessingWorker          │
  │                           │  ├─ TextureProcessor           │
  │                           │  ├─ ModelProcessor             │
  │                           │  └─ AudioProcessor ──► FFmpeg  │
  │                           │    │                           │
  │                           │    ▼                           │
  │◄── client event ──────────│ asset.processing.completed     │
```

---

## Stubs & Unimplemented Features

1. **Texture and Model Processors**: `TextureProcessor` and `ModelProcessor` are registered but contain minimal implementations (validation only, no actual format conversion or optimization). The `AudioProcessor` with FFmpeg integration is the only fully functional processor.

2. **Filesystem/Azure/R2 storage providers**: `StorageProvider` config accepts `minio`, `s3`, `r2`, `azure`, `filesystem` but only MinIO/S3-compatible backends have an implementation (`MinioStorageProvider`). Other backends would require new `IAssetStorageProvider` implementations.

3. **Deleted bundle cleanup**: `DeletedBundleRetentionDays` is configurable but there is no background task that purges bundles past their retention window. Soft-deleted bundles accumulate indefinitely until manually cleaned.

4. **Bundle ZIP cache cleanup**: `ZipCacheTtlHours` is set but no scheduled task removes expired ZIP cache entries from the `bundles/zip-cache` storage path.

---

## Potential Extensions

1. **CDN integration**: The `StoragePublicEndpoint` rewriting could be extended to support CDN-fronted download URLs with cache invalidation on asset updates, reducing direct MinIO load for frequently accessed assets.

2. **Content-addressable deduplication**: Asset IDs are already SHA-256 derived. The service could detect duplicate uploads (same hash, different filename/tags) and deduplicate storage while maintaining separate metadata records.

3. **Webhook-based processing trigger**: Currently `CompleteUpload` synchronously decides whether to dispatch processing. A fully event-driven approach could have the webhook handler trigger processing, removing the need for the client to call `CompleteUpload` at all.

4. **Tiered storage**: Large, infrequently accessed assets could be migrated to cheaper storage tiers (S3 Glacier, Azure Cool) with automatic retrieval on access, using asset access timestamps.

5. **Bundle diffing**: When updating bundles, only changed assets could be uploaded as a delta, reducing bandwidth for incremental content updates.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **T25 (Internal POCO uses string for Guid)**: `BundleMetadata.BundleId`, `SourceBundleReferenceInternal.BundleId`, and related bundle model ID fields store GUIDs as strings requiring `Guid.Parse()` at usage sites. Should use `Guid` type directly.

2. **T25 (String constants instead of enum)**: `AssetProcessingResult.ErrorCode` and `AssetValidationResult.ErrorCode` use string constants (`"UNSUPPORTED_CONTENT_TYPE"`, `"FILE_TOO_LARGE"`, etc.). Should define an `AssetProcessingErrorCode` enum for compile-time validation.

### Design Considerations (Requires Planning)

1. **AssetId is SHA-256 hash string (intentional)**: `InternalAssetRecord.AssetId` and `AssetProcessingContext.AssetId` are SHA-256 content hashes stored as hex strings. This is NOT a T25 violation - these are legitimately strings (hashes), not GUIDs.

2. **Interface async contract vs sync implementation** - `TextureProcessor`, `ModelProcessor`, and stub paths use `await Task.CompletedTask` to satisfy async interfaces. Consider `ValueTask` or separate sync/async interface paths if this becomes a performance concern.

3. **Model property initialization** - `UploadSession` and `SourceBundleReferenceInternal` use `= string.Empty` without `required` keyword. Consider adding `required` to ensure properties are set at construction.

### Intentional Quirks (Documented Behavior)

1. **Dual S3 clients**: Both `IMinioClient` and `IAmazonS3` are registered because the MinIO .NET SDK has a bug where pre-signed PUT URLs include Content-Type in the signature, causing uploads with different Content-Type headers to fail with 403. The AWS SDK is used exclusively for pre-signed URL generation while MinIO SDK handles bucket operations. See: https://github.com/minio/minio-dotnet/issues/1150

2. **`application/octet-stream` as model type**: The content type registry includes `application/octet-stream` in the 3D model list because `.glb` files are commonly served with this generic MIME type. This means any `application/octet-stream` upload will match the model processor pool, even if it's not actually a 3D model.

3. **Self-consuming event pattern**: The service publishes `asset.metabundle.job.queued` and subscribes to it in the same service. This is intentional for load distribution: in multi-instance deployments, the instance that queues the job is not necessarily the one that processes it (any instance can pick up the event).

4. **Deterministic asset IDs from SHA-256**: Re-uploading identical content (same bytes) produces the same asset ID regardless of filename, content type, or tags. The second upload will overwrite the first asset's metadata but the storage object remains identical.

5. **Webhook validation is optional**: If `MinioWebhookSecret` is not set, all webhook requests are accepted without authentication. This simplifies development but requires network-level isolation in production.

6. **MinIO startup retry with exponential backoff cap**: The plugin waits up to 30 retries with delays capped at `5 × retryDelayMs` (10 seconds max). If MinIO is unavailable after all retries, the entire Asset service fails to start.

7. **CA2000 pragma suppression**: The MinIO connectivity check in `AssetServicePlugin` suppresses CA2000 (dispose warning) because the MinIO client's fluent builder API returns `this` from `Build()`, making the same object both builder and built client. The `using` on `Build()` result correctly disposes it.

### Design Considerations (Requires Planning)

1. **No orphaned index cleanup**: If a bundle is deleted but the per-asset reverse indexes (`{realm}:asset-bundles:{assetId}`) are not updated (e.g., due to crash mid-operation), stale bundle IDs will persist in those indexes. `ResolveBundles` will attempt to include deleted bundles in resolution results until it encounters a 404 on the bundle metadata lookup. A background reconciliation task or transactional index updates would address this.

2. **Optimistic concurrency on shared indexes**: Type/realm/tag indexes use ETag-based optimistic retry with configurable attempts. Under high concurrent upload rates targeting the same index key, all retries could exhaust, silently dropping the asset from that index. The failure is logged but not surfaced to the caller (upload still succeeds).

3. **No back-pressure on processing queue**: The `AssetProcessingWorker` polls for jobs at fixed intervals with `ProcessorMaxConcurrentJobs` concurrency, but there's no mechanism to reject or defer job dispatch when all processors are saturated. The orchestrator pool scaling is reactive (poll-based), meaning bursts of large uploads could queue indefinitely.

4. **Streaming metabundle memory model**: `StreamingMaxMemoryMb` limits buffer allocation, but the actual peak memory includes decompressed source bundle data being read, LZ4 compression buffers, and the multipart upload parts in flight. True memory usage can exceed the configured limit by `StreamingPartSizeMb + StreamingCompressionBufferKb/1024` MB.

5. **Event emission without transactional guarantees**: Asset record creation and event publication are separate operations. If the service crashes between saving the record and publishing `asset.upload.completed`, no retry mechanism re-publishes the event. Dependent services relying on events would miss the asset until a manual reconciliation.
