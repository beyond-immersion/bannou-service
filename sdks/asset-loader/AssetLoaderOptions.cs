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
    /// Default: true
    /// </summary>
    /// <remarks>
    /// TODO: This option is defined but not yet implemented. Bundle validation
    /// (hash verification, manifest integrity checks) needs to be added to LoadBundleInternalAsync.
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
