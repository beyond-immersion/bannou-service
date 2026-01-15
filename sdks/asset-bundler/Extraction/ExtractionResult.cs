namespace BeyondImmersion.Bannou.AssetBundler.Extraction;

/// <summary>
/// Result of extracting assets from a source.
/// </summary>
public sealed class ExtractionResult
{
    /// <summary>
    /// Source identifier.
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// List of extracted assets.
    /// </summary>
    public required IReadOnlyList<ExtractedAsset> Assets { get; init; }

    /// <summary>
    /// Working directory containing extracted files.
    /// </summary>
    public required DirectoryInfo WorkingDirectory { get; init; }

    /// <summary>
    /// Total extracted size in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Number of files skipped during extraction.
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// Reasons for skipped files.
    /// </summary>
    public IReadOnlyList<string>? SkipReasons { get; init; }

    /// <summary>
    /// Extraction duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
