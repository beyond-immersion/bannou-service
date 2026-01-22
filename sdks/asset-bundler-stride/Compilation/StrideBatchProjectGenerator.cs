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

        // Create directory structure
        var assetsDir = projectDir.CreateSubdirectory("Assets");
        var resourcesDir = projectDir.CreateSubdirectory("Resources");

        // Copy source files to Resources folder first
        await CopyAssetsAsync(assets, resourcesDir, ct);

        // Generate .sdpkg file (creates asset definition files in Assets folder)
        var sdpkgPath = Path.Combine(projectDir.FullName, $"{projectName}.sdpkg");
        await GenerateSdpkgAsync(sdpkgPath, projectName, assets, assetsDir, resourcesDir, ct);

        // Generate .csproj file
        var csprojPath = Path.Combine(projectDir.FullName, $"{projectName}.csproj");
        await GenerateCsprojAsync(csprojPath, projectName, ct);

        // Generate Program.cs (required for WinExe output type)
        await GenerateProgramCsAsync(projectDir.FullName, ct);

        _logger?.LogInformation(
            "Generated Stride project with {AssetCount} assets at {ProjectPath}",
            assets.Count, projectDir.FullName);

        return csprojPath;
    }

    private async Task<Dictionary<string, Guid>> GenerateSdpkgAsync(
        string path,
        string projectName,
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo assetsDir,
        DirectoryInfo resourcesDir,
        CancellationToken ct)
    {
        // Track asset GUIDs for RootAssets section
        var assetGuids = new Dictionary<string, Guid>();

        // Generate asset definition files and track GUIDs
        foreach (var asset in assets)
        {
            var assetGuid = Guid.NewGuid();
            var safeAssetId = MakeSafeFileName(asset.AssetId);
            assetGuids[safeAssetId] = assetGuid;

            // Generate asset definition file in Assets folder
            var assetDefPath = Path.Combine(assetsDir.FullName, $"{safeAssetId}.sd{GetStrideAssetType(asset)}");
            var relativeResourcePath = $"../Resources/{asset.Filename}";
            await GenerateAssetDefinitionAsync(assetDefPath, asset, assetGuid, relativeResourcePath, ct);
        }

        // Build RootAssets section
        var rootAssetsBuilder = new StringBuilder();
        foreach (var (assetId, guid) in assetGuids)
        {
            rootAssetsBuilder.AppendLine($"    - {guid}:{assetId}");
        }
        var rootAssets = rootAssetsBuilder.Length > 0
            ? rootAssetsBuilder.ToString().TrimEnd()
            : "[]";

        // Generate sdpkg content using correct format
        var sdpkgContent = $@"!Package
SerializedVersion: {{Assets: 3.1.0.0}}
Meta:
    Name: {projectName}
    Version: 1.0.0
    Authors: []
    Owners: []
    Dependencies: null
AssetFolders:
    -   Path: !dir Assets
ResourceFolders:
    - !dir Resources
OutputGroupDirectories: {{}}
ExplicitFolders: []
Bundles: []
TemplateFolders: []
RootAssets:
{rootAssets}";

        await File.WriteAllTextAsync(path, sdpkgContent, ct);
        return assetGuids;
    }

    private async Task GenerateAssetDefinitionAsync(
        string path,
        ExtractedAsset asset,
        Guid guid,
        string resourcePath,
        CancellationToken ct)
    {
        string content;

        switch (asset.AssetType)
        {
            case AssetType.Model:
                content = $@"!Model
Id: {guid}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Source: {resourcePath}
PivotPosition: {{X: 0.0, Y: 0.0, Z: 0.0}}
Materials: {{}}
Skeleton: null";
                break;

            case AssetType.Texture:
                content = GenerateTextureYaml(guid, resourcePath);
                break;

            case AssetType.Audio:
                content = $@"!Sound
Id: {guid}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Source: {resourcePath}";
                break;

            case AssetType.Animation:
                content = $@"!Animation
Id: {guid}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Source: {resourcePath}";
                break;

            default:
                content = $@"!RawAsset
Id: {guid}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Source: {resourcePath}";
                break;
        }

        await File.WriteAllTextAsync(path, content, ct);
    }

    private string GenerateTextureYaml(Guid guid, string resourcePath)
    {
        // Use ColorTextureType for general textures
        return $@"!Texture
Id: {guid}
SerializedVersion: {{Stride: 2.0.0.0}}
Tags: []
Source: {resourcePath}
Type: !ColorTextureType
    ColorKeyColor: {{R: 255, G: 0, B: 255, A: 255}}";
    }

    private async Task GenerateCsprojAsync(string path, string projectName, CancellationToken ct)
    {
        var strideVersion = _options.StrideVersion ?? "4.3.0.2507";

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
                    <OutputType>WinExe</OutputType>
                    <RootNamespace>{projectName}</RootNamespace>
                    <OutputPath>Bin\Windows\Debug\</OutputPath>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                    <DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>
                </PropertyGroup>

                <ItemGroup>
                    <PackageReference Include="Stride.Core.Assets.CompilerApp" Version="{strideVersion}" IncludeAssets="build;buildTransitive" />
                    <PackageReference Include="Stride.Engine" Version="{strideVersion}" />
                </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(path, csproj, ct);
    }

    private static async Task GenerateProgramCsAsync(string projectDir, CancellationToken ct)
    {
        var programPath = Path.Combine(projectDir, "Program.cs");
        const string programContent = "class Program { static void Main() { } }";
        await File.WriteAllTextAsync(programPath, programContent, ct);
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

    private static string GetStrideAssetType(ExtractedAsset asset)
    {
        return asset.AssetType switch
        {
            AssetType.Model => "m3d",
            AssetType.Texture => "tex",
            AssetType.Audio => "sound",
            AssetType.Animation => "anim",
            _ => "raw"
        };
    }

    private static string MakeSafeFileName(string name)
    {
        // Convert path separators to underscores to match Stride's index format
        var sb = new StringBuilder(name);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            sb.Replace(c, '_');
        }
        sb.Replace(Path.DirectorySeparatorChar, '_');
        sb.Replace(Path.AltDirectorySeparatorChar, '_');
        sb.Replace('/', '_');
        sb.Replace('\\', '_');
        return sb.ToString();
    }
}
