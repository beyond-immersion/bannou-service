namespace BeyondImmersion.Bannou.AssetLoader.Models;

/// <summary>
/// Result of loading a bundle.
/// </summary>
public sealed class BundleLoadResult
{
    /// <summary>Bundle ID that was loaded.</summary>
    public required string BundleId { get; init; }

    /// <summary>Load status.</summary>
    public required BundleLoadStatus Status { get; init; }

    /// <summary>Number of assets in the bundle.</summary>
    public int AssetCount { get; init; }

    /// <summary>Error message if load failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the bundle was loaded from cache.</summary>
    public bool FromCache { get; init; }

    /// <summary>Download time in milliseconds (0 if from cache).</summary>
    public long DownloadTimeMs { get; init; }

    /// <summary>Bundle size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static BundleLoadResult Success(string bundleId, int assetCount, bool fromCache = false, long downloadTimeMs = 0, long sizeBytes = 0)
        => new()
        {
            BundleId = bundleId,
            Status = BundleLoadStatus.Success,
            AssetCount = assetCount,
            FromCache = fromCache,
            DownloadTimeMs = downloadTimeMs,
            SizeBytes = sizeBytes
        };

    /// <summary>Creates a result for already-loaded bundle.</summary>
    public static BundleLoadResult AlreadyLoaded(string bundleId)
        => new()
        {
            BundleId = bundleId,
            Status = BundleLoadStatus.AlreadyLoaded
        };

    /// <summary>Creates a failed result.</summary>
    public static BundleLoadResult Failed(string bundleId, string errorMessage)
        => new()
        {
            BundleId = bundleId,
            Status = BundleLoadStatus.Failed,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// Status of a bundle load operation.
/// </summary>
public enum BundleLoadStatus
{
    /// <summary>Bundle loaded successfully.</summary>
    Success,

    /// <summary>Bundle was already loaded.</summary>
    AlreadyLoaded,

    /// <summary>Bundle load failed.</summary>
    Failed,

    /// <summary>Bundle not found.</summary>
    NotFound,

    /// <summary>Download URL expired.</summary>
    UrlExpired,

    /// <summary>Download was cancelled.</summary>
    Cancelled
}
