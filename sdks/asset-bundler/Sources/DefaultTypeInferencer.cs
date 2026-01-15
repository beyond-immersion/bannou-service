using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;

namespace BeyondImmersion.Bannou.AssetBundler.Sources;

/// <summary>
/// Default type inferencer with configurable file extension and pattern matching.
/// Provides sensible defaults while allowing customization for vendor-specific conventions.
/// </summary>
public class DefaultTypeInferencer : IAssetTypeInferencer
{
    /// <summary>
    /// Singleton instance with default configuration.
    /// </summary>
    public static readonly DefaultTypeInferencer Instance = new();

    private readonly HashSet<string> _excludedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    private readonly Dictionary<string, Func<string, bool>> _categoryFilters;

    /// <summary>
    /// Creates a new DefaultTypeInferencer with default configuration.
    /// </summary>
    public DefaultTypeInferencer()
    {
        _excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Unity-specific
            ".meta", ".prefab", ".unity", ".asset",
            // Unreal-specific
            ".uasset", ".umap",
            // DCC tool formats
            ".ma", ".mb", ".max", ".blend", ".blend1",
            // Documentation
            ".pdf", ".txt", ".md"
        };

        _excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unity", "unreal", "unity_version", "unreal_version",
            ".mayaswatches", "__macosx"
        };

        _categoryFilters = new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a copy of this inferencer that can be customized.
    /// </summary>
    /// <returns>A new mutable DefaultTypeInferencer.</returns>
    public DefaultTypeInferencer Clone()
    {
        var clone = new DefaultTypeInferencer();
        foreach (var ext in _excludedExtensions)
            clone._excludedExtensions.Add(ext);
        foreach (var dir in _excludedDirectories)
            clone._excludedDirectories.Add(dir);
        foreach (var (category, filter) in _categoryFilters)
            clone._categoryFilters[category] = filter;
        return clone;
    }

    /// <summary>
    /// Adds an extension to exclude from extraction.
    /// </summary>
    /// <param name="extension">Extension including dot (e.g., ".meta").</param>
    /// <returns>This instance for fluent chaining.</returns>
    public DefaultTypeInferencer ExcludeExtension(string extension)
    {
        _excludedExtensions.Add(extension);
        return this;
    }

    /// <summary>
    /// Adds a directory name to exclude from extraction.
    /// Files in paths containing this directory name will be excluded.
    /// </summary>
    /// <param name="directoryName">Directory name to exclude (e.g., "unity").</param>
    /// <returns>This instance for fluent chaining.</returns>
    public DefaultTypeInferencer ExcludeDirectory(string directoryName)
    {
        _excludedDirectories.Add(directoryName);
        return this;
    }

    /// <summary>
    /// Registers a category-specific extraction filter.
    /// When a category is specified, this filter is applied in addition to default rules.
    /// </summary>
    /// <param name="category">Category name (e.g., "polygon", "interface").</param>
    /// <param name="filter">Filter function that returns true if the path should be extracted.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public DefaultTypeInferencer RegisterCategoryFilter(string category, Func<string, bool> filter)
    {
        _categoryFilters[category] = filter;
        return this;
    }

    /// <inheritdoc />
    public virtual AssetType InferAssetType(string filename, string? mimeType = null)
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
    public virtual TextureType InferTextureType(string filename)
    {
        var lower = filename.ToLowerInvariant();

        // Check full path for directory hints first
        if (ContainsDirectoryPattern(lower, "ui", "hud", "sprites", "interface"))
            return TextureType.UI;

        // Then check filename patterns
        var filenameOnly = Path.GetFileName(lower);

        // Normal maps
        if (HasSuffix(filenameOnly, "_normal", "_nml", "_n") ||
            filenameOnly.Contains("_normal"))
            return TextureType.NormalMap;

        // Emissive
        if (HasSuffix(filenameOnly, "_emissive", "_emission", "_emit", "_e") ||
            filenameOnly.Contains("_emissive"))
            return TextureType.Emissive;

        // Mask textures (PBR channels)
        if (HasSuffix(filenameOnly, "_mask", "_metallic", "_metalness", "_roughness",
            "_ao", "_occlusion", "_m", "_r"))
            return TextureType.Mask;

        // Height maps
        if (HasSuffix(filenameOnly, "_height", "_displacement", "_h"))
            return TextureType.HeightMap;

        // UI from prefix/infix patterns
        if (HasPrefix(filenameOnly, "spr_", "ui_", "hud_") ||
            filenameOnly.Contains("_icon") || filenameOnly.Contains("_button") ||
            filenameOnly.Contains("_bar") || filenameOnly.Contains("_panel") ||
            filenameOnly.Contains("_frame"))
            return TextureType.UI;

        return TextureType.Color;
    }

    /// <inheritdoc />
    public virtual bool ShouldExtract(string relativePath, string? category = null)
    {
        var lower = relativePath.Replace('\\', '/').ToLowerInvariant();

        // Check excluded extensions
        var ext = Path.GetExtension(lower);
        if (_excludedExtensions.Contains(ext))
            return false;

        // Check excluded directories
        var pathParts = lower.Split('/');
        foreach (var part in pathParts)
        {
            if (_excludedDirectories.Contains(part))
                return false;
        }

        // Apply category-specific filter if registered
        if (category != null && _categoryFilters.TryGetValue(category, out var filter))
        {
            return filter(lower);
        }

        return true;
    }

    /// <summary>
    /// Checks if the path contains any of the specified directory patterns.
    /// </summary>
    protected static bool ContainsDirectoryPattern(string path, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (path.Contains($"/{pattern}/") || path.Contains($"\\{pattern}\\") ||
                path.StartsWith($"{pattern}/") || path.StartsWith($"{pattern}\\"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the filename ends with any of the specified suffixes (before extension).
    /// </summary>
    protected static bool HasSuffix(string filename, params string[] suffixes)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        foreach (var suffix in suffixes)
        {
            if (nameWithoutExt.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the filename starts with any of the specified prefixes.
    /// </summary>
    protected static bool HasPrefix(string filename, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
