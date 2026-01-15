using BeyondImmersion.Bannou.AssetLoader.Abstractions;

namespace BeyondImmersion.Bannou.AssetLoader.Sources;

/// <summary>
/// Simple IAssetSource implementation for direct HTTP URLs.
/// Use when you have pre-known URLs (e.g., from a CDN manifest, offline mode).
/// Does not support bundle resolution - only direct bundle/asset access.
/// </summary>
public sealed class HttpAssetSource : IAssetSource
{
    private readonly Dictionary<string, BundleDownloadInfo> _bundleInfos = new();
    private readonly Dictionary<string, AssetDownloadInfo> _assetInfos = new();

    /// <inheritdoc />
    public bool RequiresAuthentication => false;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>
    /// Registers a bundle with its download URL.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="downloadUrl">Direct download URL.</param>
    /// <param name="sizeBytes">Bundle size in bytes.</param>
    /// <param name="assetIds">Asset IDs contained in the bundle.</param>
    /// <param name="contentHash">Optional content hash for verification.</param>
    /// <param name="expiresAt">When the URL expires (default: 1 year from now).</param>
    public void RegisterBundle(
        string bundleId,
        Uri downloadUrl,
        long sizeBytes,
        IReadOnlyList<string> assetIds,
        string? contentHash = null,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentNullException.ThrowIfNull(downloadUrl);
        ArgumentNullException.ThrowIfNull(assetIds);

        _bundleInfos[bundleId] = new BundleDownloadInfo
        {
            BundleId = bundleId,
            DownloadUrl = downloadUrl,
            SizeBytes = sizeBytes,
            AssetIds = assetIds,
            ContentHash = contentHash,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddYears(1)
        };
    }

    /// <summary>
    /// Registers a standalone asset with its download URL.
    /// </summary>
    /// <param name="assetId">Asset identifier.</param>
    /// <param name="downloadUrl">Direct download URL.</param>
    /// <param name="sizeBytes">Asset size in bytes.</param>
    /// <param name="contentType">MIME type of the asset.</param>
    /// <param name="contentHash">Optional content hash for verification.</param>
    /// <param name="expiresAt">When the URL expires (default: 1 year from now).</param>
    public void RegisterAsset(
        string assetId,
        Uri downloadUrl,
        long sizeBytes,
        string contentType,
        string? contentHash = null,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        ArgumentNullException.ThrowIfNull(downloadUrl);
        ArgumentException.ThrowIfNullOrEmpty(contentType);

        _assetInfos[assetId] = new AssetDownloadInfo
        {
            AssetId = assetId,
            DownloadUrl = downloadUrl,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            ContentHash = contentHash,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddYears(1)
        };
    }

    /// <inheritdoc />
    public async Task<BundleResolutionResult> ResolveBundlesAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<string>? excludeBundleIds = null,
        CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous in-memory lookup - placeholder for future async implementation

        var excludeSet = excludeBundleIds?.ToHashSet() ?? new HashSet<string>();
        var resolvedBundles = new List<ResolvedBundleInfo>();
        var standaloneAssets = new List<ResolvedAssetInfo>();
        var unresolvedAssets = new List<string>();
        var resolvedAssetIds = new HashSet<string>();

        // First, check bundles for the requested assets
        foreach (var (bundleId, bundleInfo) in _bundleInfos)
        {
            if (excludeSet.Contains(bundleId))
                continue;

            var matchingAssets = assetIds
                .Where(id => bundleInfo.AssetIds.Contains(id))
                .Where(id => !resolvedAssetIds.Contains(id))
                .ToList();

            if (matchingAssets.Count > 0)
            {
                resolvedBundles.Add(new ResolvedBundleInfo
                {
                    BundleId = bundleId,
                    DownloadUrl = bundleInfo.DownloadUrl,
                    SizeBytes = bundleInfo.SizeBytes,
                    ExpiresAt = bundleInfo.ExpiresAt,
                    IncludedAssetIds = matchingAssets
                });

                foreach (var assetId in matchingAssets)
                {
                    resolvedAssetIds.Add(assetId);
                }
            }
        }

        // Then, check for standalone assets
        foreach (var assetId in assetIds)
        {
            if (resolvedAssetIds.Contains(assetId))
                continue;

            if (_assetInfos.TryGetValue(assetId, out var assetInfo))
            {
                standaloneAssets.Add(new ResolvedAssetInfo
                {
                    AssetId = assetId,
                    DownloadUrl = assetInfo.DownloadUrl,
                    SizeBytes = assetInfo.SizeBytes,
                    ExpiresAt = assetInfo.ExpiresAt,
                    ContentType = assetInfo.ContentType
                });
                resolvedAssetIds.Add(assetId);
            }
            else
            {
                unresolvedAssets.Add(assetId);
            }
        }

        return new BundleResolutionResult
        {
            Bundles = resolvedBundles,
            StandaloneAssets = standaloneAssets,
            UnresolvedAssetIds = unresolvedAssets.Count > 0 ? unresolvedAssets : null
        };
    }

    /// <inheritdoc />
    public async Task<BundleDownloadInfo?> GetBundleDownloadInfoAsync(string bundleId, CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous in-memory lookup - placeholder for future async implementation

        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        return _bundleInfos.TryGetValue(bundleId, out var info) ? info : null;
    }

    /// <inheritdoc />
    public async Task<AssetDownloadInfo?> GetAssetDownloadInfoAsync(string assetId, CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous in-memory lookup - placeholder for future async implementation

        ArgumentException.ThrowIfNullOrEmpty(assetId);
        return _assetInfos.TryGetValue(assetId, out var info) ? info : null;
    }
}
