namespace BeyondImmersion.Bannou.AssetLoader.Client;

/// <summary>
/// Configuration options for the AssetManager.
/// </summary>
public sealed class AssetManagerOptions
{
    /// <summary>
    /// Directory to store cached bundles.
    /// Default: "./asset-cache"
    /// </summary>
    public string CacheDirectory { get; init; } = "./asset-cache";

    /// <summary>
    /// Default realm stub name or ID for bundle resolution.
    /// Default: "shared"
    /// </summary>
    public string Realm { get; init; } = "shared";

    /// <summary>
    /// Maximum number of concurrent bundle downloads.
    /// Default: 4
    /// </summary>
    public int MaxConcurrentDownloads { get; init; } = 4;

    /// <summary>
    /// Whether to validate bundle integrity after download using SHA256 hashes.
    /// Default: true
    /// </summary>
    /// <remarks>
    /// Validation reads all assets to compute hashes, which adds overhead proportional
    /// to bundle size. For trusted sources or performance-critical scenarios, consider
    /// disabling validation and relying on transport-level integrity (HTTPS) instead.
    /// </remarks>
    public bool ValidateBundles { get; init; } = true;

    /// <summary>
    /// Whether to prefer cached bundles over fresh downloads.
    /// When true, checks cache before downloading. When false, always downloads fresh.
    /// Default: true
    /// </summary>
    public bool PreferCache { get; init; } = true;

    /// <summary>
    /// Maximum cache size in bytes.
    /// Default: 1GB (1073741824 bytes)
    /// </summary>
    public long MaxCacheSizeBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Whether to enable file-based caching.
    /// When false, bundles are only kept in memory (lost on restart).
    /// Default: true
    /// </summary>
    public bool EnableCache { get; init; } = true;
}
