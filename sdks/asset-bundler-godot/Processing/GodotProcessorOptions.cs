using BeyondImmersion.Bannou.AssetBundler.Abstractions;

namespace BeyondImmersion.Bannou.AssetBundler.Godot.Processing;

/// <summary>
/// Configuration options for Godot asset processing.
/// </summary>
public sealed class GodotProcessorOptions : ProcessorOptions
{
    /// <summary>
    /// Whether to enable format conversion for incompatible formats.
    /// When true, attempts to convert FBX to glTF, TGA to PNG, etc.
    /// </summary>
    public bool EnableConversion { get; init; } = true;

    /// <summary>
    /// Whether to skip assets that require conversion when conversion is disabled
    /// or when the converter is not available.
    /// When false, throws an exception for unconvertible assets.
    /// </summary>
    public bool SkipUnconvertible { get; init; } = true;

    /// <summary>
    /// Path to external FBX to glTF converter (e.g., FBX2glTF).
    /// If not specified, FBX files will be skipped or cause an error.
    /// </summary>
    public string? FbxConverterPath { get; init; }

    /// <summary>
    /// Maximum texture dimension. Textures larger than this will be downscaled.
    /// Set to 0 to disable downscaling.
    /// </summary>
    public int MaxTextureSize { get; init; } = 4096;

    /// <summary>
    /// Whether to optimize PNG files by stripping metadata.
    /// </summary>
    public bool OptimizePng { get; init; } = false;

    /// <summary>
    /// Target quality for JPEG compression (1-100).
    /// Only applies when converting other formats to JPEG.
    /// </summary>
    public int JpegQuality { get; init; } = 90;

    /// <summary>
    /// Whether to generate ORM (Occlusion/Roughness/Metallic) packed textures
    /// from separate texture files.
    /// </summary>
    public bool GenerateOrmTextures { get; init; } = false;

    /// <summary>
    /// Whether to include the original format in metadata when converted.
    /// </summary>
    public bool TrackOriginalFormat { get; init; } = true;
}
