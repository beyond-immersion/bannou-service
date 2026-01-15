using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;

namespace BeyondImmersion.Bannou.AssetBundler.Sources;

/// <summary>
/// Default type inferencer with basic file extension matching.
/// </summary>
public sealed class DefaultTypeInferencer : IAssetTypeInferencer
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly DefaultTypeInferencer Instance = new();

    private DefaultTypeInferencer() { }

    /// <inheritdoc />
    public AssetType InferAssetType(string filename, string? mimeType = null)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".fbx" or ".obj" or ".dae" or ".glb" or ".gltf" => AssetType.Model,
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".dds" or ".webp" or ".bmp" => AssetType.Texture,
            ".wav" or ".ogg" or ".mp3" or ".flac" or ".opus" => AssetType.Audio,
            ".anim" => AssetType.Animation,
            ".mat" or ".material" => AssetType.Material,
            ".yaml" or ".yml" when filename.Contains("behavior", StringComparison.OrdinalIgnoreCase) => AssetType.Behavior,
            ".json" when filename.Contains("behavior", StringComparison.OrdinalIgnoreCase) => AssetType.Behavior,
            _ => AssetType.Other
        };
    }

    /// <inheritdoc />
    public TextureType InferTextureType(string filename)
    {
        var lower = filename.ToLowerInvariant();

        if (lower.Contains("_normal") || lower.Contains("_nml") || lower.Contains("_n."))
            return TextureType.NormalMap;
        if (lower.Contains("_emissive") || lower.Contains("_emit") || lower.Contains("_e."))
            return TextureType.Emissive;
        if (lower.Contains("_mask") || lower.Contains("_metallic") || lower.Contains("_roughness") ||
            lower.Contains("_ao") || lower.Contains("_occlusion"))
            return TextureType.Mask;
        if (lower.Contains("_height") || lower.Contains("_displacement") || lower.Contains("_h."))
            return TextureType.HeightMap;
        if (lower.Contains("spr_") || lower.Contains("ui_") || lower.Contains("hud_") ||
            lower.Contains("_icon") || lower.Contains("_button"))
            return TextureType.UI;

        return TextureType.Color;
    }

    /// <inheritdoc />
    public bool ShouldExtract(string relativePath, string? category = null)
    {
        var lower = relativePath.ToLowerInvariant();

        // Skip Unity-specific files
        if (lower.EndsWith(".meta") || lower.EndsWith(".prefab") || lower.EndsWith(".unity") ||
            lower.EndsWith(".asset") || lower.Contains("/unity/") || lower.Contains("\\unity\\"))
            return false;

        // Skip Unreal-specific files
        if (lower.EndsWith(".uasset") || lower.EndsWith(".umap") ||
            lower.Contains("/unreal/") || lower.Contains("\\unreal\\"))
            return false;

        // Skip Maya/Max/Blender-specific files
        if (lower.EndsWith(".ma") || lower.EndsWith(".mb") || lower.EndsWith(".max") ||
            lower.EndsWith(".blend") || lower.EndsWith(".blend1"))
            return false;

        // Skip documentation and misc
        if (lower.EndsWith(".pdf") || lower.EndsWith(".txt") || lower.EndsWith(".md"))
            return false;

        return true;
    }
}
