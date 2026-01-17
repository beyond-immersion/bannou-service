namespace BeyondImmersion.Bannou.AssetLoader.Client;

/// <summary>
/// Progress information for asset loading operations.
/// Aggregates progress across multiple bundle downloads.
/// </summary>
public sealed class AssetLoadProgress
{
    /// <summary>
    /// Current phase of the overall load operation.
    /// </summary>
    public required AssetLoadPhase Phase { get; init; }

    /// <summary>
    /// Total number of bundles to download.
    /// </summary>
    public required int TotalBundles { get; init; }

    /// <summary>
    /// Number of bundles completed (downloaded or already cached).
    /// </summary>
    public required int CompletedBundles { get; init; }

    /// <summary>
    /// Total bytes to download across all bundles.
    /// May be -1 if unknown until resolution completes.
    /// </summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Total bytes downloaded so far.
    /// </summary>
    public required long DownloadedBytes { get; init; }

    /// <summary>
    /// Bundle currently being downloaded, if any.
    /// </summary>
    public string? CurrentBundleId { get; init; }

    /// <summary>
    /// Current download speed in bytes per second.
    /// </summary>
    public long BytesPerSecond { get; init; }

    /// <summary>
    /// Overall progress percentage (0.0 to 1.0).
    /// Returns -1 if total is unknown.
    /// </summary>
    public double Progress => TotalBytes > 0
        ? (double)DownloadedBytes / TotalBytes
        : TotalBundles > 0
            ? (double)CompletedBundles / TotalBundles
            : -1;

    /// <summary>
    /// Estimated time remaining based on current download speed.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (BytesPerSecond <= 0 || TotalBytes <= 0)
                return null;
            var remaining = TotalBytes - DownloadedBytes;
            return TimeSpan.FromSeconds((double)remaining / BytesPerSecond);
        }
    }

    /// <summary>
    /// Creates progress for the resolving phase.
    /// </summary>
    public static AssetLoadProgress Resolving() => new()
    {
        Phase = AssetLoadPhase.Resolving,
        TotalBundles = 0,
        CompletedBundles = 0,
        TotalBytes = -1,
        DownloadedBytes = 0
    };

    /// <summary>
    /// Creates progress for the downloading phase.
    /// </summary>
    public static AssetLoadProgress Downloading(
        int totalBundles,
        int completedBundles,
        long totalBytes,
        long downloadedBytes,
        string? currentBundleId = null,
        long bytesPerSecond = 0) => new()
    {
        Phase = AssetLoadPhase.Downloading,
        TotalBundles = totalBundles,
        CompletedBundles = completedBundles,
        TotalBytes = totalBytes,
        DownloadedBytes = downloadedBytes,
        CurrentBundleId = currentBundleId,
        BytesPerSecond = bytesPerSecond
    };

    /// <summary>
    /// Creates progress for completion.
    /// </summary>
    public static AssetLoadProgress Complete(int totalBundles, long totalBytes) => new()
    {
        Phase = AssetLoadPhase.Complete,
        TotalBundles = totalBundles,
        CompletedBundles = totalBundles,
        TotalBytes = totalBytes,
        DownloadedBytes = totalBytes
    };

    /// <summary>
    /// Creates progress for failure.
    /// </summary>
    public static AssetLoadProgress Failed(int totalBundles, int completedBundles, string errorMessage) => new()
    {
        Phase = AssetLoadPhase.Failed,
        TotalBundles = totalBundles,
        CompletedBundles = completedBundles,
        TotalBytes = 0,
        DownloadedBytes = 0,
        CurrentBundleId = errorMessage
    };
}

/// <summary>
/// Phase of an asset loading operation.
/// </summary>
public enum AssetLoadPhase
{
    /// <summary>Resolving which bundles are needed.</summary>
    Resolving,

    /// <summary>Downloading bundles.</summary>
    Downloading,

    /// <summary>Loading complete.</summary>
    Complete,

    /// <summary>Loading failed.</summary>
    Failed
}
