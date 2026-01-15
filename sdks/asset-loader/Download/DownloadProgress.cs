namespace BeyondImmersion.Bannou.AssetLoader.Download;

/// <summary>
/// Progress information for a bundle download.
/// </summary>
public sealed class BundleDownloadProgress
{
    /// <summary>Bundle ID being downloaded.</summary>
    public required string BundleId { get; init; }

    /// <summary>Current download phase.</summary>
    public required DownloadPhase Phase { get; init; }

    /// <summary>Bytes downloaded so far.</summary>
    public required long BytesDownloaded { get; init; }

    /// <summary>Total bytes to download (may be -1 if unknown).</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Progress percentage (0.0 to 1.0, -1 if unknown).</summary>
    public double Progress => TotalBytes > 0
        ? (double)BytesDownloaded / TotalBytes
        : -1;

    /// <summary>Current download speed in bytes per second.</summary>
    public long BytesPerSecond { get; init; }

    /// <summary>Estimated time remaining.</summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (BytesPerSecond <= 0 || TotalBytes <= 0)
                return null;
            var remaining = TotalBytes - BytesDownloaded;
            return TimeSpan.FromSeconds((double)remaining / BytesPerSecond);
        }
    }
}

/// <summary>
/// Phase of a download operation.
/// </summary>
public enum DownloadPhase
{
    /// <summary>Starting download, establishing connection.</summary>
    Starting,

    /// <summary>Actively downloading data.</summary>
    Downloading,

    /// <summary>Download complete, verifying hash.</summary>
    Verifying,

    /// <summary>Download and verification complete.</summary>
    Complete,

    /// <summary>Download failed.</summary>
    Failed
}
