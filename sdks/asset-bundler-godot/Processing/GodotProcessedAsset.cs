using BeyondImmersion.Bannou.AssetBundler.Abstractions;

namespace BeyondImmersion.Bannou.AssetBundler.Godot.Processing;

/// <summary>
/// Represents an asset processed for Godot runtime loading.
/// </summary>
public sealed class GodotProcessedAsset : IProcessedAsset
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public required string Filename { get; init; }

    /// <inheritdoc />
    public required string ContentType { get; init; }

    /// <inheritdoc />
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <inheritdoc />
    public required string ContentHash { get; init; }

    /// <inheritdoc />
    public required IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Dependencies { get; init; }

    /// <inheritdoc />
    public required IReadOnlyDictionary<string, object> Metadata { get; init; }

    /// <summary>
    /// Original source filename before any conversion.
    /// </summary>
    public string? SourceFilename { get; init; }

    /// <summary>
    /// Original content type before conversion (if converted).
    /// </summary>
    public string? OriginalContentType { get; init; }

    /// <summary>
    /// Whether the asset was converted from another format.
    /// </summary>
    public bool WasConverted { get; init; }

    /// <summary>
    /// Asset type as determined by Godot type inferencer.
    /// </summary>
    public string? GodotAssetType { get; init; }

    /// <summary>
    /// Texture type hint for texture assets.
    /// </summary>
    public string? TextureTypeHint { get; init; }
}
