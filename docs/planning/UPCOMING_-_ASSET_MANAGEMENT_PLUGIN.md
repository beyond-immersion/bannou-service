# Asset Management Plugin - Planning Document

**Status**: PLANNING
**Priority**: High
**Complexity**: High
**Estimated Duration**: 8-12 weeks
**Dependencies**: Connect Service, MinIO/S3 Storage, Redis State Store
**Last Updated**: 2025-12-23

---

## Executive Summary

This document outlines the design for Bannou's Asset Management system, enabling storage, versioning, and distribution of large binary assets (textures, 3D models, audio, asset bundles) for the Arcadia game platform. The system must handle files ranging from kilobytes to gigabytes while working within Dapr's 4MB default payload limits and our WebSocket binary protocol constraints.

### Key Challenges

1. **Dapr Payload Limits**: Default 4MB limit (configurable to ~16MB max practical) makes service-to-service large file transfer impractical
2. **WebSocket Protocol**: Current 31-byte header protocol optimized for JSON payloads, not multi-megabyte binary transfers
3. **Stride Engine Limitations**: Built-in asset bundle system is **completely broken** - must build custom solution
4. **Versioning Requirements**: Short-term (immediate access) vs long-term (archived) asset version storage
5. **Scale Requirements**: 100,000+ NPCs with potentially unique appearance/behavior assets

### Recommended Architecture

**Pre-signed URL Approach**: Use MinIO/S3-compatible storage as the primary asset store with **all asset transfers using pre-signed URLs**. WebSocket handles metadata requests, coordination, and progress notifications only - never raw asset data. This:
- Bypasses Dapr entirely for file transfers
- Eliminates risk of oversized payloads or bad client behavior
- Maintains clean separation between coordination (WebSocket/Dapr) and data transfer (HTTPS/MinIO)
- Enables CDN integration for production deployment

**Storage Abstraction**: The storage layer is designed with swappable backends (MinIO self-hosted → AWS S3 → Cloudflare R2) via dependency injection, keeping business logic isolated from storage implementation.

---

## Table of Contents

1. [Research Findings](#1-research-findings)
   - [Game Engine Asset Systems](#11-game-engine-asset-systems)
   - [Dapr Transport Limitations](#12-dapr-transport-limitations)
   - [Current Protocol Analysis](#13-current-protocol-analysis)
2. [Architecture Design](#2-architecture-design)
   - [Storage Abstraction Layer](#21-storage-abstraction-layer)
   - [Storage Implementation (MinIO)](#22-storage-implementation-minio)
   - [Service Architecture](#23-service-architecture)
3. [Generic Asset Types](#3-generic-asset-types)
4. [Versioning Strategy](#4-versioning-strategy)
5. [Asset Bundle Format](#5-asset-bundle-format)
   - [Client-Uploaded Bundles](#55-client-uploaded-bundles)
6. [Prefabs and Hierarchical Assets](#6-prefabs-and-hierarchical-assets)
   - [Bundle-Prefab Relationship](#63-bundle-prefab-relationship)
7. [Client Integration](#7-client-integration)
8. [Implementation Roadmap](#8-implementation-roadmap)
9. [Open Questions](#9-open-questions)

---

## 1. Research Findings

### 1.1 Game Engine Asset Systems

#### Industry Research Summary

We researched Unity AssetBundles and Unreal PAK/IOStore systems. Key insights adopted:

| Insight | Source | How We're Using It |
|---------|--------|-------------------|
| LZ4 for runtime, LZMA for archives | Unity | LZ4 for hot storage, LZMA for cold/archived |
| Content-based hashing | Unity/Unreal | SHA256 deduplication across all assets |
| Footer-based index | Unreal PAK | Fast metadata reads without full file scan |
| Separate TOC from content | Unreal IOStore | `.manifest` + `.bundle` file pairs |
| Chunk-based streaming | Both | 16MB chunks for multipart uploads |

#### Stride Engine (Primary Target)

**CRITICAL FINDING: Asset Bundles Are Broken**

> "Custom bundles are COMPLETELY NON-FUNCTIONAL. Feature has been unused/untested for a while and degraded."
> — GitHub Issue #201

**What Works**:
- ✅ Default bundle (all non-bundled assets)
- ✅ YAML design-time serialization (source control friendly)
- ✅ Binary runtime serialization (optimized)
- ✅ Content-addressable storage (SHA hash filenames)
- ✅ Custom asset types via `[AssetDescription]` attributes

**What's Broken/Missing**:
- ❌ Custom bundle creation (fails during build)
- ❌ Progressive texture streaming
- ❌ Distance-based automatic unloading
- ❌ Background/async asset loading APIs
- ❌ Priority queue for load order

**Implication**: We must build a complete custom bundling and streaming system on top of Stride's ObjectDatabase.

### 1.2 Dapr Transport Limitations

#### Payload Size Limits

| Transport | Default Limit | Max Configurable | Notes |
|-----------|--------------|------------------|-------|
| HTTP Service Invocation | 4 MB | ~16 MB practical | `--dapr-http-max-request-size` flag |
| gRPC Service Invocation | 4 MB | ~16 MB | Same limit as HTTP in Dapr |
| Pub/Sub (RabbitMQ) | 16 MB (v4.0+) | 512 MB | Performance degrades >4KB messages |
| State Store (Redis) | 512 MB | Configurable | `proto-max-bulk-len` parameter |

**Configuration Example**:
```bash
# Dapr sidecar
dapr run --dapr-http-max-request-size 16 dotnet run

# Kubernetes annotation
dapr.io/max-body-size: "16Mi"
```

**Known Issues**:
- Actor responses fail >4MB even with increased limits
- gRPC channel configuration complexity
- No native streaming support for large files

#### Recommended Pattern: Claim Check

Dapr's official recommendation for large files:

```
1. Upload large file to external storage (MinIO/S3)
2. Pass reference (URL/key) between services via Dapr
3. Services download directly from storage when needed
```

This bypasses Dapr's payload limits entirely.

### 1.3 Design Decision: Pre-Signed URLs Over WebSocket

**Why not WebSocket for asset transfers?**

The existing WebSocket binary protocol (31-byte header) is optimized for JSON API payloads, not large binary files. Key limitations:
- No multi-part/chunked message support built-in
- Maximum practical payload ~500KB-1MB per WebSocket frame
- Would require significant protocol extensions

**Decision**: All asset transfers use **pre-signed URLs** via HTTPS directly to storage. WebSocket handles only:
- Metadata requests (get upload URL, get download URL)
- Progress/completion notifications (server → client events)
- Coordination (e.g., "asset ready" notifications)

This provides consistent flow for all asset sizes, CDN compatibility, and avoids service memory pressure.

---

## 2. Architecture Design

### 2.1 Storage Abstraction Layer

The storage layer is designed for **swappable backends** via dependency injection. Business logic in the Asset Service never interacts with storage directly - only through the abstraction.

#### Interface Design

```csharp
/// <summary>
/// Abstract storage provider for asset files.
/// Implementations: MinIO, AWS S3, Cloudflare R2, Azure Blob, Local Filesystem
/// </summary>
public interface IAssetStorageProvider
{
    /// <summary>
    /// Generate a pre-signed URL for uploading a new asset.
    /// </summary>
    Task<PreSignedUploadResult> GenerateUploadUrlAsync(
        string bucket,
        string key,
        string contentType,
        long expectedSize,
        TimeSpan expiration,
        IDictionary<string, string>? metadata = null);

    /// <summary>
    /// Generate a pre-signed URL for downloading an asset.
    /// </summary>
    Task<PreSignedDownloadResult> GenerateDownloadUrlAsync(
        string bucket,
        string key,
        string? versionId = null,
        TimeSpan? expiration = null);

    /// <summary>
    /// Generate pre-signed URLs for multipart upload (large files).
    /// </summary>
    Task<MultipartUploadResult> InitiateMultipartUploadAsync(
        string bucket,
        string key,
        string contentType,
        int partCount,
        TimeSpan partUrlExpiration);

    /// <summary>
    /// Complete a multipart upload after all parts are uploaded.
    /// </summary>
    Task<AssetReference> CompleteMultipartUploadAsync(
        string bucket,
        string key,
        string uploadId,
        IList<CompletedPart> parts);

    /// <summary>
    /// Copy an object within storage (for archival, versioning).
    /// </summary>
    Task<AssetReference> CopyObjectAsync(
        string sourceBucket,
        string sourceKey,
        string destBucket,
        string destKey,
        string? sourceVersionId = null);

    /// <summary>
    /// Delete an object or specific version.
    /// </summary>
    Task DeleteObjectAsync(
        string bucket,
        string key,
        string? versionId = null);

    /// <summary>
    /// List all versions of an object.
    /// </summary>
    Task<IList<ObjectVersionInfo>> ListVersionsAsync(
        string bucket,
        string keyPrefix);

    /// <summary>
    /// Get object metadata without downloading content.
    /// </summary>
    Task<ObjectMetadata> GetObjectMetadataAsync(
        string bucket,
        string key,
        string? versionId = null);

    /// <summary>
    /// Check if provider supports a capability.
    /// </summary>
    bool SupportsCapability(StorageCapability capability);
}

public enum StorageCapability
{
    Versioning,
    MultipartUpload,
    EventNotifications,
    ObjectLocking,
    ServerSideEncryption,
    ObjectTagging
}
```

#### Result Types

```csharp
public record PreSignedUploadResult(
    string UploadUrl,
    string Key,
    DateTime ExpiresAt,
    IDictionary<string, string> RequiredHeaders);

public record PreSignedDownloadResult(
    string DownloadUrl,
    string Key,
    string? VersionId,
    DateTime ExpiresAt,
    long? ContentLength,
    string? ContentType);

public record MultipartUploadResult(
    string UploadId,
    string Key,
    IList<PartUploadInfo> Parts,
    DateTime ExpiresAt);

public record PartUploadInfo(
    int PartNumber,
    string UploadUrl,
    long MinSize,
    long MaxSize);

public record CompletedPart(
    int PartNumber,
    string ETag);

public record AssetReference(
    string Bucket,
    string Key,
    string? VersionId,
    string ContentHash,
    long Size);
```

#### Service Registration Pattern

```csharp
// In AssetServicePlugin.cs
public class AssetServicePlugin : IServicePlugin
{
    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // Register storage provider based on configuration
        var storageType = config.GetValue<string>("Asset:StorageProvider") ?? "minio";

        switch (storageType.ToLowerInvariant())
        {
            case "minio":
            case "s3":
                services.AddSingleton<IAssetStorageProvider, S3StorageProvider>();
                services.Configure<S3StorageOptions>(config.GetSection("Asset:S3"));
                break;

            case "r2":
                services.AddSingleton<IAssetStorageProvider, CloudflareR2StorageProvider>();
                services.Configure<R2StorageOptions>(config.GetSection("Asset:R2"));
                break;

            case "azure":
                services.AddSingleton<IAssetStorageProvider, AzureBlobStorageProvider>();
                services.Configure<AzureBlobOptions>(config.GetSection("Asset:Azure"));
                break;

            case "filesystem":
                services.AddSingleton<IAssetStorageProvider, FilesystemStorageProvider>();
                services.Configure<FilesystemStorageOptions>(config.GetSection("Asset:Filesystem"));
                break;

            default:
                throw new InvalidOperationException($"Unknown storage provider: {storageType}");
        }

        // Register asset service (uses IAssetStorageProvider via DI)
        services.AddScoped<IAssetService, AssetService>();
    }
}
```

### 2.2 Storage Implementation (MinIO)

#### Why MinIO for Development/Self-Hosted?

| Requirement | MinIO Capability |
|-------------|------------------|
| S3-compatible API | Full compatibility with AWS S3 SDKs |
| Pre-signed URLs | Bypass Dapr/WebSocket for all transfers |
| Multipart Upload | Parallel chunk uploads for large files |
| Versioning | Built-in object versioning with version IDs |
| Event Notifications | Webhooks trigger processing pipelines |
| Local Development | Docker container for dev/CI |
| Self-Hosted Production | No cloud dependency, full control |

#### Bucket Structure

```
bannou-assets/
├── assets/                    # Individual asset files
│   ├── textures/
│   │   ├── {hash}.png
│   │   └── {hash}.dds
│   ├── models/
│   │   ├── {hash}.fbx
│   │   └── {hash}.glb
│   ├── audio/
│   │   └── {hash}.ogg
│   └── behaviors/
│       └── {hash}.yaml
│
├── bundles/                   # Asset bundles (ZIP-like containers)
│   ├── current/               # Active version bundles
│   │   ├── {bundle-id}.bundle
│   │   └── {bundle-id}.manifest
│   └── archived/              # Historical versions (compressed)
│       └── {bundle-id}/
│           └── v{version}.bundle.lz4
│
└── temp/                      # In-progress uploads
    └── {upload-id}/
        └── chunks/
```

#### MinIO Integration Components

```yaml
# Dapr component: minio-binding.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: asset-storage
spec:
  type: bindings.aws.s3  # MinIO is S3-compatible
  version: v1
  metadata:
  - name: bucket
    value: "bannou-assets"
  - name: region
    value: "us-east-1"
  - name: endpoint
    value: "http://minio:9000"
  - name: accessKey
    secretKeyRef:
      name: minio-credentials
      key: access-key
  - name: secretKey
    secretKeyRef:
      name: minio-credentials
      key: secret-key
  - name: forcePathStyle
    value: "true"
```

#### Pre-signed URL Flow

```
┌─────────┐      ┌─────────────┐      ┌─────────┐      ┌─────────┐
│  Client │      │   Connect   │      │  Asset  │      │  MinIO  │
│   App   │      │   Service   │      │ Service │      │         │
└────┬────┘      └──────┬──────┘      └────┬────┘      └────┬────┘
     │                  │                  │                │
     │ 1. Upload Request│                  │                │
     │  (WebSocket)     │                  │                │
     │─────────────────>│                  │                │
     │                  │                  │                │
     │                  │ 2. Generate URL  │                │
     │                  │    (Dapr)        │                │
     │                  │─────────────────>│                │
     │                  │                  │                │
     │                  │                  │ 3. Create      │
     │                  │                  │    Pre-signed  │
     │                  │                  │    URL         │
     │                  │                  │───────────────>│
     │                  │                  │                │
     │                  │                  │<───────────────│
     │                  │                  │  4. URL        │
     │                  │<─────────────────│                │
     │                  │                  │                │
     │<─────────────────│                  │                │
     │ 5. Upload URL    │                  │                │
     │    Response      │                  │                │
     │                  │                  │                │
     │ 6. Direct Upload │                  │                │
     │    (HTTPS)       │                  │                │
     │─────────────────────────────────────────────────────>│
     │                  │                  │                │
     │<─────────────────────────────────────────────────────│
     │ 7. Upload        │                  │                │
     │    Complete      │                  │                │
     │                  │                  │                │
     │                  │                  │ 8. Webhook     │
     │                  │                  │    Notification│
     │                  │                  │<───────────────│
     │                  │                  │                │
     │                  │ 9. Process Asset │                │
     │                  │<─────────────────│                │
     │                  │                  │                │
     │<─────────────────│                  │                │
     │ 10. Asset Ready  │                  │                │
     │     Event        │                  │                │
     └──────────────────┴──────────────────┴────────────────┘
```

### Decision: Always Pre-signed URLs

**All asset transfers use pre-signed URLs** - no binary data ever flows through WebSocket or Dapr. This provides:

- **Security**: Bad clients can't overflow buffers or send oversized payloads
- **Simplicity**: Single code path for all asset sizes
- **Scalability**: CDN-friendly, horizontally scalable storage
- **Reliability**: S3 multipart uploads handle resumption automatically

**Upload Flow**:
```json
// Client Request (via WebSocket API)
POST /assets/upload/request
{
  "filename": "high_res_texture.dds",
  "size": 134217728,
  "contentType": "image/vnd-ms.dds",
  "metadata": {
    "realm": "arcadia",
    "tags": ["texture", "ground", "dirt"]
  }
}

// Server Response
{
  "uploadId": "uuid-here",
  "uploadUrl": "https://minio.example.com/bannou-assets/temp/uuid/upload?X-Amz-...",
  "expiresAt": "2025-12-24T12:00:00Z",
  "multipart": {
    "required": true,
    "uploadId": "multipart-id",
    "partSize": 16777216,
    "parts": [
      {"partNumber": 1, "uploadUrl": "https://...?partNumber=1&..."},
      {"partNumber": 2, "uploadUrl": "https://...?partNumber=2&..."}
    ]
  }
}

// Client uploads directly to MinIO/S3 (HTTPS, not WebSocket)
// MinIO webhook notifies Asset Service on completion
// Asset Service processes and notifies client via WebSocket event
```

**Download Flow**:
```json
// Client Request
GET /assets/{assetId}?version=latest

// Server Response
{
  "assetId": "abc123",
  "downloadUrl": "https://minio.example.com/bannou-assets/assets/abc123?X-Amz-...",
  "expiresAt": "2025-12-24T12:00:00Z",
  "size": 134217728,
  "hash": "sha256:abcdef...",
  "contentType": "image/vnd-ms.dds",
  "metadata": {
    "realm": "arcadia",
    "tags": ["texture", "ground", "dirt"]
  }
}

// Client downloads directly from MinIO/S3 (HTTPS)
```

**Pre-signed URL Duration**: 1 hour default, configurable. URLs are short-lived to minimize risk if leaked, but long enough for large file transfers on slow connections.

### 2.3 NGINX/OpenResty Security Gateway

For self-hosted deployments, all asset transfers route through **NGINX with OpenResty** (Lua scripting) before reaching MinIO. This provides:

- **Session-scoped access**: Each pre-signed URL is tied to a specific session
- **JWT validation**: Same authentication as WebSocket connections
- **Rate limiting**: Prevent abuse of storage resources
- **Audit logging**: Track all asset access

#### Upload Token Architecture

```
┌─────────┐      ┌─────────────┐      ┌─────────────┐      ┌─────────┐
│  Client │      │   Connect   │      │    Asset    │      │  Redis  │
│   App   │      │   Service   │      │   Service   │      │         │
└────┬────┘      └──────┬──────┘      └──────┬──────┘      └────┬────┘
     │                  │                    │                  │
     │ 1. Request Upload│                    │                  │
     │    URL (WS API)  │                    │                  │
     │─────────────────>│                    │                  │
     │                  │                    │                  │
     │                  │ 2. Forward to      │                  │
     │                  │    Asset Service   │                  │
     │                  │───────────────────>│                  │
     │                  │                    │                  │
     │                  │                    │ 3. Generate      │
     │                  │                    │    upload token  │
     │                  │                    │    + store in    │
     │                  │                    │    Redis         │
     │                  │                    │─────────────────>│
     │                  │                    │                  │
     │                  │<───────────────────│                  │
     │<─────────────────│ 4. Return URL      │                  │
     │                  │    with token      │                  │
     │                  │                    │                  │
     ├──────────────────────────────────────────────────────────┤
     │                                                          │
     │        5. Direct upload to NGINX (with token)            │
     │──────────────────────────┐                               │
     │                          │                               │
     │                          ▼                               │
     │                  ┌───────────────┐                       │
     │                  │    NGINX +    │                       │
     │                  │   OpenResty   │                       │
     │                  └───────┬───────┘                       │
     │                          │                               │
     │                          │ 6. Lua validates token        │
     │                          │    against Redis              │
     │                          │─────────────────────────────>│
     │                          │                              │
     │                          │<─────────────────────────────│
     │                          │ 7. Token valid + session     │
     │                          │    matches                    │
     │                          │                               │
     │                          │ 8. Proxy to MinIO             │
     │                          │                               │
     │                          ▼                               │
     │                  ┌───────────────┐                       │
     │                  │     MinIO     │                       │
     │                  └───────────────┘                       │
     │                                                          │
     └──────────────────────────────────────────────────────────┘
```

#### Redis Token Schema

```
Key: asset:upload:{token_id}
TTL: 3600 (1 hour)
Value: {
  "session_id": "abc123",
  "account_id": "user-uuid",
  "bucket": "bannou-assets",
  "key": "temp/{upload_id}/file.ext",
  "max_size": 134217728,
  "content_type": "image/png",
  "created_at": "2025-12-23T10:00:00Z",
  "used": false
}
```

#### OpenResty Lua Validation

```lua
-- /etc/nginx/lua/validate_asset_token.lua
local redis = require "resty.redis"
local cjson = require "cjson"

local function validate_upload_token()
    -- Extract token from query parameter
    local token = ngx.var.arg_token
    if not token then
        ngx.status = 401
        ngx.say('{"error": "Missing upload token"}')
        return ngx.exit(401)
    end

    -- Connect to Redis
    local red = redis:new()
    red:set_timeout(1000)
    local ok, err = red:connect("redis", 6379)
    if not ok then
        ngx.log(ngx.ERR, "Redis connection failed: ", err)
        ngx.status = 500
        return ngx.exit(500)
    end

    -- Lookup token
    local token_data, err = red:get("asset:upload:" .. token)
    if not token_data or token_data == ngx.null then
        ngx.status = 401
        ngx.say('{"error": "Invalid or expired token"}')
        return ngx.exit(401)
    end

    local data = cjson.decode(token_data)

    -- Validate session matches (from JWT header)
    local jwt_session = ngx.var.http_x_session_id
    if data.session_id ~= jwt_session then
        ngx.status = 403
        ngx.say('{"error": "Session mismatch"}')
        return ngx.exit(403)
    end

    -- Check if token already used (one-time tokens)
    if data.used then
        ngx.status = 410
        ngx.say('{"error": "Token already used"}')
        return ngx.exit(410)
    end

    -- Mark token as used (atomic operation)
    local updated = cjson.encode({
        session_id = data.session_id,
        account_id = data.account_id,
        bucket = data.bucket,
        key = data.key,
        max_size = data.max_size,
        content_type = data.content_type,
        created_at = data.created_at,
        used = true,
        used_at = ngx.time()
    })
    red:setex("asset:upload:" .. token, 300, updated)  -- Keep for 5 min after use

    -- Set headers for upstream (MinIO)
    ngx.req.set_header("X-Validated-Bucket", data.bucket)
    ngx.req.set_header("X-Validated-Key", data.key)

    -- Return connection to pool
    red:set_keepalive(10000, 100)
end

return validate_upload_token
```

#### NGINX Configuration

```nginx
# /etc/nginx/conf.d/asset-upload.conf

upstream minio {
    server minio:9000;
    keepalive 32;
}

server {
    listen 443 ssl http2;
    server_name assets.bannou.example.com;

    ssl_certificate /etc/nginx/ssl/cert.pem;
    ssl_certificate_key /etc/nginx/ssl/key.pem;

    # Asset upload endpoint
    location /upload {
        # Validate token via Lua
        access_by_lua_file /etc/nginx/lua/validate_asset_token.lua;

        # Rate limiting per session
        limit_req zone=asset_upload burst=5 nodelay;

        # Max upload size (configurable per deployment)
        client_max_body_size 500M;

        # Proxy to MinIO
        proxy_pass http://minio;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Streaming upload (don't buffer)
        proxy_request_buffering off;
        proxy_http_version 1.1;
    }

    # Asset download endpoint (similar validation)
    location /download {
        access_by_lua_file /etc/nginx/lua/validate_asset_token.lua;

        limit_req zone=asset_download burst=20 nodelay;

        proxy_pass http://minio;
        proxy_set_header Host $host;

        # Enable sendfile for efficient downloads
        sendfile on;
        tcp_nopush on;
    }
}

# Rate limiting zones
limit_req_zone $http_x_session_id zone=asset_upload:10m rate=10r/s;
limit_req_zone $http_x_session_id zone=asset_download:10m rate=50r/s;
```

#### Security Properties

| Property | Implementation |
|----------|----------------|
| **Session Isolation** | Token tied to session_id, validated on each request |
| **Time-Limited** | 1-hour TTL on tokens (configurable via `ASSET_TOKEN_TTL_SECONDS`) |
| **One-Time Use** | Upload tokens marked as `used` after first request |
| **Size Limits** | `max_size` enforced before proxying to MinIO |
| **Content-Type** | Validated against declared type in token |
| **No URL Sharing** | Token + session header required (can't share URL) |

#### Environment Configuration

```bash
# .env configuration for asset security
ASSET_TOKEN_TTL_SECONDS=3600          # 1 hour default
ASSET_MAX_UPLOAD_SIZE_MB=500          # Per-file limit
ASSET_MULTIPART_THRESHOLD_MB=50       # When to use multipart
ASSET_DOWNLOAD_RATE_LIMIT=50          # Requests per second per session
ASSET_UPLOAD_RATE_LIMIT=10            # Requests per second per session
```

### 2.4 Service Architecture

#### Asset Service (lib-asset)

**Responsibilities**:
- Asset metadata management (CRUD)
- Pre-signed URL generation
- Version management
- Bundle creation/extraction
- Processing pipeline coordination

**OpenAPI Schema** (`schemas/asset-api.yaml`):

> **Tenet 1 Compliance**: All endpoints use POST with request body. No GET with path parameters.

```yaml
openapi: 3.0.3
info:
  title: Asset Service API
  version: 1.0.0

servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method

paths:
  # ═══════════════════════════════════════════════════════════════
  # Asset Upload Operations
  # ═══════════════════════════════════════════════════════════════

  /assets/upload/request:
    post:
      operationId: requestUpload
      summary: Request upload URL for a new asset
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UploadRequest'
      responses:
        '200':
          description: Upload URL generated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UploadResponse'

  /assets/upload/complete:
    post:
      operationId: completeUpload
      summary: Mark upload as complete, trigger processing
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CompleteUploadRequest'
      responses:
        '200':
          description: Upload completed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AssetMetadata'

  # ═══════════════════════════════════════════════════════════════
  # Asset Retrieval Operations (POST-only per Tenet 1)
  # ═══════════════════════════════════════════════════════════════

  /assets/get:
    post:
      operationId: getAsset
      summary: Get asset metadata and download URL
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [asset_id]
              properties:
                asset_id:
                  type: string
                  description: "Asset identifier"
                version:
                  type: string
                  default: "latest"
                  description: "Version ID or 'latest'"
      responses:
        '200':
          description: Asset metadata with download URL
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AssetWithDownloadUrl'

  /assets/list-versions:
    post:
      operationId: listAssetVersions
      summary: List all versions of an asset
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [asset_id]
              properties:
                asset_id:
                  type: string
                limit:
                  type: integer
                  default: 50
                offset:
                  type: integer
                  default: 0
      responses:
        '200':
          description: List of asset versions
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AssetVersionList'

  /assets/search:
    post:
      operationId: searchAssets
      summary: Search assets by tags, type, or realm
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              properties:
                tags:
                  type: array
                  items:
                    type: string
                asset_type:
                  type: string
                  enum: [texture, model, audio, behavior, bundle, prefab, other]
                realm:
                  type: string
                  enum: [omega, arcadia, fantasia, shared]
                content_type:
                  type: string
                limit:
                  type: integer
                  default: 50
                offset:
                  type: integer
                  default: 0
      responses:
        '200':
          description: Matching assets
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AssetSearchResult'

  # ═══════════════════════════════════════════════════════════════
  # Bundle Operations (POST-only per Tenet 1)
  # ═══════════════════════════════════════════════════════════════

  /bundles/create:
    post:
      operationId: createBundle
      summary: Create asset bundle from multiple assets
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateBundleRequest'
      responses:
        '200':
          description: Bundle creation started
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CreateBundleResponse'

  /bundles/get:
    post:
      operationId: getBundle
      summary: Get bundle manifest and download URL
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [bundle_id]
              properties:
                bundle_id:
                  type: string
                format:
                  type: string
                  enum: [bannou, zip]
                  default: bannou
      responses:
        '200':
          description: Bundle manifest with download URL
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BundleWithDownloadUrl'

  /bundles/upload/request:
    post:
      operationId: requestBundleUpload
      summary: Request upload URL for a pre-made bundle
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BundleUploadRequest'
      responses:
        '200':
          description: Upload URL generated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UploadResponse'

components:
  schemas:
    UploadRequest:
      type: object
      required:
        - filename
        - size
        - content_type
      properties:
        filename:
          type: string
        size:
          type: integer
          format: int64
        content_type:
          type: string
        metadata:
          $ref: '#/components/schemas/AssetMetadataInput'

    CompleteUploadRequest:
      type: object
      required:
        - upload_id
      properties:
        upload_id:
          type: string
          format: uuid
        parts:
          type: array
          description: "For multipart uploads - ETags of completed parts"
          items:
            type: object
            properties:
              part_number:
                type: integer
              etag:
                type: string

    UploadResponse:
      type: object
      required:
        - upload_id
        - upload_url
        - expires_at
      properties:
        upload_id:
          type: string
          format: uuid
        upload_url:
          type: string
          format: uri
        expires_at:
          type: string
          format: date-time
        multipart:
          $ref: '#/components/schemas/MultipartConfig'

    MultipartConfig:
      type: object
      properties:
        required:
          type: boolean
        part_size:
          type: integer
        max_parts:
          type: integer
        upload_urls:
          type: array
          items:
            type: object
            properties:
              part_number:
                type: integer
              upload_url:
                type: string
                format: uri

    AssetMetadataInput:
      type: object
      properties:
        asset_type:
          type: string
          enum: [texture, model, audio, behavior, bundle, prefab, other]
        realm:
          type: string
          enum: [omega, arcadia, fantasia, shared]
        tags:
          type: array
          items:
            type: string

    AssetMetadata:
      type: object
      properties:
        asset_id:
          type: string
        content_hash:
          type: string
        filename:
          type: string
        content_type:
          type: string
        size:
          type: integer
          format: int64
        asset_type:
          type: string
          enum: [texture, model, audio, behavior, bundle, prefab, other]
        realm:
          type: string
          enum: [omega, arcadia, fantasia, shared]
        tags:
          type: array
          items:
            type: string
        processing_status:
          type: string
          enum: [pending, processing, complete, failed]
        created_at:
          type: string
          format: date-time
        updated_at:
          type: string
          format: date-time

    AssetWithDownloadUrl:
      type: object
      properties:
        asset_id:
          type: string
        version_id:
          type: string
        download_url:
          type: string
          format: uri
        expires_at:
          type: string
          format: date-time
        size:
          type: integer
          format: int64
        content_hash:
          type: string
        content_type:
          type: string
        metadata:
          $ref: '#/components/schemas/AssetMetadata'

    AssetVersionList:
      type: object
      properties:
        asset_id:
          type: string
        versions:
          type: array
          items:
            type: object
            properties:
              version_id:
                type: string
              created_at:
                type: string
                format: date-time
              size:
                type: integer
                format: int64
              is_archived:
                type: boolean
        total:
          type: integer
        limit:
          type: integer
        offset:
          type: integer

    AssetSearchResult:
      type: object
      properties:
        assets:
          type: array
          items:
            $ref: '#/components/schemas/AssetMetadata'
        total:
          type: integer
        limit:
          type: integer
        offset:
          type: integer

    CreateBundleRequest:
      type: object
      required:
        - bundle_id
        - asset_ids
      properties:
        bundle_id:
          type: string
        version:
          type: string
          default: "1.0.0"
        asset_ids:
          type: array
          items:
            type: string
        compression:
          type: string
          enum: [lz4, lzma, none]
          default: lz4
        metadata:
          type: object
          additionalProperties: true

    CreateBundleResponse:
      type: object
      properties:
        bundle_id:
          type: string
        status:
          type: string
          enum: [queued, processing]
        estimated_size:
          type: integer
          format: int64

    BundleWithDownloadUrl:
      type: object
      properties:
        bundle_id:
          type: string
        version:
          type: string
        download_url:
          type: string
          format: uri
        format:
          type: string
          enum: [bannou, zip]
        expires_at:
          type: string
          format: date-time
        size:
          type: integer
          format: int64
        asset_count:
          type: integer
        from_cache:
          type: boolean
          description: "True if ZIP format was served from conversion cache"

    BundleUploadRequest:
      type: object
      required:
        - filename
        - size
      properties:
        filename:
          type: string
          description: "Must end with .bannou or .zip"
        size:
          type: integer
          format: int64
        manifest_preview:
          type: object
          properties:
            bundle_id:
              type: string
            version:
              type: string
            asset_count:
              type: integer
```

#### Asset Processing Pipeline

```
┌──────────────────────────────────────────────────────────────────┐
│                    Asset Processing Pipeline                      │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────┐    ┌─────────────┐    ┌─────────────┐    ┌────────┐ │
│  │  MinIO  │───>│   Webhook   │───>│  RabbitMQ   │───>│ Asset  │ │
│  │ Upload  │    │ Notification│    │ bannou-pubsub│    │Service │ │
│  │Complete │    │             │    │             │    │        │ │
│  └─────────┘    └─────────────┘    └─────────────┘    └───┬────┘ │
│                                                           │      │
│                    ┌──────────────────────────────────────┘      │
│                    │                                             │
│                    ▼                                             │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                   Processing Tasks                          │ │
│  ├─────────────────────────────────────────────────────────────┤ │
│  │                                                             │ │
│  │  Textures:                                                  │ │
│  │  ├─ Generate mipmaps                                        │ │
│  │  ├─ Convert to GPU formats (BC7, ASTC, ETC2)                │ │
│  │  ├─ Create thumbnails                                       │ │
│  │  └─ Calculate perceptual hash                               │ │
│  │                                                             │ │
│  │  Models:                                                    │ │
│  │  ├─ Validate geometry                                       │ │
│  │  ├─ Generate LODs                                           │ │
│  │  ├─ Extract materials list                                  │ │
│  │  └─ Calculate bounding box                                  │ │
│  │                                                             │ │
│  │  Audio:                                                     │ │
│  │  ├─ Transcode to Vorbis/Opus                                │ │
│  │  ├─ Calculate duration                                      │ │
│  │  └─ Generate waveform preview                               │ │
│  │                                                             │ │
│  │  Behaviors (ABML):                                          │ │
│  │  ├─ Validate YAML syntax                                    │ │
│  │  ├─ Compile to binary format                                │ │
│  │  └─ Extract dependency graph                                │ │
│  │                                                             │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                    │                                             │
│                    ▼                                             │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                  Post-Processing                            │ │
│  ├─────────────────────────────────────────────────────────────┤ │
│  │  1. Move from temp/ to assets/                              │ │
│  │  2. Update asset metadata in state store                    │ │
│  │  3. Publish asset.ready event                               │ │
│  │  4. Notify requesting client via WebSocket                  │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

### 2.5 Processing Pool Architecture

Heavy asset processing (texture conversion, LOD generation, audio transcoding) runs on **dedicated processing instances** managed by the Orchestrator service. This isolates CPU-intensive work from the main API service.

#### Core Concepts

| Concept | Description |
|---------|-------------|
| **Processing Pool** | Set of bannou instances with only the Asset plugin enabled |
| **Unique App-ID** | Each instance has distinct Dapr identity (e.g., `asset-processor-1`) |
| **Round-Robin** | Asset Service requests app-id from Orchestrator, load distributed |
| **Persistent Instances** | Instances stay up and are reused (not ephemeral containers) |
| **Dapr Sidecar** | Each processing instance has full Dapr sidecar for state/events |

#### Processing Pool Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Processing Pool Architecture                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────┐    1. Processing Request    ┌─────────────────┐            │
│  │   Asset     │ ─────────────────────────>  │   Orchestrator  │            │
│  │   Service   │                             │    Service      │            │
│  └──────┬──────┘                             └────────┬────────┘            │
│         │                                             │                      │
│         │                                             │ 2. Return available  │
│         │                                             │    processor app-id  │
│         │                                             │                      │
│         │  <──────────────────────────────────────────┘                      │
│         │    app-id: "asset-processor-2"                                     │
│         │                                                                    │
│         │  3. Dapr Service Invocation                                        │
│         │     to app-id: "asset-processor-2"                                 │
│         │                                                                    │
│         ▼                                                                    │
│  ┌──────────────────────────────────────────────────────────────────┐       │
│  │                    Processing Pool (managed by Orchestrator)      │       │
│  ├──────────────────────────────────────────────────────────────────┤       │
│  │                                                                   │       │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │       │
│  │  │ asset-processor │  │ asset-processor │  │ asset-processor │  │       │
│  │  │       -1        │  │       -2        │  │       -3        │  │       │
│  │  ├─────────────────┤  ├─────────────────┤  ├─────────────────┤  │       │
│  │  │ [Asset Plugin]  │  │ [Asset Plugin]  │  │ [Asset Plugin]  │  │       │
│  │  │ [Dapr Sidecar]  │  │ [Dapr Sidecar]  │  │ [Dapr Sidecar]  │  │       │
│  │  │                 │  │   ◄── active    │  │                 │  │       │
│  │  │    (idle)       │  │   processing    │  │    (idle)       │  │       │
│  │  └─────────────────┘  └─────────────────┘  └─────────────────┘  │       │
│  │                                                                   │       │
│  └──────────────────────────────────────────────────────────────────┘       │
│                                                                              │
│  4. Processing complete → Publish event → Asset Service notifies client      │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### Orchestrator API Extensions

```yaml
# New endpoints for orchestrator-api.yaml

/orchestrator/processing-pool/acquire:
  post:
    operationId: acquireProcessor
    summary: Get an available processing instance app-id
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [pool_type]
            properties:
              pool_type:
                type: string
                enum: [asset-processor, behavior-compiler]
              prefer_idle:
                type: boolean
                default: true
    responses:
      '200':
        description: Processor assigned
        content:
          application/json:
            schema:
              type: object
              properties:
                app_id:
                  type: string
                  example: "asset-processor-2"
                instance_status:
                  type: string
                  enum: [idle, busy]
      '429':
        description: No processors available (all busy)
        content:
          application/json:
            schema:
              type: object
              properties:
                retry_after_ms:
                  type: integer
                  example: 5000
                queue_depth:
                  type: integer

/orchestrator/processing-pool/status:
  post:
    operationId: getPoolStatus
    summary: Get status of processing pool instances
    requestBody:
      content:
        application/json:
          schema:
            type: object
            properties:
              pool_type:
                type: string
    responses:
      '200':
        description: Pool status
        content:
          application/json:
            schema:
              type: object
              properties:
                pool_type:
                  type: string
                total_instances:
                  type: integer
                idle_instances:
                  type: integer
                busy_instances:
                  type: integer
                instances:
                  type: array
                  items:
                    type: object
                    properties:
                      app_id:
                        type: string
                      status:
                        type: string
                        enum: [idle, busy, unhealthy]
                      current_job:
                        type: string
                      uptime_seconds:
                        type: integer

/orchestrator/processing-pool/scale:
  post:
    operationId: scalePool
    summary: Adjust pool size (add or remove instances)
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [pool_type, target_count]
            properties:
              pool_type:
                type: string
              target_count:
                type: integer
                minimum: 0
                maximum: 20
    responses:
      '200':
        description: Scaling initiated

/orchestrator/processing-pool/cleanup:
  post:
    operationId: cleanupPool
    summary: Scale pool back to configured minimum
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [pool_type]
            properties:
              pool_type:
                type: string
    responses:
      '200':
        description: Cleanup initiated
```

#### Processing Pool Configuration

```yaml
# provisioning/orchestrator/presets/asset-processing.yaml
# Preset for asset processing pool deployment

services:
  processing_pools:
    - pool_type: "asset-processor"
      plugin: "asset"
      min_instances: 1
      max_instances: 5
      scale_up_threshold: 0.8    # Scale up when 80% busy
      scale_down_delay_seconds: 300
      instance_config:
        ASSET_SERVICE_ENABLED: "true"
        ASSET_PROCESSING_MODE: "worker"  # Only processing, no API
        ASSET_PROCESSING_TIMEOUT_SECONDS: "300"

    - pool_type: "behavior-compiler"
      plugin: "behavior"
      min_instances: 1
      max_instances: 3
      instance_config:
        BEHAVIOR_SERVICE_ENABLED: "true"
        BEHAVIOR_COMPILE_MODE: "worker"
```

#### Environment Configuration

```bash
# Processing pool configuration
ASSET_PROCESSING_POOL_ENABLED=true
ASSET_PROCESSING_POOL_MIN=1
ASSET_PROCESSING_POOL_MAX=5
ASSET_PROCESSING_TIMEOUT_SECONDS=300
ASSET_LARGE_FILE_THRESHOLD_MB=50   # Files above this go to processing pool
```

#### Asset Service Integration

```csharp
public class AssetService : IAssetService
{
    private readonly IOrchestratorClient _orchestrator;
    private readonly DaprClient _daprClient;
    private readonly AssetServiceConfiguration _config;

    public async Task<ProcessingResult> ProcessAssetAsync(
        string assetId,
        ProcessingOptions options)
    {
        // Small files: process in-service
        if (options.FileSize < _config.LargeFileThresholdBytes)
        {
            return await ProcessLocallyAsync(assetId, options);
        }

        // Large files: delegate to processing pool
        var acquireResult = await _orchestrator.AcquireProcessorAsync(
            new AcquireProcessorRequest { PoolType = "asset-processor" });

        if (acquireResult.StatusCode == 429)
        {
            // No processors available - caller handles retry
            throw new ProcessingUnavailableException(
                $"No processors available, retry after {acquireResult.RetryAfterMs}ms");
        }

        // Invoke processing on the assigned instance via Dapr
        var result = await _daprClient.InvokeMethodAsync<ProcessingRequest, ProcessingResult>(
            acquireResult.AppId,  // e.g., "asset-processor-2"
            "process-asset",
            new ProcessingRequest
            {
                AssetId = assetId,
                Options = options
            });

        return result;
    }
}
```

#### Retry Handling (Caller Responsibility)

When the processing pool returns 429 (no processors available), the **caller handles retry**:

```csharp
// In Asset Service upload completion handler
public async Task<CompleteUploadResponse> CompleteUploadAsync(CompleteUploadRequest request)
{
    // ... validate upload, move to storage ...

    // Attempt processing (non-blocking - fires event on completion)
    try
    {
        await ProcessAssetAsync(assetId, processingOptions);
    }
    catch (ProcessingUnavailableException ex)
    {
        // Queue for retry via delayed event
        await _daprClient.PublishEventAsync("bannou-pubsub", "asset.processing.queued",
            new AssetProcessingQueuedEvent
            {
                AssetId = assetId,
                Options = processingOptions,
                RetryAfterMs = ex.RetryAfterMs,
                AttemptNumber = 1
            });
    }

    // Return immediately - client gets event when processing completes
    return new CompleteUploadResponse
    {
        AssetId = assetId,
        Status = "processing",
        Message = "Upload complete, processing queued"
    };
}
```

#### Reusable Pattern

This processing pool pattern is **reusable for other plugins**:

| Pool Type | Plugin | Use Case |
|-----------|--------|----------|
| `asset-processor` | Asset | Texture conversion, LOD generation |
| `behavior-compiler` | Behavior | ABML YAML compilation |
| `voice-processor` | Voice | Audio processing, transcription |
| `map-generator` | Realm | Procedural terrain generation |

---

## 3. Generic Asset Types

The asset system supports **any file type** - not just predefined categories. While we provide specialized processing for common game assets, the system is fundamentally a general-purpose content-addressed storage system.

### 3.1 Asset Type Philosophy

**Core Principle**: An asset is any file with metadata. The system doesn't restrict what you can store.

| Category | Examples | Special Processing | Notes |
|----------|----------|-------------------|-------|
| **Textures** | .png, .jpg, .dds, .ktx2 | Mipmaps, GPU formats, thumbnails | Format conversion optional |
| **Models** | .fbx, .glb, .gltf, .obj | LOD generation, validation | Dependency extraction |
| **Audio** | .wav, .ogg, .mp3, .opus | Transcoding, normalization | Waveform preview |
| **Behaviors** | .yaml, .abml | YAML validation, compilation | Dependency graph |
| **Prefabs** | .prefab.yaml | Component validation, refs | Hierarchical |
| **Scenes** | .scene.yaml | Prefab refs, spatial data | Hierarchical |
| **Data** | .json, .csv, .xml | Schema validation (optional) | Game data |
| **Binary** | .bin, .dat, anything | None (passthrough) | Custom formats |

### 3.2 Asset Metadata Schema

All assets share a common metadata structure, with optional type-specific extensions:

```yaml
# Core metadata (required for all assets)
asset_metadata:
  # Identity
  asset_id: "uuid"                    # System-generated unique ID
  content_hash: "sha256:..."          # Content-based hash for deduplication

  # Classification
  filename: "ground_dirt.dds"         # Original filename
  content_type: "image/vnd-ms.dds"    # MIME type
  size: 1048576                       # Size in bytes

  # Organization
  tags: ["texture", "ground", "outdoor"]  # User-defined tags
  realm: "arcadia"                    # Optional: realm association

  # Timestamps
  created_at: "2025-12-23T10:00:00Z"
  updated_at: "2025-12-23T10:00:00Z"

  # Processing
  processing_status: "complete"       # pending, processing, complete, failed
  processing_outputs: []              # List of derived assets (mipmaps, LODs, etc.)

# Type-specific extensions (optional)
texture_metadata:
  width: 2048
  height: 2048
  format: "bc7"
  has_alpha: true
  mipmap_count: 12

model_metadata:
  vertex_count: 50000
  triangle_count: 25000
  has_skeleton: true
  material_slots: ["body", "eyes", "clothing"]
  bounding_box: { min: [-1,-1,-1], max: [1,2,1] }

audio_metadata:
  duration_seconds: 180.5
  sample_rate: 48000
  channels: 2
  codec: "vorbis"
```

### 3.3 Content-Type Detection

Asset type is determined by (in priority order):

1. **Explicit declaration** in upload request (`contentType` field)
2. **File extension** mapping to MIME type
3. **Magic bytes** detection for known formats
4. **Default** to `application/octet-stream` (generic binary)

```csharp
public interface IAssetTypeDetector
{
    /// <summary>
    /// Detect asset type from file content and metadata.
    /// </summary>
    Task<AssetTypeInfo> DetectTypeAsync(
        Stream contentStream,
        string? filename = null,
        string? declaredContentType = null);
}

public record AssetTypeInfo(
    string ContentType,           // MIME type
    string Category,              // texture, model, audio, prefab, etc.
    bool HasSpecializedProcessor, // Can we do more than passthrough?
    IList<string> SuggestedTags); // Auto-generated tags
```

### 3.4 Processing Pipeline Architecture

Processing is **opt-in** and **extensible**:

```csharp
/// <summary>
/// Processor for a specific asset type. Registered via DI.
/// </summary>
public interface IAssetProcessor
{
    /// <summary>
    /// Content types this processor handles.
    /// </summary>
    IEnumerable<string> SupportedContentTypes { get; }

    /// <summary>
    /// Process an uploaded asset, generating derived outputs.
    /// </summary>
    Task<ProcessingResult> ProcessAsync(
        AssetReference input,
        ProcessingOptions options,
        CancellationToken cancellationToken = default);
}

// Processors are registered in DI and discovered automatically
services.AddSingleton<IAssetProcessor, TextureProcessor>();
services.AddSingleton<IAssetProcessor, ModelProcessor>();
services.AddSingleton<IAssetProcessor, AudioProcessor>();
services.AddSingleton<IAssetProcessor, BehaviorProcessor>();
services.AddSingleton<IAssetProcessor, PrefabProcessor>();
// Custom processors can be added by plugins
```

**Unknown types pass through** - if no processor handles a content type, the asset is stored as-is without processing. This ensures any file can be uploaded and retrieved.

---

## 4. Versioning Strategy

### 4.1 Version Types

#### Short-Term Versions (Hot Storage)

- **Purpose**: Immediate access, active development
- **Storage**: Uncompressed in MinIO `assets/` prefix
- **Retention**: Last N versions (configurable, default 5)
- **Access**: Direct pre-signed URLs, sub-second retrieval

#### Long-Term Versions (Cold Storage)

- **Purpose**: Historical preservation, rollback capability
- **Storage**: LZ4-compressed in MinIO `archived/` prefix
- **Retention**: Indefinite (with lifecycle policies)
- **Access**: Requires decompression, higher latency acceptable

### 4.2 Version Identification

```
Asset ID: abc123
Version ID: v1703001234567 (timestamp-based)
Content Hash: sha256:abcdef1234567890...

Full Reference: abc123@v1703001234567
Or by hash: abc123@sha256:abcdef...
```

### 4.3 Version Metadata Schema

```yaml
# Stored alongside each version
version_metadata:
  asset_id: "abc123"
  version_id: "v1703001234567"
  content_hash: "sha256:abcdef..."
  created_at: "2025-12-23T10:00:00Z"
  created_by: "user-uuid"
  size_bytes: 134217728
  size_compressed: 89478485  # If archived
  compression: "lz4"         # If archived
  parent_version: "v1703000000000"  # Previous version
  change_description: "Updated normal map"
  tags:
    - "release-1.0"
    - "reviewed"
```

### 4.4 Archival Policy

```yaml
archival_policy:
  # When to archive (move from hot to cold)
  archive_after:
    age_days: 30
    newer_versions_exist: 3  # Archive when 3 newer versions exist

  # Compression settings for archived versions
  compression:
    algorithm: "lz4"
    level: 9  # Max compression

  # Deletion policy (optional)
  delete_archived_after:
    age_days: 365
    min_versions_to_keep: 1  # Always keep at least 1 archived version
```

---

## 5. Asset Bundle Format

### 5.1 Bundle Structure

Since Stride's bundle system is broken, we define our own format:

```
bundle-{id}.bannou
├── manifest.json          # Bundle metadata and asset list
├── index.bin              # Binary offset index for fast lookups
├── assets/                # Asset data (concatenated or individual)
│   ├── 0000.chunk         # First chunk of assets
│   ├── 0001.chunk         # Second chunk
│   └── ...
└── signature.sig          # Optional integrity signature
```

### 5.2 Manifest Format

```json
{
  "bundleId": "bundle-abc123",
  "version": "1.0.0",
  "created": "2025-12-23T10:00:00Z",
  "compression": "lz4",
  "encryption": null,
  "checksum": "sha256:bundlehash...",

  "assets": [
    {
      "assetId": "texture-001",
      "path": "textures/ground/dirt.dds",
      "offset": 0,
      "size": 1048576,
      "compressedSize": 524288,
      "hash": "sha256:assethash...",
      "type": "texture",
      "dependencies": []
    },
    {
      "assetId": "model-001",
      "path": "models/props/barrel.glb",
      "offset": 524288,
      "size": 2097152,
      "compressedSize": 1048576,
      "hash": "sha256:assethash...",
      "type": "model",
      "dependencies": ["texture-001", "material-005"]
    }
  ],

  "dependencies": {
    "externalBundles": ["core-textures-v1", "shared-materials-v2"],
    "requiredServices": ["behavior-v1.2"]
  },

  "metadata": {
    "realm": "arcadia",
    "region": "starter-town",
    "loadPriority": 1,
    "streamingHint": "preload"
  }
}
```

### 5.3 Binary Index Format

For fast asset lookups without parsing full manifest:

```
Index Header (32 bytes):
├─ Magic: "BNBI" (4 bytes)
├─ Version: uint32 (4 bytes)
├─ Asset Count: uint32 (4 bytes)
├─ Index Offset: uint64 (8 bytes)
├─ Data Offset: uint64 (8 bytes)
├─ Reserved: (4 bytes)

Per-Asset Entry (48 bytes each):
├─ Asset ID Hash: uint64 (8 bytes) - For fast lookup
├─ Data Offset: uint64 (8 bytes)
├─ Compressed Size: uint32 (4 bytes)
├─ Uncompressed Size: uint32 (4 bytes)
├─ Flags: uint16 (2 bytes) - Compression, encryption flags
├─ Type: uint16 (2 bytes) - Asset type enum
├─ Name Offset: uint32 (4 bytes) - Offset to name in string table
├─ Hash: 16 bytes - Truncated SHA256
```

### 5.4 Bundle Types

| Bundle Type | Purpose | Compression | Distribution |
|-------------|---------|-------------|--------------|
| **Core** | Essential game assets | LZ4 (fast) | Bundled with client |
| **Realm** | World-specific assets | LZ4 (fast) | Download on realm entry |
| **DLC** | Expansion content | LZMA (small) | Purchased download |
| **Patch** | Incremental updates | LZ4 (fast) | Auto-update |
| **Archive** | Historical versions | LZMA (small) | On-demand |
| **User** | Client-uploaded bundles | User-defined | User storage quota |

### 5.5 Client-Uploaded Bundles

Clients can upload **pre-made bundles** instead of individual assets. This enables:
- **Modding support**: Community content distribution
- **Batch uploads**: Efficient upload of related assets
- **Offline preparation**: Bundle creation on client side

#### Bundle Validation Pipeline

Client-uploaded bundles go through strict validation before acceptance:

```
┌──────────────────────────────────────────────────────────────────┐
│                  Client Bundle Upload Flow                       │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  1. Client uploads bundle file via pre-signed URL                │
│                         │                                        │
│                         ▼                                        │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │              Structure Validation                           │ │
│  ├─────────────────────────────────────────────────────────────┤ │
│  │  ✓ Valid archive format (ZIP or .bannou)                    │ │
│  │  ✓ manifest.json exists and is valid JSON                   │ │
│  │  ✓ All assets listed in manifest exist in archive           │ │
│  │  ✓ No path traversal attacks (../ in paths)                 │ │
│  │  ✓ Total uncompressed size within quota                     │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                         │                                        │
│                         ▼                                        │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │              Manifest Validation                            │ │
│  ├─────────────────────────────────────────────────────────────┤ │
│  │  ✓ Required fields present (bundleId, version, assets[])   │ │
│  │  ✓ Asset hashes match actual content                        │ │
│  │  ✓ No duplicate asset IDs                                   │ │
│  │  ✓ Dependencies reference valid bundles (if declared)       │ │
│  │  ✓ Bundle ID follows naming conventions                     │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                         │                                        │
│                         ▼                                        │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │              Content Validation (Optional)                  │ │
│  ├─────────────────────────────────────────────────────────────┤ │
│  │  ✓ File type matches declared content type                  │ │
│  │  ✓ Image dimensions within limits                           │ │
│  │  ✓ No executable content (.exe, .dll, scripts)              │ │
│  │  ✓ YAML/JSON assets parse without errors                    │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                         │                                        │
│                         ▼                                        │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │              Registration                                   │ │
│  ├─────────────────────────────────────────────────────────────┤ │
│  │  → Extract and register individual assets                   │ │
│  │  → Calculate content hashes for deduplication               │ │
│  │  → Store bundle in bundles/ with user prefix                │ │
│  │  → Index assets for search/discovery                        │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

#### Bundle Upload API

```json
// Request upload URL for a bundle
POST /bundles/upload/request
{
  "bundleFilename": "my-character-pack.bannou",
  "bundleSize": 52428800,
  "manifestPreview": {
    "bundleId": "user-123-character-pack-v1",
    "version": "1.0.0",
    "assetCount": 15
  }
}

// Response
{
  "uploadId": "uuid",
  "uploadUrl": "https://minio.../temp/uuid/bundle?...",
  "expiresAt": "...",
  "validationWebhook": true  // Server will validate after upload
}

// After upload completes, server validates and responds via WebSocket event
{
  "type": "bundle.validation.complete",
  "uploadId": "uuid",
  "success": true,
  "bundleId": "user-123-character-pack-v1",
  "assetsRegistered": 15,
  "duplicatesSkipped": 3,  // Assets with same hash already existed
  "warnings": []
}

// Or if validation fails
{
  "type": "bundle.validation.failed",
  "uploadId": "uuid",
  "success": false,
  "errors": [
    {"code": "INVALID_MANIFEST", "message": "manifest.json missing required field 'version'"},
    {"code": "HASH_MISMATCH", "asset": "texture-001", "message": "Declared hash doesn't match content"}
  ]
}
```

#### Supported Bundle Formats

| Format | Extension | Primary Use | Conversion |
|--------|-----------|-------------|------------|
| **Bannou Native** | `.bannou` | Internal storage, distribution | None (canonical) |
| **ZIP** | `.zip` | Upload, manual creation | Auto-convert to .bannou |

**Primary Format**: `.bannou` is the canonical storage format. All bundles are stored internally as `.bannou` regardless of upload format.

#### Format Conversion and Caching

Clients can upload ZIP bundles (easier to create with standard tools) and request ZIP downloads (easier to extract). The system automatically converts between formats with caching.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      Format Conversion Architecture                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  UPLOAD PATH (ZIP → .bannou)                                                 │
│  ──────────────────────────────                                              │
│  1. Client uploads bundle.zip                                                │
│  2. Validation checks ZIP structure                                          │
│  3. Processing pool converts to .bannou format                               │
│  4. .bannou stored as canonical version                                      │
│  5. ZIP upload deleted after conversion                                      │
│                                                                              │
│  DOWNLOAD PATH (.bannou → ZIP)                                               │
│  ──────────────────────────────                                              │
│  1. Client requests bundle with ?format=zip query parameter                  │
│  2. Check cache: temp/zip-cache/{bundle-id}.zip                              │
│  3. If cached and not expired → return cached ZIP URL                        │
│  4. If not cached → processing pool converts .bannou → ZIP                   │
│  5. Store ZIP in cache with 24-hour TTL                                      │
│  6. Return pre-signed URL to cached ZIP                                      │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### Conversion Cache Configuration

```bash
# Environment configuration for format conversion
ASSET_ZIP_CACHE_TTL_HOURS=24              # How long to keep converted ZIPs
ASSET_ZIP_CACHE_CLEANUP_INTERVAL_HOURS=6  # How often to clean expired cache
ASSET_ZIP_CACHE_MAX_SIZE_GB=50            # Max total cache size
```

#### API Format Selection

```yaml
# Download endpoint supports format selection
/bundles/{bundleId}/download:
  post:
    operationId: downloadBundle
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [bundle_id]
            properties:
              bundle_id:
                type: string
              format:
                type: string
                enum: [bannou, zip]
                default: bannou
                description: "bannou = native format (faster), zip = standard archive (cached)"
    responses:
      '200':
        description: Download URL
        content:
          application/json:
            schema:
              type: object
              properties:
                download_url:
                  type: string
                  format: uri
                format:
                  type: string
                expires_at:
                  type: string
                  format: date-time
                from_cache:
                  type: boolean
                  description: "True if ZIP was served from cache"
```

---

## 6. Prefabs and Hierarchical Assets

Arcadia uses **YAML-based hierarchical assets** for game objects - characters, buildings, scenes, and maps. These are structurally similar to bundles: both are containers that reference other assets.

### 6.1 Prefab Concept

A **prefab** is a reusable template defining a game object's structure:

```yaml
# character-warrior.prefab.yaml
prefab:
  id: "character-warrior-001"
  name: "Warrior Template"
  version: "1.0.0"

  # Component hierarchy (inspired by Stride but engine-agnostic)
  components:
    - type: "transform"
      position: [0, 0, 0]
      rotation: [0, 0, 0, 1]
      scale: [1, 1, 1]

    - type: "model"
      asset_ref: "model:warrior-body-001"      # Reference to model asset
      materials:
        - slot: "body"
          asset_ref: "material:warrior-armor"
        - slot: "skin"
          asset_ref: "material:human-skin-01"

    - type: "skeleton"
      asset_ref: "skeleton:humanoid-standard"

    - type: "behavior"
      asset_ref: "behavior:npc-warrior-combat"  # ABML behavior reference

    - type: "audio"
      footsteps: "audio:footstep-metal"
      voice_set: "audio:voice-male-gruff"

  # Nested prefabs (composition)
  children:
    - prefab_ref: "prefab:weapon-sword-001"
      attach_point: "hand_right"

    - prefab_ref: "prefab:armor-helmet-001"
      attach_point: "head"

  # Metadata for search/organization
  tags: ["npc", "warrior", "humanoid", "combat"]
  realm: "arcadia"
```

### 6.2 Scene/Map Prefabs

Scenes are prefabs that contain spatial data and other prefabs:

```yaml
# starter-town.scene.yaml
scene:
  id: "scene-starter-town"
  name: "Starter Town"
  version: "1.0.0"

  # World settings
  settings:
    bounds: { min: [-500, -10, -500], max: [500, 100, 500] }
    ambient_light: { color: [1, 0.95, 0.9], intensity: 0.3 }
    skybox: "texture:skybox-daytime"

  # Terrain/environment
  terrain:
    heightmap: "asset:terrain-starter-town-heightmap"
    layers:
      - texture: "texture:grass-01"
        normal: "texture:grass-01-normal"
      - texture: "texture:dirt-01"
        normal: "texture:dirt-01-normal"

  # Placed prefabs with transforms
  entities:
    - prefab_ref: "prefab:building-tavern-001"
      position: [50, 0, 30]
      rotation: [0, 45, 0]

    - prefab_ref: "prefab:building-blacksmith-001"
      position: [80, 0, 45]
      rotation: [0, -30, 0]

    - prefab_ref: "prefab:npc-spawn-point"
      position: [60, 0, 35]
      metadata:
        spawn_type: "merchant"
        behavior_override: "behavior:npc-merchant-weapons"

  # Streaming zones for large scenes
  streaming_zones:
    - id: "zone-town-center"
      bounds: { min: [0, 0, 0], max: [100, 50, 100] }
      priority: 1  # Always loaded

    - id: "zone-town-outskirts"
      bounds: { min: [-200, 0, -200], max: [0, 50, 0] }
      priority: 2  # Load when player approaches
```

### 6.3 Bundle-Prefab Relationship

**Your intuition is correct**: Bundles and prefabs are structurally similar - both define contents and references. The key difference is their purpose:

| Aspect | Bundle | Prefab |
|--------|--------|--------|
| **Purpose** | Distribution/packaging | Runtime instantiation |
| **Contains** | Raw asset files | Component configurations |
| **References** | Asset IDs with offsets | Asset IDs for loading |
| **Hierarchy** | Flat (assets listed) | Tree (nested components) |
| **Versioning** | Bundle version | Prefab version |
| **Compression** | LZ4/LZMA | None (YAML text) |

**The relationship**:

```
Bundle "character-warrior-bundle"
├── manifest.json                    # Lists all assets
├── warrior-body-001.glb             # Model file
├── warrior-armor.mat                # Material file
├── human-skin-01.mat                # Material file
├── humanoid-standard.skeleton       # Skeleton file
├── npc-warrior-combat.behavior      # Compiled ABML
├── weapon-sword-001.prefab.yaml     # Nested prefab
├── armor-helmet-001.prefab.yaml     # Nested prefab
└── character-warrior-001.prefab.yaml # Root prefab

The prefab REFERENCES assets by ID.
The bundle CONTAINS those assets.
Together they form a complete, distributable game object.
```

### 6.4 Prefab Processing Pipeline

When a prefab is uploaded:

```csharp
public class PrefabProcessor : IAssetProcessor
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "application/x-bannou-prefab",
        "text/yaml"  // With .prefab.yaml extension
    };

    public async Task<ProcessingResult> ProcessAsync(
        AssetReference input,
        ProcessingOptions options,
        CancellationToken ct)
    {
        // 1. Parse YAML
        var prefab = await ParsePrefabYamlAsync(input);

        // 2. Extract and validate all asset references
        var refs = ExtractAssetReferences(prefab);
        var validationResult = await ValidateReferencesExistAsync(refs);

        if (!validationResult.AllValid)
        {
            return ProcessingResult.Failed(
                $"Missing referenced assets: {string.Join(", ", validationResult.Missing)}");
        }

        // 3. Build dependency graph
        var dependencies = await BuildDependencyGraphAsync(prefab);

        // 4. Store processed prefab with resolved references
        return ProcessingResult.Success(new
        {
            PrefabId = prefab.Id,
            Dependencies = dependencies,
            ComponentCount = prefab.Components.Count,
            NestedPrefabs = prefab.Children.Count
        });
    }
}
```

### 6.5 Engine-Agnostic Design

The prefab YAML format is **inspired by Stride but not bound to it**:

- **No Stride library dependencies** in the schema
- **Component types are strings**, not Stride classes
- **Asset references use our ID system**, not Stride's
- **Can be loaded by any engine** that implements the schema

This enables:
1. **Future engine migration** without asset conversion
2. **Server-side prefab validation** without Stride
3. **Cross-platform tools** for prefab editing
4. **NPC behavior integration** via component references

---

## 7. Client Integration

### 7.1 Asset Client SDK

The client SDK (generated from OpenAPI) plus asset-specific utilities:

```csharp
public interface IAssetClient
{
    // Metadata operations (via WebSocket/Dapr)
    Task<UploadResponse> RequestUploadAsync(UploadRequest request);
    Task<AssetMetadata> CompleteUploadAsync(CompleteUploadRequest request);
    Task<AssetWithDownloadUrl> GetAssetAsync(string assetId, string version = "latest");
    Task<List<VersionInfo>> ListVersionsAsync(string assetId);

    // Direct transfer operations (via pre-signed URLs)
    Task UploadFileAsync(UploadResponse uploadInfo, Stream fileStream,
                         IProgress<TransferProgress> progress = null);
    Task<Stream> DownloadFileAsync(AssetWithDownloadUrl assetInfo,
                                   IProgress<TransferProgress> progress = null);

    // Bundle operations
    Task<BundleInfo> GetBundleAsync(string bundleId);
    Task DownloadBundleAsync(BundleInfo bundle, string targetPath,
                             IProgress<TransferProgress> progress = null);
}

public class TransferProgress
{
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public double PercentComplete => (double)BytesTransferred / TotalBytes * 100;
    public TimeSpan EstimatedTimeRemaining { get; set; }
}
```

### 7.2 Stride Integration

Custom asset loader for Stride engine:

```csharp
public class BannouAssetLoader : IAssetLoader
{
    private readonly IAssetClient _assetClient;
    private readonly IContentManager _contentManager;
    private readonly string _cacheDirectory;

    public async Task<T> LoadAssetAsync<T>(string assetId) where T : class
    {
        // Check local cache first
        var cachePath = GetCachePath(assetId);
        if (File.Exists(cachePath))
        {
            return await LoadFromCacheAsync<T>(cachePath);
        }

        // Get download URL from asset service
        var assetInfo = await _assetClient.GetAssetAsync(assetId);

        // Download to cache
        using var downloadStream = await _assetClient.DownloadFileAsync(assetInfo);
        await SaveToCacheAsync(cachePath, downloadStream);

        // Load via Stride's content system
        return await LoadFromCacheAsync<T>(cachePath);
    }

    public async Task PreloadBundleAsync(string bundleId)
    {
        var bundleInfo = await _assetClient.GetBundleAsync(bundleId);
        var bundlePath = GetBundleCachePath(bundleId);

        if (!File.Exists(bundlePath) || !ValidateBundle(bundlePath, bundleInfo))
        {
            await _assetClient.DownloadBundleAsync(bundleInfo, bundlePath);
        }

        // Register bundle with content manager
        RegisterBundle(bundlePath);
    }
}
```

### 7.3 Client Events Schema (Tenet 17 Compliance)

Per Tenet 17, all WebSocket-pushed events must have a defined schema in `schemas/asset-client-events.yaml`. These events are sent server→client via the WebSocket Event flag (0x10).

**Event Delivery**: Events are published to RabbitMQ channel `CONNECT_{sessionId}`, which the Connect service forwards to the client's WebSocket connection.

```yaml
# schemas/asset-client-events.yaml
# Client-facing events pushed via WebSocket (Event flag 0x10)

openapi: 3.0.3
info:
  title: Asset Client Events
  version: 1.0.0
  description: |
    WebSocket events pushed from Asset Service to connected clients.
    All events follow the pattern: Server publishes to RabbitMQ → Connect service
    forwards to client WebSocket with Event flag (0x10).

components:
  schemas:
    # ═══════════════════════════════════════════════════════════════
    # Upload Events
    # ═══════════════════════════════════════════════════════════════

    AssetUploadCompleteEvent:
      type: object
      required:
        - event_type
        - event_id
        - timestamp
        - upload_id
        - success
      properties:
        event_type:
          type: string
          const: "asset.upload.complete"
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        upload_id:
          type: string
          format: uuid
          description: "Correlates with the upload request"
        success:
          type: boolean
        asset_id:
          type: string
          description: "Set on success, null on failure"
        content_hash:
          type: string
          description: "SHA256 hash of uploaded content"
        size:
          type: integer
          format: int64
        error_code:
          type: string
          enum:
            - VALIDATION_FAILED
            - HASH_MISMATCH
            - SIZE_EXCEEDED
            - STORAGE_ERROR
            - TIMEOUT
          description: "Set on failure"
        error_message:
          type: string
          description: "Human-readable error description"

    # ═══════════════════════════════════════════════════════════════
    # Processing Events
    # ═══════════════════════════════════════════════════════════════

    AssetProcessingCompleteEvent:
      type: object
      required:
        - event_type
        - event_id
        - timestamp
        - asset_id
        - success
      properties:
        event_type:
          type: string
          const: "asset.processing.complete"
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        asset_id:
          type: string
        success:
          type: boolean
        processing_type:
          type: string
          enum:
            - mipmaps
            - lod_generation
            - transcode
            - compression
            - validation
            - behavior_compile
        outputs:
          type: array
          description: "Generated derivative assets (mipmaps, LODs, etc.)"
          items:
            type: object
            properties:
              output_type:
                type: string
              asset_id:
                type: string
              size:
                type: integer
                format: int64
        error_code:
          type: string
          enum:
            - PROCESSING_FAILED
            - INVALID_FORMAT
            - RESOURCE_EXHAUSTED
            - TIMEOUT
            - PROCESSOR_UNAVAILABLE
        error_message:
          type: string

    AssetProcessingFailedEvent:
      type: object
      required:
        - event_type
        - event_id
        - timestamp
        - asset_id
        - error_code
      properties:
        event_type:
          type: string
          const: "asset.processing.failed"
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        asset_id:
          type: string
        error_code:
          type: string
          enum:
            - PROCESSING_FAILED
            - INVALID_FORMAT
            - RESOURCE_EXHAUSTED
            - TIMEOUT
            - PROCESSOR_UNAVAILABLE
        error_message:
          type: string
        retry_available:
          type: boolean
          default: true
        retry_after_ms:
          type: integer
          description: "Suggested retry delay in milliseconds"

    # ═══════════════════════════════════════════════════════════════
    # Bundle Events
    # ═══════════════════════════════════════════════════════════════

    BundleValidationCompleteEvent:
      type: object
      required:
        - event_type
        - event_id
        - timestamp
        - upload_id
        - success
      properties:
        event_type:
          type: string
          const: "bundle.validation.complete"
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        upload_id:
          type: string
          format: uuid
        success:
          type: boolean
        bundle_id:
          type: string
          description: "Assigned bundle ID on success"
        assets_registered:
          type: integer
          description: "Number of assets extracted and registered"
        duplicates_skipped:
          type: integer
          description: "Assets with matching hash already in storage"
        warnings:
          type: array
          items:
            type: object
            properties:
              code:
                type: string
              message:
                type: string
              asset_path:
                type: string

    BundleValidationFailedEvent:
      type: object
      required:
        - event_type
        - event_id
        - timestamp
        - upload_id
        - errors
      properties:
        event_type:
          type: string
          const: "bundle.validation.failed"
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        upload_id:
          type: string
          format: uuid
        errors:
          type: array
          items:
            type: object
            required:
              - code
              - message
            properties:
              code:
                type: string
                enum:
                  - INVALID_ARCHIVE
                  - MISSING_MANIFEST
                  - INVALID_MANIFEST
                  - HASH_MISMATCH
                  - PATH_TRAVERSAL
                  - SIZE_EXCEEDED
                  - DUPLICATE_ASSET_ID
                  - MISSING_DEPENDENCY
              message:
                type: string
              asset_path:
                type: string
                description: "Path within bundle where error occurred"

    # ═══════════════════════════════════════════════════════════════
    # Asset Ready Event (final notification)
    # ═══════════════════════════════════════════════════════════════

    AssetReadyEvent:
      type: object
      required:
        - event_type
        - event_id
        - timestamp
        - asset_id
      properties:
        event_type:
          type: string
          const: "asset.ready"
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        asset_id:
          type: string
        version_id:
          type: string
        content_hash:
          type: string
        size:
          type: integer
          format: int64
        content_type:
          type: string
        metadata:
          type: object
          additionalProperties: true
        derivatives:
          type: array
          description: "Processed outputs (mipmaps, LODs, etc.)"
          items:
            type: object
            properties:
              type:
                type: string
              asset_id:
                type: string
              size:
                type: integer
```

#### Event Flow Summary

| Event | When Sent | Contains |
|-------|-----------|----------|
| `asset.upload.complete` | After MinIO confirms upload | Upload ID, asset ID (if success), error (if failed) |
| `asset.processing.complete` | After processing finishes | Asset ID, processing outputs |
| `asset.processing.failed` | If processing fails | Error code, retry suggestion |
| `bundle.validation.complete` | After bundle validation | Bundle ID, asset counts |
| `bundle.validation.failed` | If bundle validation fails | Detailed error list |
| `asset.ready` | Final notification after all processing | Complete asset metadata |

#### Publishing Events (Service Implementation)

```csharp
// Client event publishing pattern for asset events
public interface IAssetEventEmitter
{
    Task EmitUploadCompleteAsync(string sessionId, AssetUploadCompleteEvent evt);
    Task EmitProcessingCompleteAsync(string sessionId, AssetProcessingCompleteEvent evt);
    Task EmitProcessingFailedAsync(string sessionId, AssetProcessingFailedEvent evt);
    Task EmitBundleValidationCompleteAsync(string sessionId, BundleValidationCompleteEvent evt);
    Task EmitBundleValidationFailedAsync(string sessionId, BundleValidationFailedEvent evt);
    Task EmitAssetReadyAsync(string sessionId, AssetReadyEvent evt);
}

// Implementation publishes to Connect service's channel
public class AssetEventEmitter : IAssetEventEmitter
{
    private readonly DaprClient _daprClient;

    public async Task EmitUploadCompleteAsync(string sessionId, AssetUploadCompleteEvent evt)
    {
        // Connect service listens on this channel and forwards to client WebSocket
        await _daprClient.PublishEventAsync(
            "bannou-pubsub",
            $"CONNECT_{sessionId}",
            evt);
    }
}
```

---

## 8. Implementation Roadmap

> **Implementation Guide**: Each task includes specific files, commands, and acceptance criteria. A developer unfamiliar with the codebase should be able to pick up any task and execute it.

---

### Phase 1: Infrastructure Setup

**Goal**: MinIO running locally, basic schema in place, lib-asset plugin skeleton created.

**Prerequisites**: Working Bannou development environment (`make all` passes).

#### Task 1.1: Add MinIO to Docker Compose

**What**: Add MinIO container to development Docker Compose configuration.

**Steps**:
1. Edit `docker-compose.yml` - add MinIO service (see Appendix A in this document)
2. Add `minio-init` service for bucket creation and webhook setup
3. Add `minio-data` volume to `volumes:` section
4. Create `.env` entries: `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`

**Files to modify**:
- `docker-compose.yml`
- `.env.example`

**Acceptance criteria**:
- [ ] `docker compose up minio` starts without errors
- [ ] MinIO console accessible at `http://localhost:9001`
- [ ] `bannou-assets` bucket exists after `minio-init` runs

---

#### Task 1.2: Create Asset Service OpenAPI Schema

**What**: Create `schemas/asset-api.yaml` based on the schema in Section 2.4 of this document.

**Steps**:
1. Create file `schemas/asset-api.yaml`
2. Copy OpenAPI schema from Section 2.4 (POST-only endpoints)
3. Verify YAML syntax: `python3 -c "import yaml; yaml.safe_load(open('schemas/asset-api.yaml'))"`

**Files to create**:
- `schemas/asset-api.yaml`

**Acceptance criteria**:
- [ ] Schema parses without YAML errors
- [ ] `servers.url` uses `http://localhost:3500/v1.0/invoke/bannou/method`

---

#### Task 1.3: Create Asset Client Events Schema

**What**: Create `schemas/asset-client-events.yaml` for WebSocket push events.

**Steps**:
1. Create file `schemas/asset-client-events.yaml`
2. Copy client events schema from Section 7.3 of this document
3. Verify YAML syntax

**Files to create**:
- `schemas/asset-client-events.yaml`

**Acceptance criteria**:
- [ ] Schema parses without errors
- [ ] Contains all 6 event types from Section 7.3

---

#### Task 1.4: Generate Asset Service Plugin Skeleton

**What**: Run generation scripts to create lib-asset plugin structure.

**Steps**:
1. Add `asset` to the service list in `scripts/generate-all-services.sh`
2. Run: `./scripts/generate-all-services.sh`
3. Verify generated files in `lib-asset/Generated/`

**Files to modify**:
- `scripts/generate-all-services.sh`

**Files generated**:
- `lib-asset/Generated/AssetController.Generated.cs`
- `lib-asset/Generated/IAssetService.cs`
- `lib-asset/Generated/AssetModels.cs`
- `lib-asset/lib-asset.csproj`

**Acceptance criteria**:
- [ ] `lib-asset/` directory created with Generated/ subdirectory
- [ ] `dotnet build` succeeds (with stub NotImplementedException methods)

---

#### Task 1.5: Create AssetService Implementation Class

**What**: Create the business logic implementation file for Asset Service.

**Steps**:
1. Create `lib-asset/AssetService.cs`
2. Implement `IAssetService` interface (generated)
3. Add `[DaprService]` attribute
4. Inject dependencies: `DaprClient`, `ILogger`, `IAssetStorageProvider`
5. Implement all methods with `throw new NotImplementedException()` initially

**Reference**: Use `lib-auth/AuthService.cs` as a pattern.

**Files to create**:
- `lib-asset/AssetService.cs`

**Acceptance criteria**:
- [ ] Class compiles without errors
- [ ] All interface methods implemented (even if throwing NotImplementedException)
- [ ] `[DaprService("asset", typeof(IAssetService))]` attribute present

---

#### Task 1.6: Create IAssetStorageProvider Interface

**What**: Create the storage abstraction interface in `bannou-service/` (shared).

**Steps**:
1. Create `bannou-service/Storage/IAssetStorageProvider.cs`
2. Copy interface definition from Section 2.1 of this document
3. Create result types: `PreSignedUploadResult`, `PreSignedDownloadResult`, etc.

**Files to create**:
- `bannou-service/Storage/IAssetStorageProvider.cs`
- `bannou-service/Storage/StorageModels.cs`

**Acceptance criteria**:
- [ ] Interface compiles
- [ ] All result types defined

---

#### Task 1.7: Create MinIO Storage Provider Implementation

**What**: Implement `IAssetStorageProvider` using MinIO SDK.

**Steps**:
1. Add NuGet package: `dotnet add lib-asset package Minio`
2. Create `lib-asset/Storage/MinioStorageProvider.cs`
3. Implement all interface methods using MinIO SDK
4. Add configuration class: `MinioStorageOptions`

**Reference**: [MinIO .NET SDK Documentation](https://min.io/docs/minio/linux/developers/dotnet/API.html)

**Files to create**:
- `lib-asset/Storage/MinioStorageProvider.cs`
- `lib-asset/Storage/MinioStorageOptions.cs`

**Acceptance criteria**:
- [ ] `GenerateUploadUrlAsync` returns valid pre-signed URL
- [ ] `GenerateDownloadUrlAsync` returns valid pre-signed URL
- [ ] Unit tests for URL generation pass

---

### Phase 2: Core Upload/Download Flow

**Goal**: End-to-end upload and download via pre-signed URLs.

**Prerequisites**: Phase 1 complete, MinIO running.

#### Task 2.1: Implement requestUpload Endpoint

**What**: Generate upload URLs and store upload state in Redis.

**Steps**:
1. In `AssetService.RequestUploadAsync()`:
   - Validate request (filename, size, content_type)
   - Generate upload token (UUID)
   - Store upload state in Redis: `asset:upload:{token}`
   - Call `IAssetStorageProvider.GenerateUploadUrlAsync()`
   - Return `UploadResponse` with URL and token

**Files to modify**:
- `lib-asset/AssetService.cs`

**Acceptance criteria**:
- [ ] Returns valid MinIO pre-signed upload URL
- [ ] Upload token stored in Redis with 1-hour TTL
- [ ] HTTP test: POST `/assets/upload/request` returns 200 with URL

---

#### Task 2.2: Implement completeUpload Endpoint

**What**: Validate upload completion and register asset.

**Steps**:
1. In `AssetService.CompleteUploadAsync()`:
   - Validate upload_id exists in Redis
   - Verify file exists in MinIO temp location
   - Calculate SHA256 content hash
   - Move from `temp/` to `assets/` bucket prefix
   - Store asset metadata in Redis state store
   - Publish `AssetUploadCompleteEvent` to session channel
   - Return `AssetMetadata`

**Files to modify**:
- `lib-asset/AssetService.cs`

**Acceptance criteria**:
- [ ] Asset moved from temp to permanent storage
- [ ] Asset metadata retrievable by asset_id
- [ ] Client receives `asset.upload.complete` WebSocket event

---

#### Task 2.3: Implement getAsset Endpoint (POST /assets/get)

**What**: Return asset metadata with pre-signed download URL.

**Steps**:
1. In `AssetService.GetAssetAsync()`:
   - Look up asset by asset_id in state store
   - If version specified, resolve version_id
   - Generate download URL via `IAssetStorageProvider`
   - Return `AssetWithDownloadUrl`

**Files to modify**:
- `lib-asset/AssetService.cs`

**Acceptance criteria**:
- [ ] Returns valid pre-signed download URL
- [ ] URL expires after 1 hour
- [ ] HTTP test: POST `/assets/get` with valid asset_id returns 200

---

#### Task 2.4: Add MinIO Webhook Handler

**What**: Handle MinIO upload completion webhooks.

**Steps**:
1. Create webhook endpoint: `/webhooks/minio`
2. Parse MinIO notification payload
3. Trigger asset processing pipeline
4. Publish completion event

**Reference**: [MinIO Bucket Notifications](https://min.io/docs/minio/linux/administration/monitoring/bucket-notifications.html)

**Files to create**:
- `lib-asset/Webhooks/MinioWebhookController.cs`

**Acceptance criteria**:
- [ ] Webhook receives PUT notifications from MinIO
- [ ] Triggers processing for uploaded files

---

#### Task 2.5: Create HTTP Integration Tests

**What**: Add Asset Service to HTTP test suite.

**Steps**:
1. Add test file: `http-tester/Tests/AssetTests.cs`
2. Test upload request → upload → complete → download flow
3. Test error cases: invalid upload_id, expired token, etc.

**Files to create**:
- `http-tester/Tests/AssetTests.cs`

**Acceptance criteria**:
- [ ] `make test-http` includes Asset tests
- [ ] Upload/download cycle passes

---

### Phase 3: NGINX Security Gateway

**Goal**: All asset transfers go through NGINX with Redis token validation.

**Prerequisites**: Phase 2 complete.

#### Task 3.1: Create NGINX Configuration

**What**: Add NGINX config for asset upload/download with OpenResty Lua.

**Steps**:
1. Create `provisioning/nginx/conf.d/asset-gateway.conf`
2. Copy NGINX config from Section 2.3
3. Add rate limiting zones

**Files to create**:
- `provisioning/nginx/conf.d/asset-gateway.conf`

**Acceptance criteria**:
- [ ] NGINX config syntax valid: `nginx -t`

---

#### Task 3.2: Create Lua Token Validation Script

**What**: OpenResty Lua script for Redis token validation.

**Steps**:
1. Create `provisioning/nginx/lua/validate_asset_token.lua`
2. Copy Lua script from Section 2.3
3. Configure Redis connection

**Files to create**:
- `provisioning/nginx/lua/validate_asset_token.lua`

**Acceptance criteria**:
- [ ] Lua script loads without errors
- [ ] Rejects requests without valid token
- [ ] Rejects requests with expired token
- [ ] Rejects requests with wrong session_id

---

#### Task 3.3: Update Docker Compose for NGINX+OpenResty

**What**: Replace plain NGINX with OpenResty for Lua support.

**Steps**:
1. Change NGINX image to `openresty/openresty:alpine`
2. Mount Lua scripts directory
3. Mount asset-gateway.conf

**Files to modify**:
- `docker-compose.yml`

**Acceptance criteria**:
- [ ] OpenResty container starts
- [ ] Asset upload through NGINX works

---

### Phase 4: Asset Processing Pool

**Goal**: Heavy processing offloaded to dedicated instances.

**Prerequisites**: Phase 3 complete.

#### Task 4.1: Add Processing Pool Endpoints to Orchestrator

**What**: Add pool management endpoints to Orchestrator service.

**Steps**:
1. Update `schemas/orchestrator-api.yaml` with endpoints from Section 2.5
2. Regenerate Orchestrator service
3. Implement `AcquireProcessor`, `GetPoolStatus`, `ScalePool`, `CleanupPool`

**Files to modify**:
- `schemas/orchestrator-api.yaml`
- `lib-orchestrator/OrchestratorService.cs`

**Acceptance criteria**:
- [ ] `/orchestrator/processing-pool/acquire` returns available app-id
- [ ] Returns 429 when all processors busy

---

#### Task 4.2: Create Processing Pool Preset

**What**: Orchestrator preset for asset processing deployment.

**Steps**:
1. Create `provisioning/orchestrator/presets/asset-processing.yaml`
2. Configure min/max instances, scale thresholds
3. Test deployment with preset

**Files to create**:
- `provisioning/orchestrator/presets/asset-processing.yaml`

**Acceptance criteria**:
- [ ] Preset deploys processing instances
- [ ] Instances register with Orchestrator

---

#### Task 4.3: Implement Processing Delegation in AssetService

**What**: Route large files to processing pool.

**Steps**:
1. Add `IOrchestratorClient` dependency to AssetService
2. In processing logic: check file size vs threshold
3. If large: call Orchestrator to acquire processor, then Dapr invoke to that app-id
4. If small: process locally

**Files to modify**:
- `lib-asset/AssetService.cs`

**Acceptance criteria**:
- [ ] Files > 50MB routed to processing pool
- [ ] Files < 50MB processed in-service

---

### Phase 5: Bundle System

**Goal**: .bannou bundle format, ZIP conversion with caching.

**Prerequisites**: Phase 4 complete.

#### Task 5.1: Implement .bannou Bundle Writer

**What**: Create bundle files with manifest, index, and assets.

**Steps**:
1. Create `lib-asset/Bundles/BannouBundleWriter.cs`
2. Implement manifest.json generation
3. Implement binary index format (Section 5.3)
4. Implement LZ4 compression for asset chunks

**Files to create**:
- `lib-asset/Bundles/BannouBundleWriter.cs`
- `lib-asset/Bundles/BannouBundleReader.cs`

**Acceptance criteria**:
- [ ] Can create .bannou file from list of assets
- [ ] .bannou file readable and extractable

---

#### Task 5.2: Implement ZIP ↔ .bannou Conversion

**What**: Convert between ZIP and native format with caching.

**Steps**:
1. Create `lib-asset/Bundles/BundleConverter.cs`
2. Implement ZIP → .bannou conversion
3. Implement .bannou → ZIP conversion
4. Add conversion cache with 24-hour TTL

**Files to create**:
- `lib-asset/Bundles/BundleConverter.cs`

**Acceptance criteria**:
- [ ] ZIP uploads converted to .bannou
- [ ] ZIP downloads served from cache
- [ ] Cache entries expire after 24 hours

---

#### Task 5.3: Implement Bundle API Endpoints

**What**: Create/get/upload bundle endpoints.

**Steps**:
1. Implement `CreateBundleAsync` - create bundle from asset list
2. Implement `GetBundleAsync` - return manifest + download URL
3. Implement `RequestBundleUploadAsync` - upload pre-made bundle

**Files to modify**:
- `lib-asset/AssetService.cs`

**Acceptance criteria**:
- [ ] Can create bundle from existing assets
- [ ] Can upload pre-made bundle (ZIP or .bannou)
- [ ] Can download bundle in either format

---

### Phase 6: Client Events and Notifications

**Goal**: All async operations notify clients via WebSocket.

**Prerequisites**: Phase 5 complete.

#### Task 6.1: Create IAssetEventEmitter Interface

**What**: Event emitter for client notifications.

**Steps**:
1. Create `lib-asset/Events/IAssetEventEmitter.cs`
2. Create implementation that publishes to `CONNECT_{sessionId}`
3. Register in DI

**Files to create**:
- `lib-asset/Events/IAssetEventEmitter.cs`
- `lib-asset/Events/AssetEventEmitter.cs`

**Acceptance criteria**:
- [ ] Events published to correct RabbitMQ channel
- [ ] Connect service forwards to client WebSocket

---

#### Task 6.2: Add Events to All Async Operations

**What**: Emit events at completion of all async operations.

**Steps**:
1. Add event emission to `CompleteUploadAsync`
2. Add event emission to processing completion
3. Add event emission to bundle validation
4. Add `AssetReadyEvent` as final notification

**Files to modify**:
- `lib-asset/AssetService.cs`

**Acceptance criteria**:
- [ ] Every async operation emits completion event
- [ ] Edge tests verify event reception

---

#### Task 6.3: Create Edge Tests for Asset Events

**What**: WebSocket tests for asset event flow.

**Steps**:
1. Add `edge-tester/Tests/AssetEventTests.cs`
2. Test upload → event flow
3. Test processing → event flow
4. Test error event delivery

**Files to create**:
- `edge-tester/Tests/AssetEventTests.cs`

**Acceptance criteria**:
- [ ] `make test-edge` includes Asset event tests
- [ ] All event types tested

---

### Phase 7: Production Readiness

**Goal**: Monitoring, documentation, performance validation.

**Prerequisites**: Phases 1-6 complete.

#### Task 7.1: Add Prometheus Metrics

**What**: Instrument Asset Service with metrics.

**Metrics to add**:
- `asset_uploads_total` - Counter of uploads
- `asset_downloads_total` - Counter of downloads
- `asset_processing_duration_seconds` - Histogram
- `asset_storage_bytes` - Gauge of total storage

**Files to modify**:
- `lib-asset/AssetService.cs`

---

#### Task 7.2: Add Health Checks

**What**: Health check endpoints for Asset Service dependencies.

**Checks**:
- MinIO connectivity
- Redis connectivity
- Processing pool availability

---

#### Task 7.3: Create Developer Documentation

**What**: Usage documentation in `docs/guides/`.

**Files to create**:
- `docs/guides/ASSET_SERVICE.md` - Service overview and API guide

---

#### Task 7.4: Performance Testing

**What**: Validate performance at target scale.

**Targets**:
- 100 concurrent uploads
- 1000 concurrent downloads
- 50MB average file size
- < 200ms latency for URL generation

---

### Definition of Done

Phase is complete when:
- [ ] All tasks have acceptance criteria met
- [ ] `make all` passes (unit + HTTP + edge tests)
- [ ] No new compilation warnings
- [ ] Code review approved

---

## 9. Open Questions

### Architecture Questions

1. ~~**WebSocket vs HTTP for small assets**~~: **RESOLVED** - Always use pre-signed URLs for all asset transfers regardless of size. This provides:
   - Consistent flow for all assets (no special cases)
   - Better tooling and CDN compatibility
   - Avoids service memory pressure from large data passing through Dapr/Connect

2. **Bundle format**: Should we use an existing format (ZIP, TAR) or our custom `.bannou` format?
   - **Pro existing**: Wide tooling support, familiar to developers
   - **Pro custom**: Optimized for our specific needs (streaming, indexing)
   - **User guidance**: No strong format preference, but zero corruption risk is mandatory

3. **Processing location**: Should asset processing happen in the Asset service or dedicated processing workers?
   - **Pro service**: Simpler architecture, fewer moving parts
   - **Pro workers**: Better scalability, isolation from API latency

### Storage Questions

4. ~~**MinIO vs cloud S3**~~: **RESOLVED** - Start with MinIO self-hosted, but design for swappable backends via `IAssetStorageProvider` abstraction (Section 2). This enables:
   - MinIO for development and self-hosted deployments
   - AWS S3 for production with CDN integration
   - Cloudflare R2 for cost-effective global distribution
   - Backend swapping via DI configuration without code changes

5. **Deduplication scope**: Should deduplication be per-asset-type, per-realm, or global?
   - Global: Maximum storage savings
   - Scoped: Simpler cleanup, isolated failures

### Client Questions

6. **Cache invalidation**: How should clients know when their cached assets are outdated?
   - Polling for updates?
   - WebSocket push notifications?
   - Version embedded in asset references?

7. **Offline support**: Should clients cache bundles for offline play?
   - What's the storage budget?
   - How to handle version mismatches?

### Security Questions

8. **Pre-signed URL duration**: What's the right expiration time?
   - Short (15min): More secure, requires refresh for large uploads
   - Long (24h): Better UX for slow connections, higher risk if leaked

9. **Upload validation**: How strictly should we validate uploaded assets?
   - File type validation? (magic bytes, not just extension)
   - Content scanning? (malware, inappropriate content)
   - Size limits per asset type?

---

## Appendix A: Docker Compose Configuration

```yaml
# Add to docker-compose.yml

services:
  minio:
    image: quay.io/minio/minio:RELEASE.2025-07-23T15-54-02Z
    container_name: bannou-minio
    ports:
      - "9000:9000"   # API
      - "9001:9001"   # Console
    environment:
      MINIO_ROOT_USER: ${MINIO_ROOT_USER:-minioadmin}
      MINIO_ROOT_PASSWORD: ${MINIO_ROOT_PASSWORD:-minioadmin}
      MINIO_NOTIFY_WEBHOOK_ENABLE_BANNOU: "on"
      MINIO_NOTIFY_WEBHOOK_ENDPOINT_BANNOU: "http://bannou:5000/webhooks/minio"
    command: server /data --console-address ":9001"
    volumes:
      - minio-data:/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 30s
      timeout: 10s
      retries: 3
    networks:
      - bannou-network

  minio-init:
    image: minio/mc
    depends_on:
      minio:
        condition: service_healthy
    entrypoint: >
      /bin/sh -c "
      mc alias set local http://minio:9000 minioadmin minioadmin;
      mc mb --ignore-existing local/bannou-assets;
      mc version enable local/bannou-assets;
      mc event add local/bannou-assets arn:minio:sqs::bannou:webhook --event put;
      exit 0;
      "
    networks:
      - bannou-network

volumes:
  minio-data:
```

## Appendix B: Event Schemas

```yaml
# schemas/asset-events.yaml

AssetUploadRequestedEvent:
  type: object
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    upload_id:
      type: string
      format: uuid
    session_id:
      type: string
    filename:
      type: string
    size:
      type: integer
      format: int64
    content_type:
      type: string

AssetUploadCompletedEvent:
  type: object
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    asset_id:
      type: string
    upload_id:
      type: string
      format: uuid
    session_id:
      type: string
    bucket:
      type: string
    key:
      type: string
    size:
      type: integer
      format: int64
    content_hash:
      type: string

AssetProcessingCompletedEvent:
  type: object
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    asset_id:
      type: string
    processing_type:
      type: string
      enum: [mipmaps, lod, transcode, compile, validate]
    success:
      type: boolean
    error_message:
      type: string
    outputs:
      type: array
      items:
        type: object
        properties:
          key:
            type: string
          size:
            type: integer
```

## References

### Engine Documentation
- [Unity AssetBundle Manual](https://docs.unity3d.com/Manual/AssetBundlesIntro.html)
- [Unreal PAK Files](https://docs.unrealengine.com/en-US/SharingAndReleasing/Patching/)
- [Stride Asset System](https://doc.stride3d.net/latest/en/manual/engine/assets/)
- [Stride GitHub Issue #201 - Asset Bundles](https://github.com/stride3d/stride/issues/201)

### Infrastructure
- [MinIO Documentation](https://min.io/docs/)
- [Dapr Service Invocation](https://docs.dapr.io/developing-applications/building-blocks/service-invocation/)
- [tus.io Resumable Upload Protocol](https://tus.io/)

### Compression
- [LZ4 Compression](https://github.com/lz4/lz4)
- [Oodle Compression (RAD Game Tools)](https://www.radgametools.com/oodle.htm)
