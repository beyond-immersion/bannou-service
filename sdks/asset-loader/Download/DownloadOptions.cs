namespace BeyondImmersion.Bannou.AssetLoader.Download;

/// <summary>
/// Options for download operations.
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>
    /// Maximum number of retry attempts for failed downloads.
    /// Default: 3
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Delay between retry attempts.
    /// Default: 1 second
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Timeout for the entire download operation.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Buffer size for reading download data.
    /// Default: 81920 bytes (80KB)
    /// </summary>
    public int BufferSize { get; init; } = 81920;

    /// <summary>
    /// Whether to verify content hash after download.
    /// Default: true
    /// </summary>
    public bool VerifyHash { get; init; } = true;

    /// <summary>
    /// User-Agent header for HTTP requests.
    /// </summary>
    public string? UserAgent { get; init; }
}
