namespace BeyondImmersion.Bannou.AssetLoader.Abstractions;

/// <summary>
/// Abstraction for resolving asset and bundle download locations.
/// Implementations provide URL resolution via different mechanisms:
/// - HttpAssetSource: Direct URLs (no auth)
/// - BannouWebSocketAssetSource: Client SDK WebSocket (game clients, dev tools)
/// - BannouMeshAssetSource: Server SDK mesh (game servers, backend tools)
/// </summary>
public interface IAssetSource
{
    /// <summary>
    /// Resolves optimal bundles for the requested assets.
    /// Uses server-side bundle resolution to minimize download size.
    /// </summary>
    /// <param name="assetIds">Asset IDs to resolve.</param>
    /// <param name="excludeBundleIds">Already-loaded bundle IDs to exclude from resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolution result with bundles and standalone assets.</returns>
    Task<BundleResolutionResult> ResolveBundlesAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<string>? excludeBundleIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets download information for a specific bundle by ID.
    /// </summary>
    /// <param name="bundleId">Bundle ID to get download info for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Download info if bundle exists, null otherwise.</returns>
    Task<BundleDownloadInfo?> GetBundleDownloadInfoAsync(
        string bundleId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets download information for a standalone asset.
    /// Use for assets not in any bundle or when bundle download is not desired.
    /// </summary>
    /// <param name="assetId">Asset ID to get download info for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Download info if asset exists, null otherwise.</returns>
    Task<AssetDownloadInfo?> GetAssetDownloadInfoAsync(
        string assetId,
        CancellationToken ct = default);

    /// <summary>
    /// Whether this source requires authentication before use.
    /// WebSocket sources require auth; direct HTTP sources may not.
    /// </summary>
    bool RequiresAuthentication { get; }

    /// <summary>
    /// Whether this source is currently connected and available.
    /// </summary>
    bool IsAvailable { get; }
}

/// <summary>
/// Result of bundle resolution for a set of assets.
/// </summary>
public sealed class BundleResolutionResult
{
    /// <summary>
    /// Bundles that should be downloaded to obtain the requested assets.
    /// Ordered by priority (metabundles first, then by coverage).
    /// </summary>
    public required IReadOnlyList<ResolvedBundleInfo> Bundles { get; init; }

    /// <summary>
    /// Assets that must be downloaded individually (not in any bundle).
    /// </summary>
    public required IReadOnlyList<ResolvedAssetInfo> StandaloneAssets { get; init; }

    /// <summary>
    /// Asset IDs that could not be resolved (don't exist).
    /// </summary>
    public IReadOnlyList<string>? UnresolvedAssetIds { get; init; }

    /// <summary>
    /// Total size in bytes of all bundles and standalone assets.
    /// </summary>
    public long TotalSizeBytes => Bundles.Sum(b => b.SizeBytes) + StandaloneAssets.Sum(a => a.SizeBytes);
}

/// <summary>
/// Information about a resolved bundle for download.
/// </summary>
public sealed class ResolvedBundleInfo
{
    /// <summary>Bundle identifier.</summary>
    public required string BundleId { get; init; }

    /// <summary>Pre-signed download URL (may expire).</summary>
    public required Uri DownloadUrl { get; init; }

    /// <summary>Bundle size in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>When the download URL expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Asset IDs included in this bundle (from the requested set).</summary>
    public required IReadOnlyList<string> IncludedAssetIds { get; init; }

    /// <summary>Whether this is a metabundle (combined from multiple sources).</summary>
    public bool IsMetabundle { get; init; }
}

/// <summary>
/// Information about a resolved standalone asset for download.
/// </summary>
public sealed class ResolvedAssetInfo
{
    /// <summary>Asset identifier.</summary>
    public required string AssetId { get; init; }

    /// <summary>Pre-signed download URL (may expire).</summary>
    public required Uri DownloadUrl { get; init; }

    /// <summary>Asset size in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>When the download URL expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Content type (MIME type) of the asset.</summary>
    public required string ContentType { get; init; }
}

/// <summary>
/// Download information for a specific bundle.
/// </summary>
public sealed class BundleDownloadInfo
{
    /// <summary>Bundle identifier.</summary>
    public required string BundleId { get; init; }

    /// <summary>Pre-signed download URL.</summary>
    public required Uri DownloadUrl { get; init; }

    /// <summary>Bundle size in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>When the download URL expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>All asset IDs contained in this bundle.</summary>
    public required IReadOnlyList<string> AssetIds { get; init; }

    /// <summary>Content hash (SHA256) for integrity verification.</summary>
    public string? ContentHash { get; init; }
}

/// <summary>
/// Download information for a specific asset.
/// </summary>
public sealed class AssetDownloadInfo
{
    /// <summary>Asset identifier.</summary>
    public required string AssetId { get; init; }

    /// <summary>Pre-signed download URL.</summary>
    public required Uri DownloadUrl { get; init; }

    /// <summary>Asset size in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>When the download URL expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Content type (MIME type) of the asset.</summary>
    public required string ContentType { get; init; }

    /// <summary>Content hash (SHA256) for integrity verification.</summary>
    public string? ContentHash { get; init; }
}
