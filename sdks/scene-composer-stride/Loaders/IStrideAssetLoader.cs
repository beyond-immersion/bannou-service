using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Loaders;

/// <summary>
/// Interface for loading specific Stride asset types from bundle data.
/// </summary>
/// <typeparam name="T">The Stride asset type to load.</typeparam>
public interface IStrideAssetLoader<T> where T : class
{
    /// <summary>
    /// Loads an asset from raw bundle data.
    /// </summary>
    /// <param name="data">Decompressed asset data from the bundle.</param>
    /// <param name="assetId">The asset ID for reference resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded Stride asset.</returns>
    Task<T> LoadAsync(byte[] data, string assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the estimated size of the loaded asset in bytes.
    /// </summary>
    /// <param name="asset">The loaded asset.</param>
    /// <returns>Estimated size in bytes for cache management.</returns>
    long EstimateSize(T asset);
}

/// <summary>
/// Result of loading an asset, including the asset and metadata.
/// </summary>
/// <typeparam name="T">The asset type.</typeparam>
public sealed class LoadedAsset<T> where T : class
{
    /// <summary>
    /// The loaded asset.
    /// </summary>
    public required T Asset { get; init; }

    /// <summary>
    /// Estimated size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// The asset ID.
    /// </summary>
    public required string AssetId { get; init; }
}
