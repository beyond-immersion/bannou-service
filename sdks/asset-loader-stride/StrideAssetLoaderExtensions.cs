using BeyondImmersion.Bannou.AssetLoader.Stride.Loaders;
using Stride.Animations;
using Stride.Core;
using Stride.Graphics;
using Stride.Rendering;

namespace BeyondImmersion.Bannou.AssetLoader.Stride;

/// <summary>
/// Extension methods for registering Stride type loaders with AssetLoader.
/// </summary>
public static class StrideAssetLoaderExtensions
{
    /// <summary>
    /// Registers all Stride type loaders (Model, Texture, Animation) with the asset loader.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="services">Stride service registry.</param>
    /// <param name="graphicsDevice">Stride graphics device (required for textures).</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseStride(
        this AssetLoader loader,
        IServiceRegistry services,
        GraphicsDevice graphicsDevice,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        loader.RegisterTypeLoader(new StrideModelTypeLoader(services, debugLog));
        loader.RegisterTypeLoader(new StrideTextureTypeLoader(services, graphicsDevice, debugLog));
        loader.RegisterTypeLoader(new StrideAnimationTypeLoader(services, debugLog));

        return loader;
    }

    /// <summary>
    /// Registers only the Model type loader.
    /// Use this if you don't need texture or animation loading.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="services">Stride service registry.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseStrideModels(
        this AssetLoader loader,
        IServiceRegistry services,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(services);

        loader.RegisterTypeLoader(new StrideModelTypeLoader(services, debugLog));
        return loader;
    }

    /// <summary>
    /// Registers only the Texture type loader.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="services">Stride service registry.</param>
    /// <param name="graphicsDevice">Stride graphics device.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseStrideTextures(
        this AssetLoader loader,
        IServiceRegistry services,
        GraphicsDevice graphicsDevice,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        loader.RegisterTypeLoader(new StrideTextureTypeLoader(services, graphicsDevice, debugLog));
        return loader;
    }

    /// <summary>
    /// Registers only the Animation type loader.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="services">Stride service registry.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseStrideAnimations(
        this AssetLoader loader,
        IServiceRegistry services,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(services);

        loader.RegisterTypeLoader(new StrideAnimationTypeLoader(services, debugLog));
        return loader;
    }
}
