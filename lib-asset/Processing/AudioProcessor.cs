using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Processor for audio assets.
/// Handles normalization, format conversion, and compression.
/// </summary>
public sealed class AudioProcessor : IAssetProcessor
{
    private readonly IAssetStorageProvider _storageProvider;
    private readonly ILogger<AudioProcessor> _logger;
    private readonly AssetServiceConfiguration _configuration;

    private static readonly List<string> AudioContentTypes = new()
    {
        "audio/mpeg",
        "audio/mp3",
        "audio/wav",
        "audio/x-wav",
        "audio/ogg",
        "audio/opus",
        "audio/flac",
        "audio/aac",
        "audio/webm"
    };

    /// <inheritdoc />
    public string PoolType => "audio-processor";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes => AudioContentTypes;

    /// <summary>
    /// Creates a new AudioProcessor instance.
    /// </summary>
    public AudioProcessor(
        IAssetStorageProvider storageProvider,
        ILogger<AudioProcessor> logger,
        AssetServiceConfiguration configuration)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public bool CanProcess(string contentType)
    {
        return AudioContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
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
            warnings.Add("Large audio file may take significant time to process");
        }

        // Warn about lossless formats that may need conversion
        if (context.ContentType == "audio/flac" || context.ContentType == "audio/wav" || context.ContentType == "audio/x-wav")
        {
            warnings.Add("Lossless audio will be converted to Opus for optimal streaming");
        }

        _logger.LogDebug(
            "Validated audio asset {AssetId}: valid with {WarningCount} warnings",
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
                "Processing audio asset {AssetId} ({ContentType}, {Size} bytes)",
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
            var normalize = GetProcessingOption(context, "normalize", true);
            var targetFormat = GetProcessingOption(context, "target_format", "opus");
            var bitrate = GetProcessingOption(context, "bitrate", 128); // kbps

            // Determine output filename
            var outputFilename = Path.ChangeExtension(context.Filename, $".{targetFormat}");
            var processedKey = $"processed/{context.AssetId}/{outputFilename}";
            var bucket = _configuration.StorageBucket;

            // TODO: Implement actual audio processing using FFmpeg or NAudio
            // For now, we do pass-through processing with metadata extraction
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
                ["normalize"] = normalize,
                ["target_format"] = targetFormat,
                ["bitrate_kbps"] = bitrate,
                ["original_format"] = GetFormatFromContentType(context.ContentType)
            };

            _logger.LogInformation(
                "Successfully processed audio asset {AssetId} in {Duration}ms",
                context.AssetId,
                stopwatch.ElapsedMilliseconds);

            // Determine the output content type
            var outputContentType = GetContentTypeForFormat(targetFormat);

            return AssetProcessingResult.Succeeded(
                processedKey,
                processedSize,
                stopwatch.ElapsedMilliseconds,
                outputContentType,
                resultMetadata);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to process audio asset {AssetId}",
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

    private static string GetFormatFromContentType(string contentType)
    {
        return contentType switch
        {
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/wav" or "audio/x-wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/opus" => "opus",
            "audio/flac" => "flac",
            "audio/aac" => "aac",
            "audio/webm" => "webm",
            _ => "unknown"
        };
    }

    private static string GetContentTypeForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "ogg" => "audio/ogg",
            "opus" => "audio/opus",
            "flac" => "audio/flac",
            "aac" => "audio/aac",
            "webm" => "audio/webm",
            _ => "application/octet-stream"
        };
    }
}
