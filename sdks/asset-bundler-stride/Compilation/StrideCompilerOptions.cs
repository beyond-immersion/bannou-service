namespace BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

/// <summary>
/// Configuration options for Stride asset compilation.
/// </summary>
public sealed class StrideCompilerOptions
{
    /// <summary>
    /// Stride version to use for compilation (e.g., "4.2.0.2188").
    /// If not specified, uses the latest available.
    /// </summary>
    public string? StrideVersion { get; init; }

    /// <summary>
    /// Path to dotnet executable. Defaults to "dotnet" (using PATH).
    /// </summary>
    public string DotnetPath { get; init; } = "dotnet";

    /// <summary>
    /// Build configuration (Debug or Release).
    /// </summary>
    public string Configuration { get; init; } = "Release";

    /// <summary>
    /// Target platform for asset compilation.
    /// </summary>
    public StridePlatform Platform { get; init; } = StridePlatform.Windows;

    /// <summary>
    /// Graphics backend for compilation.
    /// </summary>
    public StrideGraphicsBackend GraphicsBackend { get; init; } = StrideGraphicsBackend.Direct3D11;

    /// <summary>
    /// Timeout for build operations in milliseconds.
    /// </summary>
    public int BuildTimeoutMs { get; init; } = 300_000; // 5 minutes

    /// <summary>
    /// Whether to capture detailed build output for logging.
    /// </summary>
    public bool VerboseOutput { get; init; } = false;

    /// <summary>
    /// Maximum texture dimension for compilation.
    /// Larger textures will be downscaled.
    /// </summary>
    public int MaxTextureSize { get; init; } = 4096;

    /// <summary>
    /// Whether to generate mipmaps for textures.
    /// </summary>
    public bool GenerateMipmaps { get; init; } = true;

    /// <summary>
    /// Texture compression format.
    /// </summary>
    public StrideTextureCompression TextureCompression { get; init; } = StrideTextureCompression.BC7;

    /// <summary>
    /// Whether to automatically detect and handle WSL environment.
    /// When true (default), paths will be converted for Windows tools when running in WSL.
    /// </summary>
    public bool AutoDetectWsl { get; init; } = true;
}

/// <summary>
/// Target platforms for Stride compilation.
/// </summary>
public enum StridePlatform
{
    /// <summary>Windows desktop (x64).</summary>
    Windows,

    /// <summary>Linux desktop (x64).</summary>
    Linux,

    /// <summary>macOS (Apple Silicon or Intel).</summary>
    MacOS,

    /// <summary>Android mobile.</summary>
    Android,

    /// <summary>iOS mobile.</summary>
    iOS
}

/// <summary>
/// Graphics backends supported by Stride.
/// </summary>
public enum StrideGraphicsBackend
{
    /// <summary>DirectX 11.</summary>
    Direct3D11,

    /// <summary>DirectX 12.</summary>
    Direct3D12,

    /// <summary>Vulkan.</summary>
    Vulkan,

    /// <summary>OpenGL (Linux/macOS).</summary>
    OpenGL,

    /// <summary>OpenGL ES (mobile).</summary>
    OpenGLES
}

/// <summary>
/// Texture compression formats for Stride.
/// </summary>
public enum StrideTextureCompression
{
    /// <summary>No compression (RGBA8).</summary>
    None,

    /// <summary>BC1 (DXT1) - RGB, no alpha, 4:1 compression.</summary>
    BC1,

    /// <summary>BC3 (DXT5) - RGBA with interpolated alpha, 4:1 compression.</summary>
    BC3,

    /// <summary>BC7 - High quality RGBA, 3:1 compression.</summary>
    BC7,

    /// <summary>ETC2 - Mobile-friendly compression.</summary>
    ETC2,

    /// <summary>ASTC - Adaptive scalable texture compression.</summary>
    ASTC
}
