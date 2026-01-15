using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;

namespace BeyondImmersion.Bannou.AssetBundler.Stride;

/// <summary>
/// Type inferencer for assets targeting Stride engine compilation.
/// Handles model, texture, animation, and audio files.
/// </summary>
public sealed class StrideTypeInferencer : IAssetTypeInferencer
{
    /// <summary>
    /// Model file extensions supported by Stride.
    /// </summary>
    public static readonly IReadOnlySet<string> ModelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".obj", ".dae", ".glb", ".gltf", ".3ds", ".blend"
    };

    /// <summary>
    /// Texture file extensions supported by Stride.
    /// </summary>
    public static readonly IReadOnlySet<string> TextureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tga", ".dds", ".webp", ".bmp", ".tiff", ".gif"
    };

    /// <summary>
    /// Audio file extensions supported by Stride.
    /// </summary>
    public static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".ogg", ".mp3", ".flac"
    };

    /// <inheritdoc />
    public AssetType InferAssetType(string filename, string? mimeType = null)
    {
        var ext = Path.GetExtension(filename);

        if (ModelExtensions.Contains(ext))
            return AssetType.Model;

        if (TextureExtensions.Contains(ext))
            return AssetType.Texture;

        if (AudioExtensions.Contains(ext))
            return AssetType.Audio;

        // Animation detection (FBX files with animation keywords)
        if (ext.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".anim", StringComparison.OrdinalIgnoreCase))
        {
            var lower = filename.ToLowerInvariant();
            if (lower.Contains("anim") || lower.Contains("@"))
                return AssetType.Animation;
        }

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

        // Skip engine-specific directories
        if (lower.Contains("/unity/") || lower.Contains("\\unity\\") ||
            lower.Contains("/unitypackage/") || lower.Contains("\\unitypackage\\"))
            return false;

        if (lower.Contains("/unreal/") || lower.Contains("\\unreal\\"))
            return false;

        // Skip Unity-specific files
        if (lower.EndsWith(".meta") || lower.EndsWith(".mat") ||
            lower.EndsWith(".prefab") || lower.EndsWith(".asset") ||
            lower.EndsWith(".unity"))
            return false;

        // Skip Unreal-specific files
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

        return true;
    }
}
