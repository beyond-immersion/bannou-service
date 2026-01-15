namespace BeyondImmersion.Bannou.AssetLoader.Models;

/// <summary>
/// Result of loading a single asset.
/// </summary>
/// <typeparam name="T">Asset type.</typeparam>
public sealed class AssetLoadResult<T>
{
    /// <summary>Asset ID that was loaded.</summary>
    public required string AssetId { get; init; }

    /// <summary>Whether the load was successful.</summary>
    public required bool Success { get; init; }

    /// <summary>Loaded asset (null if failed).</summary>
    public T? Asset { get; init; }

    /// <summary>Error message if load failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Bundle the asset was loaded from.</summary>
    public string? BundleId { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static AssetLoadResult<T> Succeeded(string assetId, T asset, string bundleId)
        => new()
        {
            AssetId = assetId,
            Success = true,
            Asset = asset,
            BundleId = bundleId
        };

    /// <summary>Creates a failed result.</summary>
    public static AssetLoadResult<T> Failed(string assetId, string errorMessage)
        => new()
        {
            AssetId = assetId,
            Success = false,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// Result of ensuring assets are available (downloading bundles if needed).
/// </summary>
public sealed class AssetAvailabilityResult
{
    /// <summary>Asset IDs that were requested.</summary>
    public required IReadOnlyList<string> RequestedAssetIds { get; init; }

    /// <summary>Bundle IDs that were downloaded to satisfy the request.</summary>
    public required IReadOnlyList<string> DownloadedBundleIds { get; init; }

    /// <summary>Asset IDs that could not be resolved (don't exist).</summary>
    public required IReadOnlyList<string> UnresolvedAssetIds { get; init; }

    /// <summary>Whether all requested assets are now available.</summary>
    public bool AllAvailable => UnresolvedAssetIds.Count == 0;

    /// <summary>Number of assets that are now available.</summary>
    public int AvailableCount => RequestedAssetIds.Count - UnresolvedAssetIds.Count;

    /// <summary>Creates a result where all assets were already available.</summary>
    public static AssetAvailabilityResult AllAlreadyAvailable(IReadOnlyList<string> assetIds)
        => new()
        {
            RequestedAssetIds = assetIds,
            DownloadedBundleIds = Array.Empty<string>(),
            UnresolvedAssetIds = Array.Empty<string>()
        };
}
