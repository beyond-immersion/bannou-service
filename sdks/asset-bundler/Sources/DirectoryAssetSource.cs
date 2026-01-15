using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using System.Diagnostics;
using System.Security.Cryptography;

namespace BeyondImmersion.Bannou.AssetBundler.Sources;

/// <summary>
/// Asset source that reads from a directory of files.
/// </summary>
public sealed class DirectoryAssetSource : IAssetSource
{
    private readonly DirectoryInfo _sourceDirectory;
    private readonly string _sourceId;
    private readonly string _name;
    private readonly string _version;
    private readonly IReadOnlyDictionary<string, string> _tags;
    private string? _contentHash;

    /// <summary>
    /// Creates a new directory asset source.
    /// </summary>
    /// <param name="sourceDirectory">The directory containing assets.</param>
    /// <param name="sourceId">Unique source identifier.</param>
    /// <param name="name">Human-readable name.</param>
    /// <param name="version">Version string.</param>
    /// <param name="tags">Optional tags.</param>
    public DirectoryAssetSource(
        DirectoryInfo sourceDirectory,
        string sourceId,
        string? name = null,
        string? version = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        _sourceDirectory = sourceDirectory ?? throw new ArgumentNullException(nameof(sourceDirectory));
        _sourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _name = name ?? sourceDirectory.Name;
        _version = version ?? "1.0.0";
        _tags = tags ?? new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public string SourceId => _sourceId;

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public string Version => _version;

    /// <inheritdoc />
    public string ContentHash => _contentHash ??= ComputeDirectoryHash();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tags => _tags;

    /// <inheritdoc />
    public async Task<ExtractionResult> ExtractAsync(
        DirectoryInfo workingDir,
        IAssetTypeInferencer? typeInferencer = null,
        CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous file operations - placeholder for future async implementation

        var stopwatch = Stopwatch.StartNew();
        var assets = new List<ExtractedAsset>();
        var skippedReasons = new List<string>();
        long totalSize = 0;
        int skippedCount = 0;
        int assetIndex = 0;

        typeInferencer ??= DefaultTypeInferencer.Instance;

        foreach (var file in _sourceDirectory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(_sourceDirectory.FullName, file.FullName);

            // Check if file should be extracted
            if (!typeInferencer.ShouldExtract(relativePath))
            {
                skippedCount++;
                skippedReasons.Add($"Filtered: {relativePath}");
                continue;
            }

            // Copy to working directory preserving relative path
            var targetPath = Path.Combine(workingDir.FullName, relativePath);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (targetDir != null)
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(file.FullName, targetPath, overwrite: true);

            var assetType = typeInferencer.InferAssetType(file.Name);
            var textureType = assetType == AssetType.Texture
                ? typeInferencer.InferTextureType(file.Name)
                : (TextureType?)null;

            var assetId = $"{_sourceId}/{Path.GetFileNameWithoutExtension(file.Name)}_{assetIndex:D4}";

            assets.Add(new ExtractedAsset
            {
                AssetId = assetId,
                Filename = file.Name,
                FilePath = targetPath,
                RelativePath = relativePath,
                ContentType = InferContentType(file.Name),
                AssetType = assetType,
                TextureType = textureType,
                SizeBytes = file.Length
            });

            totalSize += file.Length;
            assetIndex++;
        }

        stopwatch.Stop();

        return new ExtractionResult
        {
            SourceId = _sourceId,
            Assets = assets,
            WorkingDirectory = workingDir,
            TotalSizeBytes = totalSize,
            SkippedCount = skippedCount,
            SkipReasons = skippedReasons.Count > 0 ? skippedReasons : null,
            Duration = stopwatch.Elapsed
        };
    }

    private string ComputeDirectoryHash()
    {
        using var sha = SHA256.Create();
        var files = _sourceDirectory.GetFiles("*", SearchOption.AllDirectories)
            .OrderBy(f => f.FullName)
            .ToList();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(_sourceDirectory.FullName, file.FullName);
            var pathBytes = System.Text.Encoding.UTF8.GetBytes(relativePath);
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

            using var stream = file.OpenRead();
            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, bytesRead, buffer, 0);
            }
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant();
    }

    private static string InferContentType(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".tga" => "image/x-targa",
            ".dds" => "image/vnd-ms.dds",
            ".glb" => "model/gltf-binary",
            ".gltf" => "model/gltf+json",
            ".fbx" => "application/x-fbx",
            ".obj" => "model/obj",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".json" => "application/json",
            ".yaml" or ".yml" => "application/x-yaml",
            _ => "application/octet-stream"
        };
    }
}
