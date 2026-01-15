using BeyondImmersion.Bannou.AssetBundler.Abstractions;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

/// <summary>
/// Represents an asset compiled through Stride's pipeline.
/// </summary>
public sealed class StrideProcessedAsset : IProcessedAsset
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
    /// Stride GUID assigned during compilation.
    /// </summary>
    public Guid StrideGuid { get; init; }

    /// <summary>
    /// Stride ObjectId from the compiled output.
    /// </summary>
    public string? ObjectId { get; init; }

    /// <summary>
    /// Original source filename before compilation.
    /// </summary>
    public string? SourceFilename { get; init; }

    /// <summary>
    /// Asset type as determined by Stride.
    /// </summary>
    public string? StrideAssetType { get; init; }
}
