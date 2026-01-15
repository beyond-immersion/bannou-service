using System.Security.Cryptography;
using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Godot.Processing;

/// <summary>
/// Processes assets for Godot runtime loading.
/// Mostly pass-through for compatible formats, with optional conversion for incompatible ones.
/// </summary>
public sealed class GodotAssetProcessor : IAssetProcessor, IDisposable
{
    private readonly GodotProcessorOptions _options;
    private readonly ILogger<GodotAssetProcessor>? _logger;
    private readonly GodotTypeInferencer _typeInferencer = new();

    /// <summary>
    /// Creates a new Godot asset processor.
    /// </summary>
    /// <param name="options">Processor options.</param>
    /// <param name="logger">Optional logger.</param>
    public GodotAssetProcessor(GodotProcessorOptions? options = null, ILogger<GodotAssetProcessor>? logger = null)
    {
        _options = options ?? new GodotProcessorOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProcessorId => "godot";

    /// <inheritdoc />
    public IReadOnlyList<string> OutputContentTypes =>
    [
        GodotContentTypes.TexturePng,
        GodotContentTypes.TextureJpeg,
        GodotContentTypes.TextureWebp,
        GodotContentTypes.ModelGltfBinary,
        GodotContentTypes.ModelGltfJson,
        GodotContentTypes.AudioWav,
        GodotContentTypes.AudioOgg,
        GodotContentTypes.AudioMp3
    ];

    /// <inheritdoc />
    public IAssetTypeInferencer? TypeInferencer => _typeInferencer;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IProcessedAsset>> ProcessAsync(
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo workingDir,
        ProcessorOptions? options = null,
        CancellationToken ct = default)
    {
        if (assets.Count == 0)
            return new Dictionary<string, IProcessedAsset>();

        _logger?.LogInformation("Starting Godot asset processing for {AssetCount} assets", assets.Count);

        var results = new Dictionary<string, IProcessedAsset>();
        var processedCount = 0;
        var skippedCount = 0;
        var convertedCount = 0;

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var processedAsset = await ProcessSingleAssetAsync(asset, workingDir, ct);
                if (processedAsset != null)
                {
                    results[asset.AssetId] = processedAsset;
                    processedCount++;

                    if (processedAsset is GodotProcessedAsset godotAsset && godotAsset.WasConverted)
                        convertedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to process asset {AssetId}", asset.AssetId);

                if (_options.FailFast)
                    throw;

                skippedCount++;
            }
        }

        _logger?.LogInformation(
            "Godot processing complete: {ProcessedCount} processed, {ConvertedCount} converted, {SkippedCount} skipped",
            processedCount, convertedCount, skippedCount);

        return results;
    }

    private async Task<IProcessedAsset?> ProcessSingleAssetAsync(
        ExtractedAsset asset,
        DirectoryInfo workingDir,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(asset.Filename);
        var contentType = asset.ContentType ?? GodotContentTypes.FromExtension(ext);

        // Check if this format is directly loadable by Godot
        if (GodotTypeInferencer.IsRuntimeLoadable(ext))
        {
            return await CreatePassThroughAssetAsync(asset, contentType, ct);
        }

        // Check if conversion is needed
        if (GodotTypeInferencer.RequiresConversion(ext))
        {
            if (!_options.EnableConversion)
            {
                if (_options.SkipUnconvertible)
                {
                    _logger?.LogDebug("Skipping {AssetId} - conversion disabled and format requires conversion", asset.AssetId);
                    return null;
                }

                throw new GodotProcessingException(
                    $"Asset '{asset.AssetId}' requires conversion but conversion is disabled");
            }

            return await ConvertAssetAsync(asset, workingDir, ct);
        }

        // Unknown or pass-through format
        return await CreatePassThroughAssetAsync(asset, contentType, ct);
    }

    private async Task<GodotProcessedAsset> CreatePassThroughAssetAsync(
        ExtractedAsset asset,
        string contentType,
        CancellationToken ct)
    {
        var data = await File.ReadAllBytesAsync(asset.FilePath, ct);
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        _logger?.LogDebug("Pass-through asset {AssetId} ({ContentType})", asset.AssetId, contentType);

        return new GodotProcessedAsset
        {
            AssetId = asset.AssetId,
            Filename = asset.Filename,
            ContentType = contentType,
            Data = data,
            ContentHash = hash,
            Dependencies = new Dictionary<string, ReadOnlyMemory<byte>>(),
            Metadata = CreateMetadata(asset, wasConverted: false),
            SourceFilename = asset.Filename,
            WasConverted = false,
            GodotAssetType = asset.AssetType.ToString(),
            TextureTypeHint = asset.AssetType == AssetType.Texture
                ? _typeInferencer.InferTextureType(asset.Filename).ToString()
                : null
        };
    }

    private async Task<GodotProcessedAsset?> ConvertAssetAsync(
        ExtractedAsset asset,
        DirectoryInfo workingDir,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(asset.Filename).ToLowerInvariant();

        return ext switch
        {
            ".fbx" => await ConvertFbxToGltfAsync(asset, workingDir, ct),
            ".tga" or ".dds" or ".bmp" or ".tiff" or ".tif" => await ConvertImageToPngAsync(asset, workingDir, ct),
            ".flac" => await ConvertFlacToOggAsync(asset, workingDir, ct),
            _ => await HandleUnknownConversionAsync(asset, ct)
        };
    }

    private async Task<GodotProcessedAsset?> ConvertFbxToGltfAsync(
        ExtractedAsset asset,
        DirectoryInfo workingDir,
        CancellationToken ct)
    {
        await Task.CompletedTask; // Placeholder for future async conversion implementation

        if (string.IsNullOrEmpty(_options.FbxConverterPath))
        {
            if (_options.SkipUnconvertible)
            {
                _logger?.LogWarning(
                    "Skipping FBX asset {AssetId} - no FBX converter configured",
                    asset.AssetId);
                return null;
            }

            throw new GodotProcessingException(
                $"FBX conversion required for '{asset.AssetId}' but FbxConverterPath not configured");
        }

        // TODO: Implement FBX to glTF conversion using external tool
        // For now, log and skip
        _logger?.LogWarning(
            "FBX conversion not yet implemented for {AssetId}",
            asset.AssetId);

        if (_options.SkipUnconvertible)
            return null;

        throw new GodotProcessingException(
            $"FBX conversion not yet implemented for '{asset.AssetId}'");
    }

    private async Task<GodotProcessedAsset?> ConvertImageToPngAsync(
        ExtractedAsset asset,
        DirectoryInfo workingDir,
        CancellationToken ct)
    {
        await Task.CompletedTask; // Placeholder for future async conversion implementation

        // TODO: Implement image conversion (TGA/DDS/BMP to PNG)
        // Would require System.Drawing, ImageSharp, or similar library
        _logger?.LogWarning(
            "Image conversion not yet implemented for {AssetId} ({Extension})",
            asset.AssetId,
            Path.GetExtension(asset.Filename));

        if (_options.SkipUnconvertible)
            return null;

        throw new GodotProcessingException(
            $"Image conversion not yet implemented for '{asset.AssetId}'");
    }

    private async Task<GodotProcessedAsset?> ConvertFlacToOggAsync(
        ExtractedAsset asset,
        DirectoryInfo workingDir,
        CancellationToken ct)
    {
        await Task.CompletedTask; // Placeholder for future async conversion implementation

        // TODO: Implement FLAC to OGG conversion
        // Would require an audio conversion library
        _logger?.LogWarning(
            "Audio conversion not yet implemented for {AssetId}",
            asset.AssetId);

        if (_options.SkipUnconvertible)
            return null;

        throw new GodotProcessingException(
            $"Audio conversion not yet implemented for '{asset.AssetId}'");
    }

    private async Task<GodotProcessedAsset?> HandleUnknownConversionAsync(
        ExtractedAsset asset,
        CancellationToken ct)
    {
        _logger?.LogWarning(
            "Unknown format {Extension} for {AssetId}",
            Path.GetExtension(asset.Filename),
            asset.AssetId);

        if (_options.SkipUnconvertible)
            return null;

        // Pass through as binary
        return await CreatePassThroughAssetAsync(asset, GodotContentTypes.Binary, ct);
    }

    private Dictionary<string, object> CreateMetadata(ExtractedAsset asset, bool wasConverted)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceFile"] = asset.Filename,
            ["assetType"] = asset.AssetType.ToString()
        };

        if (asset.TextureType.HasValue)
        {
            metadata["textureType"] = asset.TextureType.Value.ToString();
        }

        if (wasConverted && _options.TrackOriginalFormat)
        {
            metadata["originalFormat"] = Path.GetExtension(asset.Filename);
        }

        if (asset.Category != null)
        {
            metadata["category"] = asset.Category;
        }

        if (asset.Tags != null && asset.Tags.Count > 0)
        {
            metadata["tags"] = asset.Tags;
        }

        return metadata;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No resources to dispose currently
    }
}
