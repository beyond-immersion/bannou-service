using BeyondImmersion.Bannou.AssetLoader.Godot.Loaders;

namespace BeyondImmersion.Bannou.AssetLoader.Godot;

/// <summary>
/// Extension methods for registering Godot type loaders with AssetLoader.
/// </summary>
public static class GodotAssetLoaderExtensions
{
    /// <summary>
    /// Registers all Godot type loaders (Texture2D, Mesh, AudioStream) with the asset loader.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseGodot(
        this AssetLoader loader,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);

        loader.RegisterTypeLoader(new GodotTexture2DTypeLoader(debugLog));
        loader.RegisterTypeLoader(new GodotMeshTypeLoader(debugLog));
        loader.RegisterTypeLoader(new GodotAudioStreamTypeLoader(debugLog));

        return loader;
    }

    /// <summary>
    /// Registers only the Texture2D type loader.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseGodotTextures(
        this AssetLoader loader,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        loader.RegisterTypeLoader(new GodotTexture2DTypeLoader(debugLog));
        return loader;
    }

    /// <summary>
    /// Registers only the Mesh type loader.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseGodotMeshes(
        this AssetLoader loader,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        loader.RegisterTypeLoader(new GodotMeshTypeLoader(debugLog));
        return loader;
    }

    /// <summary>
    /// Registers only the AudioStream type loader.
    /// </summary>
    /// <param name="loader">The asset loader to configure.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    /// <returns>The asset loader for chaining.</returns>
    public static AssetLoader UseGodotAudio(
        this AssetLoader loader,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        loader.RegisterTypeLoader(new GodotAudioStreamTypeLoader(debugLog));
        return loader;
    }
}
