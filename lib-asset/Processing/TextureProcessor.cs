using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Processor for texture assets (images).
/// Handles compression, resizing, and format conversion.
/// </summary>
public sealed class TextureProcessor : IAssetProcessor
{
    private readonly IAssetStorageProvider _storageProvider;
    private readonly ILogger<TextureProcessor> _logger;
    private readonly AssetServiceConfiguration _configuration;

    private static readonly List<string> TextureContentTypes = new()
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp",
        "image/tiff",
        "image/bmp",
        "image/gif"
    };

    /// <inheritdoc />
    public string PoolType => "texture-processor";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes => TextureContentTypes;

    /// <summary>
    /// Creates a new TextureProcessor instance.
    /// </summary>
    public TextureProcessor(
        IAssetStorageProvider storageProvider,
        ILogger<TextureProcessor> logger,
        AssetServiceConfiguration configuration)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public bool CanProcess(string contentType)
    {
        return TextureContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<AssetValidationResult> ValidateAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        // Check if content type is supported
        if (!CanProcess(context.ContentType))
        {
            return Task.FromResult(AssetValidationResult.Invalid(
                $"Unsupported content type: {context.ContentType}",
                "UNSUPPORTED_CONTENT_TYPE"));
        }

        // Check file size limits
        var maxSizeBytes = _configuration.MaxUploadSizeMb * 1024L * 1024L;
        if (context.SizeBytes > maxSizeBytes)
        {
            return Task.FromResult(AssetValidationResult.Invalid(
                $"File size {context.SizeBytes} exceeds maximum {maxSizeBytes} bytes",
                "FILE_TOO_LARGE"));
        }

        // Check for potentially problematic scenarios
        if (context.SizeBytes > 100 * 1024 * 1024) // > 100MB
        {
            warnings.Add("Large texture file may take significant time to process");
        }

        _logger.LogDebug(
            "Validated texture asset {AssetId}: valid with {WarningCount} warnings",
            context.AssetId,
            warnings.Count);

        return Task.FromResult(AssetValidationResult.Valid(warnings.Count > 0 ? warnings : null));
    }

    /// <inheritdoc />
    public async Task<AssetProcessingResult> ProcessAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing texture asset {AssetId} ({ContentType}, {Size} bytes)",
                context.AssetId,
                context.ContentType,
                context.SizeBytes);

            // Validate first
            var validation = await ValidateAsync(context, cancellationToken);
            if (!validation.IsValid)
            {
                return AssetProcessingResult.Failed(
                    validation.ErrorMessage ?? "Validation failed",
                    validation.ErrorCode,
                    stopwatch.ElapsedMilliseconds);
            }

            // Get processing options
            var compressionEnabled = GetProcessingOption(context, "compression_enabled", true);
            var maxDimension = GetProcessingOption(context, "max_dimension", 4096);
            var targetFormat = GetProcessingOption(context, "target_format", "webp");

            // TODO: Implement actual texture processing using SkiaSharp or ImageSharp
            // For now, we do pass-through processing with metadata extraction
            var processedKey = $"processed/{context.AssetId}/{context.Filename}";
            var bucket = _configuration.StorageBucket;

            // Copy the asset to the processed location
            await _storageProvider.CopyObjectAsync(
                bucket,
                context.StorageKey,
                bucket,
                processedKey);

            // Get the size of the processed file
            var metadata = await _storageProvider.GetObjectMetadataAsync(bucket, processedKey);
            var processedSize = metadata?.ContentLength ?? context.SizeBytes;

            stopwatch.Stop();

            var resultMetadata = new Dictionary<string, object>
            {
                ["original_size"] = context.SizeBytes,
                ["compression_enabled"] = compressionEnabled,
                ["max_dimension"] = maxDimension,
                ["target_format"] = targetFormat
            };

            _logger.LogInformation(
                "Successfully processed texture asset {AssetId} in {Duration}ms",
                context.AssetId,
                stopwatch.ElapsedMilliseconds);

            return AssetProcessingResult.Succeeded(
                processedKey,
                processedSize,
                stopwatch.ElapsedMilliseconds,
                context.ContentType,
                resultMetadata);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to process texture asset {AssetId}",
                context.AssetId);

            return AssetProcessingResult.Failed(
                ex.Message,
                "PROCESSING_ERROR",
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static T GetProcessingOption<T>(AssetProcessingContext context, string key, T defaultValue)
    {
        if (context.ProcessingOptions == null)
        {
            return defaultValue;
        }

        if (!context.ProcessingOptions.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}
