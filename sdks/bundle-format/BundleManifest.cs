namespace BeyondImmersion.Bannou.Bundle.Format;

/// <summary>
/// Manifest describing the contents of a .bannou bundle.
/// Stored as manifest.json at the start of the bundle.
/// </summary>
public sealed class BundleManifest
{
    /// <summary>
    /// Bundle format version for compatibility checking.
    /// </summary>
    public required string FormatVersion { get; init; }

    /// <summary>
    /// Human-readable identifier for this bundle (e.g., "synty/polygon-adventure", "my-bundle-v1").
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Human-readable name for the bundle.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description of the bundle contents.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Version string for the bundle contents.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Timestamp when the bundle was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Account ID of the bundle creator.
    /// </summary>
    public required string CreatedBy { get; init; }

    /// <summary>
    /// Total uncompressed size of all assets in bytes.
    /// </summary>
    public required long TotalUncompressedSize { get; init; }

    /// <summary>
    /// Total compressed size of all assets in bytes.
    /// </summary>
    public required long TotalCompressedSize { get; init; }

    /// <summary>
    /// Number of assets in the bundle.
    /// </summary>
    public required int AssetCount { get; init; }

    /// <summary>
    /// Compression algorithm used (e.g., "lz4").
    /// </summary>
    public required string CompressionAlgorithm { get; init; }

    /// <summary>
    /// List of asset entries in the bundle.
    /// </summary>
    public required IReadOnlyList<BundleAssetEntry> Assets { get; init; }

    /// <summary>
    /// Optional metadata tags for the bundle.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Current format version constant.
    /// </summary>
    public const string CurrentFormatVersion = "1.0.0";
}

/// <summary>
/// Entry describing a single asset within a bundle.
/// </summary>
public sealed class BundleAssetEntry
{
    /// <summary>
    /// Asset ID (unique within the bundle).
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// Original filename of the asset.
    /// </summary>
    public required string Filename { get; init; }

    /// <summary>
    /// Content type (MIME type) of the asset.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Uncompressed size in bytes.
    /// </summary>
    public required long UncompressedSize { get; init; }

    /// <summary>
    /// Compressed size in bytes.
    /// </summary>
    public required long CompressedSize { get; init; }

    /// <summary>
    /// SHA256 hash of the uncompressed content.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Index of this asset in the bundle (0-based).
    /// Used to look up offset in index.bin.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// External asset identifier for cross-system references.
    /// </summary>
    public string? ExternalAssetId { get; init; }

    /// <summary>
    /// Tags associated with this asset for smart bundling decisions.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Optional asset-specific metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
