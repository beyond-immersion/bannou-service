using System.Text;
using System.Xml;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

/// <summary>
/// Generates Stride projects for batch asset compilation.
/// </summary>
public sealed class StrideBatchProjectGenerator
{
    private readonly StrideCompilerOptions _options;
    private readonly ILogger<StrideBatchProjectGenerator>? _logger;

    /// <summary>
    /// Creates a new project generator.
    /// </summary>
    /// <param name="options">Compiler options.</param>
    /// <param name="logger">Optional logger.</param>
    public StrideBatchProjectGenerator(
        StrideCompilerOptions options,
        ILogger<StrideBatchProjectGenerator>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Generates a Stride project for compiling the specified assets.
    /// </summary>
    /// <param name="assets">Assets to include in the project.</param>
    /// <param name="outputDirectory">Directory for generated project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Path to the generated .csproj file.</returns>
    public async Task<string> GenerateAsync(
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo outputDirectory,
        CancellationToken ct = default)
    {
        outputDirectory.Create();

        var projectName = $"AssetBatch_{Guid.NewGuid():N}";
        var projectDir = outputDirectory.CreateSubdirectory(projectName);

        // Create asset directories
        var assetsDir = projectDir.CreateSubdirectory("Assets");

        // Generate .sdpkg file
        var sdpkgPath = Path.Combine(projectDir.FullName, $"{projectName}.sdpkg");
        await GenerateSdpkgAsync(sdpkgPath, projectName, assets, assetsDir, ct);

        // Generate .csproj file
        var csprojPath = Path.Combine(projectDir.FullName, $"{projectName}.csproj");
        await GenerateCsprojAsync(csprojPath, projectName, ct);

        // Copy assets to project
        await CopyAssetsAsync(assets, assetsDir, ct);

        _logger?.LogInformation(
            "Generated Stride project with {AssetCount} assets at {ProjectPath}",
            assets.Count, projectDir.FullName);

        return csprojPath;
    }

    private async Task GenerateSdpkgAsync(
        string path,
        string projectName,
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo assetsDir,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("!Package");
        sb.AppendLine($"Id: {Guid.NewGuid()}");
        sb.AppendLine("SerializedVersion: {{Stride: 4.2.0.0}}");

        // Meta section
        sb.AppendLine("Meta:");
        sb.AppendLine($"    Name: {projectName}");
        sb.AppendLine("    Version: 1.0.0");

        // Profiles section
        sb.AppendLine("Profiles:");
        sb.AppendLine("    - Name: Default");
        sb.AppendLine("      AssetFolders:");
        sb.AppendLine("          - Path: Assets");

        // Add asset references
        foreach (var asset in assets)
        {
            var relativePath = GetRelativeAssetPath(asset);
            var guid = Guid.NewGuid();

            // Generate .sdasset file for each asset
            var assetDefPath = Path.Combine(assetsDir.FullName, $"{Path.GetFileNameWithoutExtension(relativePath)}.sd{GetStrideAssetType(asset)}");
            await GenerateAssetDefinitionAsync(assetDefPath, asset, guid, ct);
        }

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }

    private async Task GenerateAssetDefinitionAsync(
        string path,
        ExtractedAsset asset,
        Guid guid,
        CancellationToken ct)
    {
        var sb = new StringBuilder();

        switch (asset.AssetType)
        {
            case AssetType.Model:
                sb.AppendLine("!Model");
                sb.AppendLine($"Id: {guid}");
                sb.AppendLine("SerializedVersion: {{Stride: 4.2.0.0}}");
                sb.AppendLine($"Source: !file {asset.Filename}");
                sb.AppendLine("Materials: {{}}");
                break;

            case AssetType.Texture:
                sb.AppendLine("!Texture");
                sb.AppendLine($"Id: {guid}");
                sb.AppendLine("SerializedVersion: {{Stride: 4.2.0.0}}");
                sb.AppendLine($"Source: !file {asset.Filename}");
                sb.AppendLine($"Width: {_options.MaxTextureSize}");
                sb.AppendLine($"Height: {_options.MaxTextureSize}");
                sb.AppendLine($"GenerateMipmaps: {_options.GenerateMipmaps.ToString().ToLowerInvariant()}");
                sb.AppendLine($"Format: {GetTextureFormat(asset)}");
                break;

            case AssetType.Audio:
                sb.AppendLine("!Sound");
                sb.AppendLine($"Id: {guid}");
                sb.AppendLine("SerializedVersion: {{Stride: 4.2.0.0}}");
                sb.AppendLine($"Source: !file {asset.Filename}");
                break;

            case AssetType.Animation:
                sb.AppendLine("!Animation");
                sb.AppendLine($"Id: {guid}");
                sb.AppendLine("SerializedVersion: {{Stride: 4.2.0.0}}");
                sb.AppendLine($"Source: !file {asset.Filename}");
                break;

            default:
                sb.AppendLine("!RawAsset");
                sb.AppendLine($"Id: {guid}");
                sb.AppendLine("SerializedVersion: {{Stride: 4.2.0.0}}");
                sb.AppendLine($"Source: !file {asset.Filename}");
                break;
        }

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }

    private async Task GenerateCsprojAsync(string path, string projectName, CancellationToken ct)
    {
        var strideVersion = _options.StrideVersion ?? "4.2.0.2188";

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <OutputType>Library</OutputType>
                <RootNamespace>{projectName}</RootNamespace>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Stride.Core.Assets.CompilerApp" Version="{strideVersion}" />
                <PackageReference Include="Stride.Assets" Version="{strideVersion}" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(path, csproj, ct);
    }

    private async Task CopyAssetsAsync(
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo assetsDir,
        CancellationToken ct)
    {
        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            var destPath = Path.Combine(assetsDir.FullName, asset.Filename);
            var destDir = Path.GetDirectoryName(destPath);

            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (File.Exists(asset.FilePath))
            {
                // Use async file operations
                await using var sourceStream = File.OpenRead(asset.FilePath);
                await using var destStream = File.Create(destPath);
                await sourceStream.CopyToAsync(destStream, ct);
            }
        }
    }

    private static string GetRelativeAssetPath(ExtractedAsset asset)
    {
        return asset.Filename.Replace('\\', '/');
    }

    private static string GetStrideAssetType(ExtractedAsset asset)
    {
        return asset.AssetType switch
        {
            AssetType.Model => "model",
            AssetType.Texture => "tex",
            AssetType.Audio => "sound",
            AssetType.Animation => "anim",
            _ => "raw"
        };
    }

    private string GetTextureFormat(ExtractedAsset asset)
    {
        return _options.TextureCompression switch
        {
            StrideTextureCompression.BC1 => "BC1_UNorm",
            StrideTextureCompression.BC3 => "BC3_UNorm",
            StrideTextureCompression.BC7 => "BC7_UNorm",
            StrideTextureCompression.ETC2 => "ETC2_RGBA",
            StrideTextureCompression.ASTC => "ASTC_6x6_UNorm",
            _ => "R8G8B8A8_UNorm"
        };
    }
}
