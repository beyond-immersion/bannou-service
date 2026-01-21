using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;

namespace BeyondImmersion.Bannou.AssetBundler.Godot;

/// <summary>
/// Type inferencer for assets targeting Godot engine.
/// Handles model, texture, animation, and audio files.
/// Filters out engine-specific files that aren't compatible with Godot.
/// </summary>
public sealed class GodotTypeInferencer : IAssetTypeInferencer
{
    /// <summary>
    /// Model file extensions that Godot can load at runtime.
    /// Godot uses GltfDocument.AppendFromBuffer() for runtime mesh loading.
    /// </summary>
    public static readonly IReadOnlySet<string> RuntimeModelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf"
    };

    /// <summary>
    /// Model file extensions that require conversion for Godot.
    /// These formats need external conversion to glTF before bundling.
    /// </summary>
    public static readonly IReadOnlySet<string> ConvertibleModelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".obj", ".dae", ".3ds", ".blend"
    };

    /// <summary>
    /// All model file extensions (both runtime and convertible).
    /// </summary>
    public static readonly IReadOnlySet<string> AllModelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".fbx", ".obj", ".dae", ".3ds", ".blend"
    };

    /// <summary>
    /// Texture file extensions that Godot can load at runtime.
    /// </summary>
    public static readonly IReadOnlySet<string> RuntimeTextureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    /// <summary>
    /// Texture file extensions that require conversion.
    /// </summary>
    public static readonly IReadOnlySet<string> ConvertibleTextureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".tga", ".dds", ".bmp", ".tiff", ".tif"
    };

    /// <summary>
    /// All texture file extensions.
    /// </summary>
    public static readonly IReadOnlySet<string> AllTextureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".tga", ".dds", ".bmp", ".tiff", ".tif"
    };

    /// <summary>
    /// Audio file extensions that Godot can load at runtime.
    /// </summary>
    public static readonly IReadOnlySet<string> RuntimeAudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".ogg", ".mp3"
    };

    /// <summary>
    /// Audio file extensions that require conversion.
    /// </summary>
    public static readonly IReadOnlySet<string> ConvertibleAudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".opus"
    };

    /// <summary>
    /// All audio file extensions.
    /// </summary>
    public static readonly IReadOnlySet<string> AllAudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".ogg", ".mp3", ".flac", ".opus"
    };

    /// <inheritdoc />
    public AssetType InferAssetType(string filename, string? mimeType = null)
    {
        var ext = Path.GetExtension(filename);

        if (AllModelExtensions.Contains(ext))
            return AssetType.Model;

        if (AllTextureExtensions.Contains(ext))
            return AssetType.Texture;

        if (AllAudioExtensions.Contains(ext))
            return AssetType.Audio;

        // Animation detection (FBX files with animation keywords)
        if (ext.Equals(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            var lower = filename.ToLowerInvariant();
            if (lower.Contains("anim") || lower.Contains("@"))
                return AssetType.Animation;
        }

        // Godot scene and resource files
        if (ext.Equals(".tscn", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".tres", StringComparison.OrdinalIgnoreCase))
            return AssetType.Other;

        // Behavior files
        var extLower = ext.ToLowerInvariant();
        if ((extLower == ".yaml" || extLower == ".yml" || extLower == ".json") &&
            filename.Contains("behavior", StringComparison.OrdinalIgnoreCase))
        {
            return AssetType.Behavior;
        }

        return AssetType.Other;
    }

    /// <inheritdoc />
    public TextureType InferTextureType(string filename)
    {
        var lower = filename.ToLowerInvariant();

        // Normal maps
        if (lower.Contains("_normal") || lower.Contains("_nml") ||
            lower.Contains("_n.") || lower.Contains("_norm"))
            return TextureType.NormalMap;

        // Emissive/glow
        if (lower.Contains("_emissive") || lower.Contains("_emit") ||
            lower.Contains("_e.") || lower.Contains("_glow"))
            return TextureType.Emissive;

        // Mask textures (metallic, roughness, AO combined)
        if (lower.Contains("_mask") || lower.Contains("_metallic") ||
            lower.Contains("_roughness") || lower.Contains("_ao") ||
            lower.Contains("_orm") || lower.Contains("_rma"))
            return TextureType.Mask;

        // Height/displacement maps
        if (lower.Contains("_height") || lower.Contains("_displacement") ||
            lower.Contains("_h.") || lower.Contains("_bump"))
            return TextureType.HeightMap;

        // UI elements
        if (lower.Contains("spr_") || lower.Contains("ui_") ||
            lower.Contains("hud_") || lower.Contains("_icon") ||
            lower.Contains("_button") || lower.Contains("_gui"))
            return TextureType.UI;

        return TextureType.Color;
    }

    /// <inheritdoc />
    public bool ShouldExtract(string relativePath, string? category = null)
    {
        var lower = relativePath.ToLowerInvariant();

        // Skip Stride-specific directories and files
        if (lower.Contains("/stride/") || lower.Contains("\\stride\\") ||
            lower.EndsWith(".sdpkg") || lower.EndsWith(".sdmodel") ||
            lower.EndsWith(".sdtex") || lower.EndsWith(".sdanim") ||
            lower.EndsWith(".sdmat"))
            return false;

        // Skip Unity-specific directories and files
        if (lower.Contains("/unity/") || lower.Contains("\\unity\\") ||
            lower.Contains("/unitypackage/") || lower.Contains("\\unitypackage\\"))
            return false;

        if (lower.EndsWith(".meta") || lower.EndsWith(".mat") ||
            lower.EndsWith(".prefab") || lower.EndsWith(".asset") ||
            lower.EndsWith(".unity") || lower.EndsWith(".anim"))
            return false;

        // Skip Unreal-specific files
        if (lower.Contains("/unreal/") || lower.Contains("\\unreal\\"))
            return false;

        if (lower.EndsWith(".uasset") || lower.EndsWith(".umap"))
            return false;

        // Skip Maya/Max working files
        if (lower.Contains("/maya/") || lower.EndsWith(".ma") ||
            lower.EndsWith(".mb") || lower.EndsWith(".max"))
            return false;

        // Skip documentation
        if (lower.EndsWith(".pdf") || lower.EndsWith(".txt") ||
            lower.EndsWith(".md") || lower.EndsWith(".html"))
            return false;

        // Skip source files (photoshop, substance, etc.)
        if (lower.EndsWith(".psd") || lower.EndsWith(".spp") ||
            lower.EndsWith(".sbs") || lower.EndsWith(".sbsar"))
            return false;

        // Skip Godot editor-only files (we want runtime-loadable assets)
        // .import files are editor cache, not needed in bundles
        if (lower.EndsWith(".import"))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if an extension can be loaded directly by Godot at runtime.
    /// </summary>
    /// <param name="extension">File extension (with dot).</param>
    /// <returns>True if Godot can load this format from a buffer at runtime.</returns>
    public static bool IsRuntimeLoadable(string extension)
    {
        return RuntimeModelExtensions.Contains(extension) ||
            RuntimeTextureExtensions.Contains(extension) ||
            RuntimeAudioExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks if an extension needs conversion before Godot can load it.
    /// </summary>
    /// <param name="extension">File extension (with dot).</param>
    /// <returns>True if conversion is required.</returns>
    public static bool RequiresConversion(string extension)
    {
        return ConvertibleModelExtensions.Contains(extension) ||
            ConvertibleTextureExtensions.Contains(extension) ||
            ConvertibleAudioExtensions.Contains(extension);
    }
}
