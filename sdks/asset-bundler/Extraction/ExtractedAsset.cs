namespace BeyondImmersion.Bannou.AssetBundler.Extraction;

/// <summary>
/// Represents a single asset extracted from a source.
/// </summary>
public sealed class ExtractedAsset
{
    /// <summary>
    /// Unique asset identifier within the source.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public required string Filename { get; init; }

    /// <summary>
    /// Full path to the extracted file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Relative path within the source.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// MIME content type (may be null if unknown).
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// General asset type.
    /// </summary>
    public required AssetType AssetType { get; init; }

    /// <summary>
    /// Texture type hint (only relevant for texture assets).
    /// </summary>
    public TextureType? TextureType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Optional category from source (e.g., "characters", "props").
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional tags for categorization.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Optional metadata from source.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
