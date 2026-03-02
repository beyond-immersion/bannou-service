namespace BeyondImmersion.BannouService.Asset.Storage;

/// <summary>
/// Configuration options for the MinIO storage provider.
/// </summary>
public class MinioStorageOptions
{
    /// <summary>
    /// MinIO server endpoint for internal service connections (e.g., "minio:9000").
    /// </summary>
    public string Endpoint { get; set; } = "minio:9000";

    /// <summary>
    /// Public endpoint for pre-signed URLs accessible by clients.
    /// If null, uses Endpoint. Use for Docker/K8s where internal hostname differs from public access.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    /// <summary>
    /// Access key (username) for authentication.
    /// Null until configured via ASSET_STORAGE_ACCESS_KEY.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// Secret key (password) for authentication.
    /// Null until configured via ASSET_STORAGE_SECRET_KEY.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Whether to use SSL/TLS for connections.
    /// </summary>
    public bool UseSSL { get; set; } = false;

    /// <summary>
    /// Default bucket name for asset storage.
    /// </summary>
    public string DefaultBucket { get; set; } = "bannou-assets";

    /// <summary>
    /// Default region for bucket operations.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Default expiration time for pre-signed URLs.
    /// Wired from AssetServiceConfiguration.TokenTtlSeconds during service configuration.
    /// </summary>
    public TimeSpan DefaultUrlExpiration { get; set; }

    /// <summary>
    /// Minimum part size for multipart uploads (5MB).
    /// </summary>
    public long MinPartSize { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum part size for multipart uploads (5GB).
    /// </summary>
    public long MaxPartSize { get; set; } = 5L * 1024 * 1024 * 1024;
}
