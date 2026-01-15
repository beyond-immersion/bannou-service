namespace BeyondImmersion.Bannou.AssetBundler.Abstractions;

/// <summary>
/// Discovers and creates asset sources from a root location.
/// Vendor-specific implementations (e.g., SyntyPackSourceProvider) scan
/// for their expected directory/file patterns.
/// </summary>
public interface IAssetSourceProvider
{
    /// <summary>
    /// Provider identifier (e.g., "synty", "unity-export", "raw").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Gets the type inferencer appropriate for this provider's assets.
    /// </summary>
    IAssetTypeInferencer TypeInferencer { get; }

    /// <summary>
    /// Discovers all asset sources in the given root directory.
    /// </summary>
    /// <param name="root">Root directory to scan.</param>
    /// <param name="options">Optional discovery options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Enumerable of discovered asset sources.</returns>
    IAsyncEnumerable<IAssetSource> DiscoverSourcesAsync(
        DirectoryInfo root,
        DiscoveryOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// Options for source discovery.
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>
    /// Filter to specific source IDs (null = all).
    /// </summary>
    public IReadOnlySet<string>? SourceIdFilter { get; init; }

    /// <summary>
    /// Include sources that appear unchanged since last processing.
    /// </summary>
    public bool IncludeUnchanged { get; init; }

    /// <summary>
    /// Maximum sources to discover (0 = unlimited).
    /// </summary>
    public int MaxSources { get; init; }
}
