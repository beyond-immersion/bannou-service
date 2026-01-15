using BeyondImmersion.Bannou.AssetBundler.Abstractions;

namespace BeyondImmersion.Bannou.AssetBundler.Processing;

/// <summary>
/// Default implementation of IProcessedAsset.
/// </summary>
public sealed class ProcessedAsset : IProcessedAsset
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
}
