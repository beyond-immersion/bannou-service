# Asset Service Developer Guide

The Asset Service provides binary asset management for Bannou including storage, versioning, processing, and bundling of textures, 3D models, audio files, and asset bundles.

## Architecture Overview

The Asset Service uses a **pre-signed URL architecture** where all binary data transfers bypass WebSocket payloads entirely:

```
Client → Bannou API → Pre-signed URL → MinIO (direct upload/download)
                    ↓
              Webhook notification → Processing pipeline
```

Key architectural decisions:
- **Pre-signed URLs only**: All transfers via MinIO/S3, avoiding payload size constraints
- **Storage abstraction**: `IAssetStorageProvider` enables swappable backends (MinIO → S3 → R2)
- **Processing delegation**: Heavy operations offloaded to dedicated processing pool instances
- **Custom bundle format**: `.bannou` format optimized for streaming with LZ4 compression

## API Endpoints

All endpoints use POST-only design per Tenet 1 (zero-copy WebSocket routing).

### Upload Flow

#### Request Upload
```
POST /assets/upload/request
```

Request a pre-signed upload URL for a new asset.

**Request Body:**
```json
{
  "filename": "texture.png",
  "content_type": "image/png",
  "size_bytes": 1048576,
  "asset_type": "texture",
  "tags": ["player", "equipment"]
}
```

**Response:**
```json
{
  "upload_id": "550e8400-e29b-41d4-a716-446655440000",
  "upload_url": "https://demo.example.com:9000/bannou-assets/temp/...",
  "expires_at": "2025-01-15T12:00:00Z",
  "multipart": false
}
```

For files larger than 50MB (configurable), `multipart: true` with an array of `part_urls`.

**Note:** The `upload_url` uses the configured public endpoint (not the internal `minio:9000`). Configure via:
- `ASSET_STORAGE_PUBLIC_ENDPOINT` - Explicit public endpoint
- Falls back to `BANNOU_SERVICE_DOMAIN:9000` if not set

See [Deployment Guide - MinIO Storage Proxy](DEPLOYMENT.md#minio-storage-proxy) for OpenResty configuration.

#### Complete Upload
```
POST /assets/upload/complete
```

Finalize an upload after the client has transferred the file to MinIO.

**Request Body:**
```json
{
  "upload_id": "550e8400-e29b-41d4-a716-446655440000",
  "parts": [
    { "part_number": 1, "etag": "abc123..." }
  ]
}
```

### Download Flow

#### Get Asset
```
POST /assets/get
```

Get asset metadata and download URL.

**Request Body:**
```json
{
  "asset_id": "asset_123",
  "version_id": null
}
```

**Response:**
```json
{
  "asset_id": "asset_123",
  "filename": "texture.png",
  "content_type": "image/png",
  "size_bytes": 1048576,
  "download_url": "https://minio:9000/bannou-assets/assets/...",
  "expires_at": "2025-01-15T12:00:00Z"
}
```

### Search and Discovery

#### Search Assets
```
POST /assets/search
```

Search assets by tags, type, realm, or content type.

**Request Body:**
```json
{
  "tags": ["player"],
  "asset_type": "texture",
  "realm": "arcadia",
  "limit": 50,
  "offset": 0
}
```

#### List Versions
```
POST /assets/list-versions
```

List all versions of an asset.

### Bundle Operations

#### Create Bundle
```
POST /bundles/create
```

Create a new `.bannou` bundle from multiple assets.

**Request Body:**
```json
{
  "name": "player-equipment-v1",
  "asset_ids": ["asset_1", "asset_2", "asset_3"],
  "compression": "lz4"
}
```

#### Get Bundle
```
POST /bundles/get
```

Get bundle metadata and download URL. Supports on-the-fly ZIP conversion.

**Request Body:**
```json
{
  "bundle_id": "bundle_123",
  "format": "bannou"
}
```

Set `format: "zip"` for ZIP conversion (cached for 24 hours).

#### Request Bundle Upload
```
POST /bundles/upload/request
```

Request upload URL for a pre-built bundle (triggers validation pipeline).

## Configuration

Configuration is managed via the `AssetServiceConfiguration` class (generated from `schemas/asset-configuration.yaml`).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StorageProvider` | string | `"minio"` | Storage backend type |
| `StorageBucket` | string | `"bannou-assets"` | Primary storage bucket name |
| `StorageEndpoint` | string | `"http://minio:9000"` | Storage service endpoint |
| `StorageAccessKey` | string | - | Access key for storage |
| `StorageSecretKey` | string | - | Secret key for storage |
| `TokenTtlSeconds` | int | `3600` | Pre-signed URL expiration time |
| `MaxUploadSizeMb` | int | `500` | Maximum single file upload size |
| `MultipartThresholdMb` | int | `50` | Size threshold for multipart uploads |
| `LargeFileThresholdMb` | int | `50` | Size threshold for processing pool delegation |

### Environment Variables

```bash
# MinIO Configuration
BANNOU_ASSET_StorageEndpoint=http://minio:9000
BANNOU_ASSET_StorageBucket=bannou-assets
BANNOU_ASSET_StorageAccessKey=minioadmin
BANNOU_ASSET_StorageSecretKey=minioadmin

# Processing Configuration
BANNOU_ASSET_PROCESSING_MODE=both  # api, worker, or both
```

## Processing Pipeline

### Automatic Processing

When assets are uploaded, they're automatically processed based on type:

| Asset Type | Processor | Operations |
|------------|-----------|------------|
| `texture` | TextureProcessor | Format validation, dimension extraction, mipmap generation |
| `model` | ModelProcessor | Format validation, polygon count, material extraction |
| `audio` | AudioProcessor | Format validation, duration, sample rate detection |

### Processing Pool

For large files (>50MB by default), processing is delegated to dedicated instances:

1. Asset service calls `IOrchestratorClient.AcquireProcessorAsync()`
2. Orchestrator returns an available processor app-id
3. Processing request forwarded via mesh service invocation
4. Results returned to main asset service

Configure the processing pool in `provisioning/orchestrator/presets/asset-processing.yaml`.

## Client Events

The Asset Service emits real-time events to connected clients via WebSocket:

| Event | Topic | Description |
|-------|-------|-------------|
| `AssetUploadCompleteEvent` | `asset.upload.complete` | Upload finalized successfully |
| `AssetProcessingCompleteEvent` | `asset.processing.complete` | Processing finished successfully |
| `AssetProcessingFailedEvent` | `asset.processing.failed` | Processing encountered an error |
| `BundleValidationCompleteEvent` | `bundle.validation.complete` | Bundle validated successfully |
| `BundleValidationFailedEvent` | `bundle.validation.failed` | Bundle validation failed |
| `BundleCreationCompleteEvent` | `bundle.creation.complete` | Bundle created successfully |
| `AssetReadyEvent` | `asset.ready` | Asset fully processed and available |

Events are published to the session-specific topic `CONNECT_{sessionId}`.

## Bundle Format (.bannou)

The `.bannou` format is optimized for streaming game assets:

```
manifest.json     # Bundle metadata and asset list
index.bin         # Binary offset index (48 bytes per asset)
assets/*.chunk    # LZ4-compressed asset data
```

### Manifest Structure

```json
{
  "version": "1.0",
  "bundle_id": "bundle_123",
  "created_at": "2025-01-15T12:00:00Z",
  "assets": [
    {
      "asset_id": "asset_1",
      "filename": "texture.png",
      "content_type": "image/png",
      "size_bytes": 1048576,
      "compressed_size": 524288,
      "content_hash": "sha256:abc123..."
    }
  ]
}
```

### Index Format

Each entry is 48 bytes:
- Asset ID hash (16 bytes)
- Offset in chunk file (8 bytes)
- Original size (8 bytes)
- Compressed size (8 bytes)
- Flags (8 bytes)

## Health Checks

The Asset Service provides health checks for monitoring:

```csharp
services.AddHealthChecks()
    .AddAssetHealthChecks();
```

| Check | Tags | Description |
|-------|------|-------------|
| `minio` | storage, asset | MinIO storage connectivity |
| `redis` | cache, state, asset | Redis state store via lib-state |
| `processing-pool` | processing, asset | Processing pool availability |

## Metrics

Prometheus-compatible metrics are available via OpenTelemetry:

| Metric | Type | Description |
|--------|------|-------------|
| `asset_uploads_total` | Counter | Total uploads by type and success |
| `asset_downloads_total` | Counter | Total downloads by type |
| `asset_upload_duration_seconds` | Histogram | Upload duration by type |
| `asset_download_duration_seconds` | Histogram | Download duration by type |
| `asset_bundle_creations_total` | Counter | Bundle creations by success |
| `asset_processing_completed_total` | Counter | Completed processing operations |
| `asset_processing_failed_total` | Counter | Failed processing operations |
| `asset_processing_duration_seconds` | Histogram | Processing duration by type |

Configure OpenTelemetry to export from meter `BeyondImmersion.Bannou.Asset`.

## Storage Provider Interface

The `IAssetStorageProvider` abstraction enables different storage backends:

```csharp
public interface IAssetStorageProvider
{
    Task<PreSignedUploadResult> GenerateUploadUrlAsync(...);
    Task<PreSignedDownloadResult> GenerateDownloadUrlAsync(...);
    Task<MultipartUploadResult> InitiateMultipartUploadAsync(...);
    Task<MultipartUploadResult> CompleteMultipartUploadAsync(...);
    Task<AssetReference> CopyObjectAsync(...);
    Task<bool> DeleteObjectAsync(...);
    Task<IEnumerable<AssetVersion>> ListVersionsAsync(...);
    Task<AssetMetadata?> GetObjectMetadataAsync(...);
    bool SupportsCapability(StorageCapability capability);
}
```

### Implementing Custom Providers

1. Create a class implementing `IAssetStorageProvider`
2. Register in `AssetServicePlugin.ConfigureServices()`
3. Set `StorageProvider` configuration to your provider name

## Integration Examples

### Unity Client Upload

```csharp
// 1. Request upload URL
var request = new UploadRequest {
    Filename = "player_texture.png",
    ContentType = "image/png",
    SizeBytes = fileBytes.Length,
    AssetType = "texture"
};

var response = await bannou.Assets.RequestUploadAsync(request);

// 2. Upload directly to MinIO
using var client = new HttpClient();
var content = new ByteArrayContent(fileBytes);
await client.PutAsync(response.UploadUrl, content);

// 3. Complete the upload
await bannou.Assets.CompleteUploadAsync(new CompleteUploadRequest {
    UploadId = response.UploadId
});

// 4. Listen for AssetReadyEvent via WebSocket
```

### Downloading Assets

```csharp
// Get download URL
var asset = await bannou.Assets.GetAssetAsync(new GetAssetRequest {
    AssetId = "asset_123"
});

// Download from MinIO
using var client = new HttpClient();
var data = await client.GetByteArrayAsync(asset.DownloadUrl);
```

### Creating Bundles

```csharp
// Create bundle from multiple assets
var bundle = await bannou.Bundles.CreateBundleAsync(new CreateBundleRequest {
    Name = "level-1-assets",
    AssetIds = new[] { "asset_1", "asset_2", "asset_3" }
});

// Wait for BundleCreationCompleteEvent, then download
var download = await bannou.Bundles.GetBundleAsync(new GetBundleRequest {
    BundleId = bundle.BundleId,
    Format = "bannou"  // or "zip" for ZIP conversion
});
```
