using BeyondImmersion.Bannou.AssetLoader.Download;

namespace BeyondImmersion.Bannou.AssetLoader;

/// <summary>
/// Configuration options for the AssetLoader.
/// </summary>
public sealed class AssetLoaderOptions
{
    /// <summary>
    /// Options for download operations.
    /// </summary>
    public DownloadOptions? DownloadOptions { get; init; }

    /// <summary>
    /// Whether to automatically load bundles into the registry after download.
    /// Default: true
    /// </summary>
    public bool AutoRegisterBundles { get; init; } = true;

    /// <summary>
    /// Whether to validate bundle integrity after download.
    /// When enabled, verifies each asset's content hash matches the manifest declaration.
    /// Default: true
    /// </summary>
    /// <remarks>
    /// Validation reads all assets to compute hashes, which adds overhead proportional
    /// to bundle size. For trusted sources or performance-critical scenarios, consider
    /// disabling validation and relying on transport-level integrity (HTTPS) instead.
    /// </remarks>
    public bool ValidateBundles { get; init; } = true;

    /// <summary>
    /// Maximum number of concurrent bundle downloads.
    /// Default: 4
    /// </summary>
    public int MaxConcurrentDownloads { get; init; } = 4;

    /// <summary>
    /// Whether to prefer cached bundles over fresh downloads.
    /// Default: true
    /// </summary>
    public bool PreferCache { get; init; } = true;
}
