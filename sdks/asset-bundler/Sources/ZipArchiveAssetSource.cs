using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace BeyondImmersion.Bannou.AssetBundler.Sources;

/// <summary>
/// Asset source that reads from a ZIP archive.
/// </summary>
public sealed class ZipArchiveAssetSource : IAssetSource
{
    private readonly FileInfo _zipFile;
    private readonly string _sourceId;
    private readonly string _name;
    private readonly string _version;
    private readonly IReadOnlyDictionary<string, string> _tags;
    private string? _contentHash;

    /// <summary>
    /// Creates a new ZIP archive asset source.
    /// </summary>
    /// <param name="zipFile">The ZIP file containing assets.</param>
    /// <param name="sourceId">Unique source identifier.</param>
    /// <param name="name">Human-readable name.</param>
    /// <param name="version">Version string.</param>
    /// <param name="tags">Optional tags.</param>
    public ZipArchiveAssetSource(
        FileInfo zipFile,
        string sourceId,
        string? name = null,
        string? version = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        _zipFile = zipFile ?? throw new ArgumentNullException(nameof(zipFile));
        _sourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _name = name ?? Path.GetFileNameWithoutExtension(zipFile.Name);
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
    public string ContentHash => _contentHash ??= ComputeFileHash();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tags => _tags;

    /// <inheritdoc />
    public async Task<ExtractionResult> ExtractAsync(
        DirectoryInfo workingDir,
        IAssetTypeInferencer? typeInferencer = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var assets = new List<ExtractedAsset>();
        var skippedReasons = new List<string>();
        long totalSize = 0;
        int skippedCount = 0;
        int assetIndex = 0;

        typeInferencer ??= DefaultTypeInferencer.Instance;

        await using var fileStream = _zipFile.OpenRead();
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // Skip directories
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            // Skip hidden/system files
            if (entry.Name.StartsWith('.') || entry.Name.StartsWith("__"))
            {
                skippedCount++;
                skippedReasons.Add($"Hidden/system: {entry.FullName}");
                continue;
            }

            // Check if file should be extracted
            if (!typeInferencer.ShouldExtract(entry.FullName))
            {
                skippedCount++;
                skippedReasons.Add($"Filtered: {entry.FullName}");
                continue;
            }

            // Extract to working directory
            var targetPath = Path.Combine(workingDir.FullName, entry.FullName);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (targetDir != null)
            {
                Directory.CreateDirectory(targetDir);
            }

            await using (var entryStream = entry.Open())
            await using (var targetStream = File.Create(targetPath))
            {
                await entryStream.CopyToAsync(targetStream, ct);
            }

            var assetType = typeInferencer.InferAssetType(entry.Name);
            var textureType = assetType == AssetType.Texture
                ? typeInferencer.InferTextureType(entry.Name)
                : (TextureType?)null;

            var assetId = $"{_sourceId}/{Path.GetFileNameWithoutExtension(entry.Name)}_{assetIndex:D4}";

            assets.Add(new ExtractedAsset
            {
                AssetId = assetId,
                Filename = entry.Name,
                FilePath = targetPath,
                RelativePath = entry.FullName,
                ContentType = InferContentType(entry.Name),
                AssetType = assetType,
                TextureType = textureType,
                SizeBytes = entry.Length
            });

            totalSize += entry.Length;
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

    private string ComputeFileHash()
    {
        using var stream = _zipFile.OpenRead();
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
