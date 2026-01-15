using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using System.Security.Cryptography;

namespace BeyondImmersion.Bannou.AssetBundler.Processing;

/// <summary>
/// Pass-through processor that packages assets as-is without transformation.
/// Use for audio files, pre-converted content, behavior definitions, etc.
/// </summary>
public sealed class RawAssetProcessor : IAssetProcessor
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly RawAssetProcessor Instance = new();

    private RawAssetProcessor() { }

    /// <inheritdoc />
    public string ProcessorId => "raw";

    /// <inheritdoc />
    public IReadOnlyList<string> OutputContentTypes => []; // Preserves original types

    /// <inheritdoc />
    public IAssetTypeInferencer? TypeInferencer => null;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IProcessedAsset>> ProcessAsync(
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo workingDir,
        ProcessorOptions? options = null,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, IProcessedAsset>();

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            var data = await File.ReadAllBytesAsync(asset.FilePath, ct);
            var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

            results[asset.AssetId] = new ProcessedAsset
            {
                AssetId = asset.AssetId,
                Filename = asset.Filename,
                ContentType = asset.ContentType ?? InferContentType(asset.Filename),
                Data = data,
                ContentHash = hash,
                Dependencies = new Dictionary<string, ReadOnlyMemory<byte>>(),
                Metadata = new Dictionary<string, object>()
            };
        }

        return results;
    }

    private static string InferContentType(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".glb" => "model/gltf-binary",
            ".gltf" => "model/gltf+json",
            ".fbx" => "application/x-fbx",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".json" => "application/json",
            ".yaml" or ".yml" => "application/x-yaml",
            _ => "application/octet-stream"
        };
    }
}
