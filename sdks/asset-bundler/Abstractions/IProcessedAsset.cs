namespace BeyondImmersion.Bannou.AssetBundler.Abstractions;

/// <summary>
/// Result of processing a single asset through an IAssetProcessor.
/// </summary>
public interface IProcessedAsset
{
    /// <summary>
    /// Original asset ID from extraction.
    /// </summary>
    string AssetId { get; }

    /// <summary>
    /// Filename for the processed asset.
    /// </summary>
    string Filename { get; }

    /// <summary>
    /// MIME content type of processed output.
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Processed asset data.
    /// </summary>
    ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Content hash of processed data (SHA256).
    /// </summary>
    string ContentHash { get; }

    /// <summary>
    /// Additional dependencies generated during processing.
    /// Key: dependency ID, Value: dependency data.
    /// Example: Stride generates buffer files alongside models.
    /// </summary>
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Dependencies { get; }

    /// <summary>
    /// Processor-specific metadata (e.g., Stride GUID, Unity asset path).
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }
}
