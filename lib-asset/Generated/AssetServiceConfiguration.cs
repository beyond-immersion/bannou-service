using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Configuration class for Asset service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(AssetService))]
public class AssetServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Storage backend type (minio, s3, r2, azure, filesystem)
    /// Environment variable: ASSET_STORAGE_PROVIDER
    /// </summary>
    public string StorageProvider { get; set; } = "minio";

    /// <summary>
    /// Primary bucket/container name for assets
    /// Environment variable: ASSET_STORAGE_BUCKET
    /// </summary>
    public string StorageBucket { get; set; } = "bannou-assets";

    /// <summary>
    /// Storage endpoint URL (MinIO/S3 compatible)
    /// Environment variable: ASSET_STORAGE_ENDPOINT
    /// </summary>
    public string StorageEndpoint { get; set; } = "http://minio:9000";

    /// <summary>
    /// Storage access key/username
    /// Environment variable: ASSET_STORAGE_ACCESS_KEY
    /// </summary>
    public string StorageAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Storage secret key/password
    /// Environment variable: ASSET_STORAGE_SECRET_KEY
    /// </summary>
    public string StorageSecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Storage region (for S3/R2)
    /// Environment variable: ASSET_STORAGE_REGION
    /// </summary>
    public string StorageRegion { get; set; } = "us-east-1";

    /// <summary>
    /// Force path-style URLs (required for MinIO)
    /// Environment variable: ASSET_STORAGE_FORCE_PATH_STYLE
    /// </summary>
    public bool StorageForcePathStyle { get; set; } = true;

    /// <summary>
    /// Use SSL/TLS for storage connections
    /// Environment variable: ASSET_STORAGE_USE_SSL
    /// </summary>
    public bool StorageUseSsl { get; set; } = false;

    /// <summary>
    /// TTL for pre-signed upload/download URLs in seconds
    /// Environment variable: ASSET_TOKEN_TTL_SECONDS
    /// </summary>
    public int TokenTtlSeconds { get; set; } = 3600;

    /// <summary>
    /// TTL for download URLs (can be shorter than upload)
    /// Environment variable: ASSET_DOWNLOAD_TOKEN_TTL_SECONDS
    /// </summary>
    public int DownloadTokenTtlSeconds { get; set; } = 900;

    /// <summary>
    /// Maximum upload size in megabytes
    /// Environment variable: ASSET_MAX_UPLOAD_SIZE_MB
    /// </summary>
    public int MaxUploadSizeMb { get; set; } = 500;

    /// <summary>
    /// File size threshold for multipart uploads in megabytes
    /// Environment variable: ASSET_MULTIPART_THRESHOLD_MB
    /// </summary>
    public int MultipartThresholdMb { get; set; } = 50;

    /// <summary>
    /// Size of each part in multipart uploads in megabytes
    /// Environment variable: ASSET_MULTIPART_PART_SIZE_MB
    /// </summary>
    public int MultipartPartSizeMb { get; set; } = 16;

    /// <summary>
    /// File size threshold for delegating to processing pool
    /// Environment variable: ASSET_LARGE_FILE_THRESHOLD_MB
    /// </summary>
    public int LargeFileThresholdMb { get; set; } = 50;

    /// <summary>
    /// Processing pool identifier for orchestrator
    /// Environment variable: ASSET_PROCESSING_POOL_TYPE
    /// </summary>
    public string ProcessingPoolType { get; set; } = "asset-processor";

    /// <summary>
    /// Service mode (api, worker, both)
    /// Environment variable: ASSET_PROCESSING_MODE
    /// </summary>
    public string ProcessingMode { get; set; } = "both";

    /// <summary>
    /// Worker pool identifier when running in worker mode
    /// Environment variable: ASSET_WORKER_POOL
    /// </summary>
    public string WorkerPool { get; set; } = string.Empty;

    /// <summary>
    /// Default compression for bundles (lz4, lzma, none)
    /// Environment variable: ASSET_BUNDLE_COMPRESSION_DEFAULT
    /// </summary>
    public string BundleCompressionDefault { get; set; } = "lz4";

    /// <summary>
    /// TTL for cached ZIP conversions in hours
    /// Environment variable: ASSET_ZIP_CACHE_TTL_HOURS
    /// </summary>
    public int ZipCacheTtlHours { get; set; } = 24;

    /// <summary>
    /// Secret for validating MinIO webhook requests
    /// Environment variable: ASSET_MINIO_WEBHOOK_SECRET
    /// </summary>
    public string MinioWebhookSecret { get; set; } = string.Empty;

}
