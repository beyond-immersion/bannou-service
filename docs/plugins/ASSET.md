# Asset Plugin Deep Dive

> **Plugin**: lib-asset
> **Schema**: schemas/asset-api.yaml
> **Version**: 1.0.0
> **Layer**: AppFeatures
> **State Store**: asset-statestore (Redis), asset-processor-pool (Redis)
> **Implementation Map**: [docs/maps/ASSET.md](../maps/ASSET.md)

---

## Overview

The Asset service (L3 AppFeatures) provides storage, versioning, and distribution of large binary assets (textures, audio, 3D models) using MinIO/S3-compatible object storage. Issues pre-signed URLs so clients upload/download directly to the storage backend, never routing raw asset data through the WebSocket gateway. Also manages bundles (grouped assets in a custom `.bannou` format with LZ4 compression), metabundles (merged super-bundles), and a distributed processor pool for content-type-specific transcoding. Used by lib-behavior, lib-save-load, lib-mapping, and lib-documentation for binary storage needs.

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
| lib-procedural (planned) | Will store HDA templates and retrieve generated geometry assets via `IAssetClient` |

No external services subscribe to asset events; all event consumption is internal.

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `assetType` | C (System State/Mode) | `AssetType` enum (`texture`, `model`, `audio`, `behavior`, `bundle`, `prefab`, `other`) | Classifies asset content for processing pipeline routing; values are system-defined pipeline categories, not game content types |
| `processingStatus` | C (System State/Mode) | `ProcessingStatus` enum (`pending`, `processing`, `complete`, `failed`) | Tracks position in the processing pipeline state machine |
| `processingType` (events) | C (System State/Mode) | `ProcessingTypeEnum` enum (`mipmaps`, `lod_generation`, `transcode`, `compression`, `validation`, `behavior_compile`) | Identifies which processing operation is being performed; system pipeline stages |
| `bundleType` | C (System State/Mode) | `BundleType` enum (`source`, `metabundle`) | Distinguishes original bundles from server-composed super-bundles; structural system distinction |
| `compression` | C (System State/Mode) | `CompressionType` enum (`lz4`, `lzma`, `none`) | Algorithm selection for bundle compression; system infrastructure choice |
| `format` | C (System State/Mode) | `BundleFormat` enum (`bannou`, `zip`) | Wire format for bundle download; system transport choice |
| `realm` | B (Game Content Type) | Opaque string (`GameRealm`) | Realm stub name (e.g., `"shared"`, `"realm-1"`); references Realm service data, not a fixed enum |
| `owner` (events) | -- (Polymorphic identifier) | Plain string | Dual-purpose: accountId (UUID) for user uploads, service name string for service uploads; not a type field per se. Deviates from T14 polymorphic pattern (`ownerType` + `ownerId`) because Asset predates the convention and upload ownership is event-only metadata (not used for queries or referential integrity). Tracked for future cleanup. |

**Notes**:
- Asset service has no `EntityType` enum fields (Category A). Ownership is tracked via plain string `owner` fields rather than typed entity references.
- `GameRealm` is a plain `type: string` in the schema (not an enum), matching the opaque string pattern for game-configurable content.
- `ProcessorError` enum (7 values: `UnsupportedContentType`, `UnsupportedFormat`, `FileTooLarge`, `MissingExtension`, `SourceNotFound`, `TranscodingFailed`, `ProcessingError`) exists in the internal processor interface (`IAssetProcessor.cs`) for validation/processing error categorization. API-level error reporting uses the generated `ProcessingErrorCode` enum from the schema.

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
| `MinioStartupMaxRetries` | `ASSET_MINIO_STARTUP_MAX_RETRIES` | `30` | Max retries for MinIO startup connectivity check |
| `MinioStartupRetryDelayMs` | `ASSET_MINIO_STARTUP_RETRY_DELAY_MS` | `2000` | Delay between MinIO startup retries (ms) |
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
| `ProcessorNodeTtlSeconds` | `ASSET_PROCESSOR_NODE_TTL_SECONDS` | `180` | TTL for processor node state entries (> heartbeat timeout) |
| `FfmpegPath` | `ASSET_FFMPEG_PATH` | *nullable* | FFmpeg binary path (system PATH if unset) |
| `FfmpegWorkingDirectory` | `ASSET_FFMPEG_WORKING_DIR` | `/tmp/bannou-ffmpeg` | FFmpeg temp file directory |
| `AudioOutputFormat` | `ASSET_AUDIO_OUTPUT_FORMAT` | `mp3` | Default audio transcode format (mp3/opus/aac) |
| `AudioBitrateKbps` | `ASSET_AUDIO_BITRATE_KBPS` | `192` | Default audio bitrate |
| `AudioPreserveLossless` | `ASSET_AUDIO_PRESERVE_LOSSLESS` | `true` | Keep original alongside transcoded |
| `TextureMaxDimension` | `ASSET_TEXTURE_MAX_DIMENSION` | `4096` | Max texture dimension in pixels |
| `TextureDefaultOutputFormat` | `ASSET_TEXTURE_DEFAULT_OUTPUT_FORMAT` | `webp` | Default texture output format |
| `ModelOptimizeMeshesDefault` | `ASSET_MODEL_OPTIMIZE_MESHES_DEFAULT` | `true` | Default mesh optimization for 3D models |
| `ModelGenerateLodsDefault` | `ASSET_MODEL_GENERATE_LODS_DEFAULT` | `true` | Default LOD generation for 3D models |
| `ModelLodLevels` | `ASSET_MODEL_LOD_LEVELS` | `3` | Default LOD level count |
| `BundleCompressionDefault` | `ASSET_BUNDLE_COMPRESSION_DEFAULT` | `lz4` | Default bundle compression (lz4/lzma/none) |
| `ZipCacheTtlHours` | `ASSET_ZIP_CACHE_TTL_HOURS` | `24` | TTL for cached ZIP conversions |
| `ZipCacheDirectory` | `ASSET_ZIP_CACHE_DIRECTORY` | *nullable* | ZIP cache directory (system temp if unset) |
| `DeletedBundleRetentionDays` | `ASSET_DELETED_BUNDLE_RETENTION_DAYS` | `30` | Soft-delete retention period |
| `BundleCleanupIntervalMinutes` | `ASSET_BUNDLE_CLEANUP_INTERVAL_MINUTES` | `60` | Interval between bundle cleanup scans |
| `BundleCleanupStartupDelaySeconds` | `ASSET_BUNDLE_CLEANUP_STARTUP_DELAY_SECONDS` | `30` | Startup delay before first bundle cleanup scan |
| `ZipCacheCleanupIntervalMinutes` | `ASSET_ZIP_CACHE_CLEANUP_INTERVAL_MINUTES` | `120` | Interval between ZIP cache cleanup scans |
| `ZipCacheCleanupStartupDelaySeconds` | `ASSET_ZIP_CACHE_CLEANUP_STARTUP_DELAY_SECONDS` | `60` | Startup delay before first ZIP cache cleanup scan |
| `MinioWebhookSecret` | `ASSET_MINIO_WEBHOOK_SECRET` | *nullable* | Webhook validation secret (disabled if unset) |
| `TempUploadPathPrefix` | `ASSET_TEMP_UPLOAD_PATH_PREFIX` | `temp` | Bucket path for upload staging |
| `FinalAssetPathPrefix` | `ASSET_FINAL_ASSET_PATH_PREFIX` | `assets` | Bucket path for finalized assets |
| `BundleCurrentPathPrefix` | `ASSET_BUNDLE_CURRENT_PATH_PREFIX` | `bundles/current` | Bucket path for bundles |
| `BundleZipCachePathPrefix` | `ASSET_BUNDLE_ZIP_CACHE_PATH_PREFIX` | `bundles/zip-cache` | Bucket path for ZIP cache |
| `BundleUploadPathPrefix` | `ASSET_BUNDLE_UPLOAD_PATH_PREFIX` | `bundles/uploads` | Bucket path for bundle upload staging |
| `ProcessorAvailabilityMaxWaitSeconds` | `ASSET_PROCESSOR_AVAILABILITY_MAX_WAIT_SECONDS` | `60` | Max wait for processor availability |
| `ProcessorAvailabilityPollIntervalSeconds` | `ASSET_PROCESSOR_AVAILABILITY_POLL_INTERVAL_SECONDS` | `2` | Poll interval while waiting for processor |
| `ProcessorAcquisitionTimeoutSeconds` | `ASSET_PROCESSOR_ACQUISITION_TIMEOUT_SECONDS` | `600` | Max wait for processor pool acquisition |
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
| `DefaultListLimit` | `ASSET_DEFAULT_LIST_LIMIT` | `50` | Default results per page |
| `MaxQueryLimit` | `ASSET_MAX_QUERY_LIMIT` | `1000` | Max results per query/list request |
| `AudioLargeFileWarningThresholdMb` | `ASSET_AUDIO_LARGE_FILE_WARNING_THRESHOLD_MB` | `100` | Audio file size warning threshold |
| `TextureLargeFileWarningThresholdMb` | `ASSET_TEXTURE_LARGE_FILE_WARNING_THRESHOLD_MB` | `100` | Texture file size warning threshold |
| `ModelLargeFileWarningThresholdMb` | `ASSET_MODEL_LARGE_FILE_WARNING_THRESHOLD_MB` | `50` | Model file size warning threshold |

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

## Background Workers

| Worker | Purpose | Key Configuration |
|--------|---------|-------------------|
| `AssetProcessingWorker` | Polls for queued processing jobs, dispatches to content-type-specific processors (texture, model, audio) | `ProcessingQueueCheckIntervalSeconds`, `ProcessingBatchIntervalSeconds`, `ProcessorMaxConcurrentJobs` |
| Bundle Cleanup Worker | Purges soft-deleted bundles past retention window from Redis metadata and MinIO storage | `BundleCleanupIntervalMinutes`, `DeletedBundleRetentionDays` |
| ZIP Cache Cleanup Worker | Removes expired ZIP conversion cache entries | `ZipCacheCleanupIntervalMinutes`, `ZipCacheTtlHours` |

---

## Stubs & Unimplemented Features

1. **Texture and Model Processors**: `TextureProcessor` and `ModelProcessor` are registered but contain minimal implementations (validation only, no actual format conversion or optimization). The `AudioProcessor` with FFmpeg integration is the only fully functional processor.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/227 -->

2. **Filesystem/Azure/R2 storage providers**: `StorageProvider` config accepts `minio`, `s3`, `r2`, `azure`, `filesystem` but only MinIO/S3-compatible backends have an implementation (`MinioStorageProvider`). Other backends would require new `IAssetStorageProvider` implementations.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/239 -->

---

## Potential Extensions

1. **CDN integration**: The `StoragePublicEndpoint` rewriting could be extended to support CDN-fronted download URLs with cache invalidation on asset updates, reducing direct MinIO load for frequently accessed assets.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/519 -->

2. **Content-addressable deduplication**: Asset IDs are already SHA-256 derived. The service could detect duplicate uploads (same hash, different filename/tags) and deduplicate storage while maintaining separate metadata records.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/521 -->

3. **Webhook-based processing trigger**: Currently `CompleteUpload` synchronously decides whether to dispatch processing. A fully event-driven approach could have the webhook handler trigger processing, removing the need for the client to call `CompleteUpload` at all.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/522 -->

4. **Tiered storage**: Large, infrequently accessed assets could be migrated to cheaper storage tiers (S3 Glacier, Azure Cool) with automatic retrieval on access, using asset access timestamps.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/523 -->

5. **Bundle diffing**: When updating bundles, only changed assets could be uploaded as a delta, reducing bandwidth for incremental content updates.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/526 -->

6. **Internal tag hierarchy**: If hierarchical tag relationships are needed for smart bundling or query expansion (e.g., searching `furniture` finds `picture_frame`), Asset could implement a lightweight parent/child tag table in its own Redis state store. Asset (L3) cannot depend on lib-relationship (L2) for this — see [closed #117](https://github.com/beyond-immersion/bannou-service/issues/117). For hierarchical composition of assets, the Scene service (L4) already provides node trees with recursive resolution.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No bugs identified.)*

### Intentional Quirks (Documented Behavior)

1. **AssetId is SHA-256 hash string (intentional)**: `InternalAssetRecord.AssetId` and `AssetProcessingContext.AssetId` are SHA-256 content hashes stored as hex strings. This is NOT a T25 violation - these are legitimately strings (hashes), not GUIDs.

2. **BundleId is human-readable string (intentional)**: `BundleMetadata.BundleId` and related fields are human-provided identifiers like `"synty/polygon-adventure"`, `"game-assets-v1"`. This is NOT a T25 violation - developers need meaningful names to categorize and retrieve bundles. See SDK examples in `sdks/asset-bundler/README.md` and `sdks/bundle-format/README.md`.

3. **Dual S3 clients**: Both `IMinioClient` and `IAmazonS3` are registered because the MinIO .NET SDK has a bug where pre-signed PUT URLs include Content-Type in the signature, causing uploads with different Content-Type headers to fail with 403. The AWS SDK is used exclusively for pre-signed URL generation while MinIO SDK handles bucket operations. See: https://github.com/minio/minio-dotnet/issues/1150

4. **`application/octet-stream` as model type**: The content type registry includes `application/octet-stream` in the 3D model list because `.glb` files are commonly served with this generic MIME type. This means any `application/octet-stream` upload will match the model processor pool, even if it's not actually a 3D model.

5. **Self-consuming event pattern**: The service publishes `asset.metabundle.job.queued` and subscribes to it in the same service. This is intentional for load distribution: in multi-instance deployments, the instance that queues the job is not necessarily the one that processes it (any instance can pick up the event).

6. **Deterministic asset IDs from SHA-256**: Re-uploading identical content (same bytes) produces the same asset ID regardless of filename, content type, or tags. The second upload will overwrite the first asset's metadata but the storage object remains identical.

7. **Webhook validation is optional**: If `MinioWebhookSecret` is not set, all webhook requests are accepted without authentication. This simplifies development but requires network-level isolation in production.

8. **MinIO startup retry with exponential backoff cap**: The plugin waits up to `MinioStartupMaxRetries` (default 30) retries with delays capped at `5 × MinioStartupRetryDelayMs` (default 10 seconds max). If MinIO is unavailable after all retries, the entire Asset service fails to start. Both values are configurable.

9. **MinIO connectivity check uses try/finally dispose pattern**: The `WaitForMinioConnectivityAsync` method creates a temporary `MinioClient` with explicit try/finally disposal instead of `using` because the fluent API returns `this` from `Build()`, making `using` semantics unclear. The pattern is correct but differs from the standard `using` approach.

10. **Processor ValidateAsync uses `await Task.CompletedTask` (T23 compliant)**: `TextureProcessor.ValidateAsync`, `ModelProcessor.ValidateAsync`, `AudioProcessor.ValidateAsync`, and `MinioHealthCheck.CheckHealthAsync` use `await Task.CompletedTask` because they implement async interfaces with synchronous validation logic. This is the explicitly documented T23 pattern for synchronous implementations of async interfaces. No performance concern — `ValueTask` or separate sync/async interface paths would add complexity for zero benefit.

### Design Considerations (Requires Planning)

1. **Index failure caller notification**: Index retry exhaustion is now logged and emitted as an error event, but the upload still succeeds. Whether indexing failure should cause the upload to fail, or whether a reconciliation mechanism should exist, requires a design decision.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/536 -->

2. **No back-pressure on processing queue**: The `AssetProcessingWorker` polls for jobs at fixed intervals with `ProcessorMaxConcurrentJobs` concurrency, but there's no mechanism to reject or defer job dispatch when all processors are saturated. The orchestrator pool scaling is reactive (poll-based), meaning bursts of large uploads could queue indefinitely.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/537 -->

3. **Streaming metabundle memory model**: `StreamingMaxMemoryMb` limits buffer allocation, but the actual peak memory includes decompressed source bundle data being read, LZ4 compression buffers, and the multipart upload parts in flight. True memory usage can exceed the configured limit by `StreamingPartSizeMb + StreamingCompressionBufferKb/1024` MB.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/539 -->

4. **Event emission without transactional guarantees**: Asset record creation and event publication are separate operations. If the service crashes between saving the record and publishing `asset.upload.completed`, no retry mechanism re-publishes the event. Dependent services relying on events would miss the asset until a manual reconciliation.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/541 -->

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Resolved
- **Bundle cleanup workers implemented**: The `BundleCleanupIntervalMinutes` and `ZipCacheCleanupIntervalMinutes` configuration properties drive active cleanup workers. The Asset portion of [#156](https://github.com/beyond-immersion/bannou-service/issues/156) (item #1: "no cleanup task") is resolved.
- **#117 closed**: Tag hierarchy integration with lib-relationship was a hierarchy violation (L3 → L2). Closed with design alternatives noted in Potential Extensions #6.
