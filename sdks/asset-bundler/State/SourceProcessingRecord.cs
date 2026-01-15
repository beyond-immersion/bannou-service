namespace BeyondImmersion.Bannou.AssetBundler.State;

/// <summary>
/// Record of a successfully processed source.
/// </summary>
public sealed class SourceProcessingRecord
{
    /// <summary>
    /// Source identifier.
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Content hash at time of processing.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Source version at time of processing.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// When processing completed.
    /// </summary>
    public required DateTimeOffset ProcessedAt { get; init; }

    /// <summary>
    /// Path to the generated bundle file.
    /// </summary>
    public required string BundlePath { get; init; }

    /// <summary>
    /// Number of assets in the bundle.
    /// </summary>
    public required int AssetCount { get; init; }

    /// <summary>
    /// When the bundle was uploaded (if applicable).
    /// </summary>
    public DateTimeOffset? UploadedAt { get; set; }

    /// <summary>
    /// Bundle ID assigned by server after upload (if applicable).
    /// </summary>
    public string? UploadedBundleId { get; set; }
}
