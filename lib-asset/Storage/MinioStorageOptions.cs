namespace BeyondImmersion.BannouService.Asset.Storage;

/// <summary>
/// Configuration options for the MinIO storage provider.
/// </summary>
public class MinioStorageOptions
{
    /// <summary>
    /// MinIO server endpoint (e.g., "minio:9000" or "s3.amazonaws.com").
    /// </summary>
    public string Endpoint { get; set; } = "minio:9000";

    /// <summary>
    /// Access key (username) for authentication.
    /// </summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Secret key (password) for authentication.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

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
    /// </summary>
    public TimeSpan DefaultUrlExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Minimum part size for multipart uploads (5MB).
    /// </summary>
    public long MinPartSize { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum part size for multipart uploads (5GB).
    /// </summary>
    public long MaxPartSize { get; set; } = 5L * 1024 * 1024 * 1024;
}
