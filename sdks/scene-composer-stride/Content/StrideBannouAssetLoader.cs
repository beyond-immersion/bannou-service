using System;
using System.Threading;
using System.Threading.Tasks;
using Stride.Graphics;
using Stride.Rendering;

namespace BeyondImmersion.Bannou.Stride.SceneComposer.Content;

/// <summary>
/// IAssetLoader implementation that wraps StrideContentManager for the SceneComposer SDK.
/// </summary>
/// <remarks>
/// This class bridges the engine-agnostic <see cref="IAssetLoader"/> interface
/// to the Stride-specific <see cref="StrideContentManager"/>.
/// </remarks>
public class StrideBannouAssetLoader : IAssetLoader
{
    private readonly StrideContentManager _contentManager;

    /// <summary>
    /// Creates a new asset loader wrapping a StrideContentManager.
    /// </summary>
    /// <param name="contentManager">The content manager to use for loading assets.</param>
    public StrideBannouAssetLoader(StrideContentManager contentManager)
    {
        _contentManager = contentManager ?? throw new ArgumentNullException(nameof(contentManager));
    }

    /// <summary>
    /// Gets the underlying content manager.
    /// </summary>
    public StrideContentManager ContentManager => _contentManager;

    /// <inheritdoc/>
    public async Task<Model?> LoadModelAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default)
    {
        try
        {
            // StrideContentManager searches all loaded bundles by assetId
            // The bundleId parameter is for future multi-bundle disambiguation
            return await _contentManager.GetModelAsync(assetId, ct);
        }
        catch (AssetNotFoundException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Texture?> LoadTextureAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default)
    {
        try
        {
            return await _contentManager.GetTextureAsync(assetId, ct);
        }
        catch (AssetNotFoundException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Material?> LoadMaterialAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default)
    {
        // Materials are typically embedded in models, not loaded separately
        // Return null for now - could be extended if needed
        await Task.CompletedTask;
        return null;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetThumbnailAsync(
        string bundleId,
        string assetId,
        int width,
        int height,
        CancellationToken ct = default)
    {
        // Thumbnail generation not implemented yet
        // Would require off-screen rendering of the asset
        await Task.CompletedTask;
        return null;
    }

    /// <inheritdoc/>
    public async Task PreloadBundleAsync(string bundleId, CancellationToken ct = default)
    {
        // Bundle is already loaded if we have it
        // This is a no-op since StrideContentManager requires explicit LoadBundleAsync
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void UnloadBundle(string bundleId)
    {
        _contentManager.UnloadBundle(bundleId);
    }

    /// <inheritdoc/>
    public bool IsBundleLoaded(string bundleId)
    {
        return _contentManager.IsBundleLoaded(bundleId);
    }

    /// <inheritdoc/>
    public bool HasAsset(string bundleId, string assetId)
    {
        // StrideContentManager checks all bundles, not specific ones
        return _contentManager.HasAsset(assetId);
    }

    /// <summary>
    /// Loads a bundle from a file path.
    /// </summary>
    /// <param name="bundlePath">Path to the .bannou bundle file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bundle ID assigned to the loaded bundle.</returns>
    public async Task<string> LoadBundleFromFileAsync(string bundlePath, CancellationToken ct = default)
    {
        return await _contentManager.LoadBundleAsync(bundlePath, cancellationToken: ct);
    }

    /// <summary>
    /// Loads a bundle from a file path with a specific ID.
    /// </summary>
    /// <param name="bundlePath">Path to the .bannou bundle file.</param>
    /// <param name="bundleId">Bundle ID to assign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bundle ID assigned to the loaded bundle.</returns>
    public async Task<string> LoadBundleFromFileAsync(string bundlePath, string bundleId, CancellationToken ct = default)
    {
        return await _contentManager.LoadBundleAsync(bundlePath, bundleId, ct);
    }
}
