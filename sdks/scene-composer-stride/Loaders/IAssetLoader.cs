using Stride.Rendering;

namespace BeyondImmersion.Bannou.Stride.SceneComposer;

/// <summary>
/// Interface for loading Stride assets from Bannou bundles.
/// </summary>
public interface IAssetLoader
{
    /// <summary>
    /// Load a model from a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier within the bundle.</param>
    /// <param name="variantId">Optional variant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded model, or null if not found.</returns>
    Task<Model?> LoadModelAsync(
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
    Task<global::Stride.Graphics.Texture?> LoadTextureAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Load a material from a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier within the bundle.</param>
    /// <param name="variantId">Optional variant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded material, or null if not found.</returns>
    Task<Material?> LoadMaterialAsync(
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
