using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.BannouService.Asset;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetLoader.Server;

/// <summary>
/// IAssetSource implementation using the generated AssetClient (mesh).
/// For game servers and backend services that use service-to-service mesh communication.
/// </summary>
public sealed class BannouMeshAssetSource : IAssetSource
{
    private readonly IAssetClient _assetClient;
    private readonly ILogger<BannouMeshAssetSource>? _logger;
    private readonly Realm _defaultRealm;

    /// <inheritdoc />
    public bool RequiresAuthentication => false; // Mesh handles auth via service identity

    /// <inheritdoc />
    public bool IsAvailable => true; // Mesh is always available if registered

    /// <summary>
    /// Creates an asset source using the generated AssetClient.
    /// </summary>
    /// <param name="assetClient">Injected AssetClient from DI.</param>
    /// <param name="defaultRealm">Default realm for bundle resolution.</param>
    /// <param name="logger">Optional logger.</param>
    public BannouMeshAssetSource(
        IAssetClient assetClient,
        Realm defaultRealm = Realm.Arcadia,
        ILogger<BannouMeshAssetSource>? logger = null)
    {
        _assetClient = assetClient ?? throw new ArgumentNullException(nameof(assetClient));
        _defaultRealm = defaultRealm;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BundleResolutionResult> ResolveBundlesAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<string>? excludeBundleIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        _logger?.LogDebug("Resolving bundles for {Count} assets via mesh", assetIds.Count);

        var request = new ResolveBundlesRequest
        {
            AssetIds = assetIds.ToList(),
            Realm = _defaultRealm,
            PreferMetabundles = true,
            IncludeStandalone = true
        };

        var response = await _assetClient.ResolveBundlesAsync(request, ct).ConfigureAwait(false);

        var bundles = response.Bundles.Select(b => new ResolvedBundleInfo
        {
            BundleId = b.BundleId,
            DownloadUrl = b.DownloadUrl,
            SizeBytes = b.Size,
            ExpiresAt = b.ExpiresAt,
            IncludedAssetIds = b.AssetIds?.ToList() ?? new List<string>(),
            IsMetabundle = b.BundleType == BundleType.Metabundle
        }).ToList();

        var standaloneAssets = response.StandaloneAssets.Select(a => new ResolvedAssetInfo
        {
            AssetId = a.AssetId,
            DownloadUrl = a.DownloadUrl ?? throw new InvalidOperationException($"Standalone asset {a.AssetId} missing download URL"),
            SizeBytes = a.Size,
            ExpiresAt = a.ExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            ContentType = a.ContentType ?? "application/octet-stream"
        }).ToList();

        _logger?.LogDebug("Resolved {BundleCount} bundles and {AssetCount} standalone assets",
            bundles.Count, standaloneAssets.Count);

        return new BundleResolutionResult
        {
            Bundles = bundles,
            StandaloneAssets = standaloneAssets,
            UnresolvedAssetIds = response.Unresolved?.ToList()
        };
    }

    /// <inheritdoc />
    public async Task<BundleDownloadInfo?> GetBundleDownloadInfoAsync(string bundleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        _logger?.LogDebug("Getting bundle download info for {BundleId} via mesh", bundleId);

        try
        {
            var request = new GetBundleRequest
            {
                BundleId = bundleId,
                Format = BundleFormat.Bannou
            };

            var response = await _assetClient.GetBundleAsync(request, ct).ConfigureAwait(false);

            return new BundleDownloadInfo
            {
                BundleId = response.BundleId,
                DownloadUrl = response.DownloadUrl,
                SizeBytes = response.Size,
                ExpiresAt = response.ExpiresAt,
                AssetIds = response.AssetIds?.ToList() ?? new List<string>()
            };
        }
        catch (BeyondImmersion.Bannou.Core.ApiException ex) when (ex.StatusCode == 404)
        {
            _logger?.LogDebug("Bundle {BundleId} not found", bundleId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<AssetDownloadInfo?> GetAssetDownloadInfoAsync(string assetId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        _logger?.LogDebug("Getting asset download info for {AssetId} via mesh", assetId);

        try
        {
            var request = new GetAssetRequest
            {
                AssetId = assetId,
                Version = "latest"
            };

            var response = await _assetClient.GetAssetAsync(request, ct).ConfigureAwait(false);

            if (response.DownloadUrl == null)
            {
                _logger?.LogWarning("Asset {AssetId} found but no download URL provided", assetId);
                return null;
            }

            return new AssetDownloadInfo
            {
                AssetId = response.AssetId,
                DownloadUrl = response.DownloadUrl,
                SizeBytes = response.Size,
                ExpiresAt = response.ExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
                ContentType = response.ContentType ?? "application/octet-stream",
                ContentHash = response.ContentHash
            };
        }
        catch (BeyondImmersion.Bannou.Core.ApiException ex) when (ex.StatusCode == 404)
        {
            _logger?.LogDebug("Asset {AssetId} not found", assetId);
            return null;
        }
    }
}
