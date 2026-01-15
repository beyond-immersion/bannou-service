using Godot;

namespace BeyondImmersion.Bannou.SceneComposer.Godot.Loaders;

/// <summary>
/// Interface for loading Godot assets from Bannou bundles.
/// </summary>
public interface IAssetLoader
{
    /// <summary>
    /// Load a mesh from a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier within the bundle.</param>
    /// <param name="variantId">Optional variant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded mesh, or null if not found.</returns>
    Task<Mesh?> LoadMeshAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Load a texture from a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier within the bundle.</param>
    /// <param name="variantId">Optional variant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded texture, or null if not found.</returns>
    Task<Texture2D?> LoadTextureAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Load an audio stream from a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier within the bundle.</param>
    /// <param name="variantId">Optional variant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded audio stream, or null if not found.</returns>
    Task<AudioStream?> LoadAudioStreamAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get a thumbnail for an asset.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier.</param>
    /// <param name="width">Thumbnail width.</param>
    /// <param name="height">Thumbnail height.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PNG image data, or null if unavailable.</returns>
    Task<byte[]?> GetThumbnailAsync(
        string bundleId,
        string assetId,
        int width,
        int height,
        CancellationToken ct = default);

    /// <summary>
    /// Preload a bundle for faster asset access.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PreloadBundleAsync(string bundleId, CancellationToken ct = default);

    /// <summary>
    /// Unload a bundle to free memory.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    void UnloadBundle(string bundleId);

    /// <summary>
    /// Check if a bundle is loaded.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    bool IsBundleLoaded(string bundleId);

    /// <summary>
    /// Check if an asset exists in a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier.</param>
    bool HasAsset(string bundleId, string assetId);
}
