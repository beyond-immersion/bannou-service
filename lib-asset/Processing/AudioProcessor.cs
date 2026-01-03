using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Processor for audio assets.
/// Handles normalization, format conversion, and compression using FFmpeg.
/// </summary>
public sealed class AudioProcessor : IAssetProcessor
{
    private readonly IAssetStorageProvider _storageProvider;
    private readonly IFFmpegService _ffmpegService;
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

    private static readonly HashSet<string> LosslessContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/wav",
        "audio/x-wav",
        "audio/flac"
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
        IFFmpegService ffmpegService,
        ILogger<AudioProcessor> logger,
        AssetServiceConfiguration configuration)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public bool CanProcess(string contentType)
    {
        return AudioContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<AssetValidationResult> ValidateAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        var warnings = new List<string>();

        // Check if content type is supported
        if (!CanProcess(context.ContentType))
        {
            return AssetValidationResult.Invalid(
                $"Unsupported content type: {context.ContentType}",
                "UNSUPPORTED_CONTENT_TYPE");
        }

        // Check file size limits
        var maxSizeBytes = _configuration.MaxUploadSizeMb * 1024L * 1024L;
        if (context.SizeBytes > maxSizeBytes)
        {
            return AssetValidationResult.Invalid(
                $"File size {context.SizeBytes} exceeds maximum {maxSizeBytes} bytes",
                "FILE_TOO_LARGE");
        }

        // Check for potentially problematic scenarios
        if (context.SizeBytes > 100 * 1024 * 1024) // > 100MB
        {
            warnings.Add("Large audio file may take significant time to process");
        }

        // Inform about lossless preservation
        if (IsLosslessFormat(context.ContentType) && _configuration.AudioPreserveLossless)
        {
            warnings.Add("Lossless original will be preserved alongside transcoded version");
        }

        _logger.LogDebug(
            "Validated audio asset {AssetId}: valid with {WarningCount} warnings",
            context.AssetId,
            warnings.Count);

        return AssetValidationResult.Valid(warnings.Count > 0 ? warnings : null);
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

            // Get processing options with configuration defaults
            var normalize = GetProcessingOption(context, "normalize", true);
            var targetFormat = GetProcessingOption(context, "target_format", _configuration.AudioOutputFormat);
            var bitrate = GetProcessingOption(context, "bitrate", _configuration.AudioBitrateKbps);

            var bucket = _configuration.StorageBucket;
            var inputFormat = GetFormatFromContentType(context.ContentType);
            var isLossless = IsLosslessFormat(context.ContentType);
            var preserveLossless = isLossless && _configuration.AudioPreserveLossless;

            // Download source file from storage
            _logger.LogDebug("Downloading source audio from {StorageKey}", context.StorageKey);
            await using var sourceStream = await _storageProvider.GetObjectAsync(bucket, context.StorageKey);
            if (sourceStream == null)
            {
                return AssetProcessingResult.Failed(
                    "Source file not found in storage",
                    "SOURCE_NOT_FOUND",
                    stopwatch.ElapsedMilliseconds);
            }

            // Copy to memory stream for processing (storage stream may not be seekable)
            using var inputStream = new MemoryStream();
            await sourceStream.CopyToAsync(inputStream, cancellationToken);
            inputStream.Position = 0;

            var outputs = new List<ProcessingOutputInfo>();

            // Preserve original lossless file if configured
            if (preserveLossless)
            {
                var originalKey = $"processed/{context.AssetId}/original.{inputFormat}";

                _logger.LogDebug("Preserving lossless original at {OriginalKey}", originalKey);

                // Reset stream position and upload original
                inputStream.Position = 0;
                await _storageProvider.PutObjectAsync(
                    bucket,
                    originalKey,
                    inputStream,
                    inputStream.Length,
                    context.ContentType);

                outputs.Add(new ProcessingOutputInfo(
                    OutputType: "original",
                    Key: originalKey,
                    Size: context.SizeBytes,
                    ContentType: context.ContentType));

                inputStream.Position = 0;
            }

            // Transcode to target format
            _logger.LogDebug(
                "Transcoding {InputFormat} -> {OutputFormat} at {Bitrate}kbps",
                inputFormat, targetFormat, bitrate);

            var ffmpegResult = await _ffmpegService.ConvertAudioAsync(
                inputStream,
                inputFormat,
                targetFormat,
                bitrate,
                normalize,
                cancellationToken);

            if (!ffmpegResult.Success || ffmpegResult.OutputStream == null)
            {
                return AssetProcessingResult.Failed(
                    ffmpegResult.ErrorMessage ?? "Transcoding failed",
                    "TRANSCODING_FAILED",
                    stopwatch.ElapsedMilliseconds);
            }

            // Upload transcoded file
            var transcodedFilename = Path.ChangeExtension(context.Filename, $".{targetFormat}");
            var transcodedKey = $"processed/{context.AssetId}/transcoded.{targetFormat}";
            var transcodedContentType = GetContentTypeForFormat(targetFormat);

            _logger.LogDebug("Uploading transcoded audio to {TranscodedKey}", transcodedKey);

            await _storageProvider.PutObjectAsync(
                bucket,
                transcodedKey,
                ffmpegResult.OutputStream,
                ffmpegResult.OutputSizeBytes,
                transcodedContentType);

            outputs.Add(new ProcessingOutputInfo(
                OutputType: "transcoded",
                Key: transcodedKey,
                Size: ffmpegResult.OutputSizeBytes,
                ContentType: transcodedContentType));

            // Dispose the output stream
            await ffmpegResult.OutputStream.DisposeAsync();

            stopwatch.Stop();

            var resultMetadata = new Dictionary<string, object>
            {
                ["original_size"] = context.SizeBytes,
                ["transcoded_size"] = ffmpegResult.OutputSizeBytes,
                ["compression_ratio"] = Math.Round((double)ffmpegResult.OutputSizeBytes / context.SizeBytes, 3),
                ["normalize"] = normalize,
                ["target_format"] = targetFormat,
                ["bitrate_kbps"] = bitrate,
                ["original_format"] = inputFormat,
                ["lossless_preserved"] = preserveLossless,
                ["outputs"] = outputs.Select(o => new Dictionary<string, object>
                {
                    ["type"] = o.OutputType,
                    ["key"] = o.Key,
                    ["size"] = o.Size,
                    ["content_type"] = o.ContentType
                }).ToList()
            };

            _logger.LogInformation(
                "Successfully processed audio asset {AssetId}: {InputFormat} -> {OutputFormat}, " +
                "{OriginalSize} -> {TranscodedSize} bytes ({Ratio:P1} of original), duration={Duration}ms",
                context.AssetId,
                inputFormat,
                targetFormat,
                context.SizeBytes,
                ffmpegResult.OutputSizeBytes,
                (double)ffmpegResult.OutputSizeBytes / context.SizeBytes,
                stopwatch.ElapsedMilliseconds);

            // Return the transcoded file as the primary output
            return AssetProcessingResult.Succeeded(
                transcodedKey,
                ffmpegResult.OutputSizeBytes,
                stopwatch.ElapsedMilliseconds,
                transcodedContentType,
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

    private static bool IsLosslessFormat(string contentType)
    {
        return LosslessContentTypes.Contains(contentType);
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

    /// <summary>
    /// Information about a processing output file.
    /// </summary>
    private record ProcessingOutputInfo(
        string OutputType,
        string Key,
        long Size,
        string ContentType);
}
