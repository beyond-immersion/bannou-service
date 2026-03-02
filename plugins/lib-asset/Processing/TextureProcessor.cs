using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Pass-through stub processor for texture assets (images).
/// Currently performs validation and copies assets unchanged to processed location.
/// Actual processing (compression, resizing, format conversion) not yet implemented.
/// </summary>
public sealed class TextureProcessor : IAssetProcessor
{
    private readonly IAssetStorageProvider _storageProvider;
    private readonly ITelemetryProvider _telemetryProvider;
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
    public string PoolType => _configuration.TextureProcessorPoolType;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes => TextureContentTypes;

    /// <summary>
    /// Creates a new TextureProcessor instance.
    /// </summary>
    public TextureProcessor(
        IAssetStorageProvider storageProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<TextureProcessor> logger,
        AssetServiceConfiguration configuration)
    {
        _storageProvider = storageProvider;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public bool CanProcess(string contentType)
    {
        return TextureContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<AssetValidationResult> ValidateAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "TextureProcessor.ValidateAsync");
        await Task.CompletedTask;
        var warnings = new List<string>();

        // Check if content type is supported
        if (!CanProcess(context.ContentType))
        {
            return AssetValidationResult.Invalid(
                $"Unsupported content type: {context.ContentType}",
                ProcessorError.UnsupportedContentType);
        }

        // Check file size limits
        var maxSizeBytes = _configuration.MaxUploadSizeMb * 1024L * 1024L;
        if (context.SizeBytes > maxSizeBytes)
        {
            return AssetValidationResult.Invalid(
                $"File size {context.SizeBytes} exceeds maximum {maxSizeBytes} bytes",
                ProcessorError.FileTooLarge);
        }

        // Check for potentially problematic scenarios
        if (context.SizeBytes > _configuration.TextureLargeFileWarningThresholdMb * 1024L * 1024L)
        {
            warnings.Add("Large texture file may take significant time to process");
        }

        _logger.LogDebug(
            "Validated texture asset {AssetId}: valid with {WarningCount} warnings",
            context.AssetId,
            warnings.Count);

        return AssetValidationResult.Valid(warnings.Count > 0 ? warnings : null);
    }

    /// <inheritdoc />
    public async Task<AssetProcessingResult> ProcessAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "TextureProcessor.ProcessAsync");
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
            var maxDimension = GetProcessingOption(context, "max_dimension", _configuration.TextureMaxDimension);
            var targetFormat = GetProcessingOption(context, "target_format", _configuration.TextureDefaultOutputFormat);

            // Pass-through processing: copy asset unchanged to processed location.
            // Processing options are recorded in metadata for future implementation.
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
                ProcessorError.ProcessingError,
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
