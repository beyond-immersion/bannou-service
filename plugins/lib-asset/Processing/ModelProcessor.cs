using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Pass-through stub processor for 3D model assets.
/// Currently performs validation and copies assets unchanged to processed location.
/// Actual processing (mesh optimization, LOD generation, format conversion) not yet implemented.
/// </summary>
public sealed class ModelProcessor : IAssetProcessor
{
    private readonly IAssetStorageProvider _storageProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<ModelProcessor> _logger;
    private readonly AssetServiceConfiguration _configuration;

    private static readonly List<string> ModelContentTypes = new()
    {
        "model/gltf+json",
        "model/gltf-binary",
        "application/octet-stream", // Common for .glb files
        "model/obj",
        "model/fbx"
    };

    private static readonly List<string> ModelExtensions = new()
    {
        ".gltf",
        ".glb",
        ".obj",
        ".fbx"
    };

    /// <inheritdoc />
    public string PoolType => _configuration.ModelProcessorPoolType;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes => ModelContentTypes;

    /// <summary>
    /// Creates a new ModelProcessor instance.
    /// </summary>
    public ModelProcessor(
        IAssetStorageProvider storageProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<ModelProcessor> logger,
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
        return ModelContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a filename has a supported model extension.
    /// </summary>
    public bool CanProcessByExtension(string filename)
    {
        var extension = Path.GetExtension(filename);
        return ModelExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<AssetValidationResult> ValidateAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "ModelProcessor.ValidateAsync");
        await Task.CompletedTask;
        var warnings = new List<string>();

        // Check if content type or extension is supported
        var isValidContentType = CanProcess(context.ContentType);
        var isValidExtension = CanProcessByExtension(context.Filename);

        if (!isValidContentType && !isValidExtension)
        {
            return AssetValidationResult.Invalid(
                $"Unsupported model format: {context.ContentType} ({context.Filename})",
                ProcessingErrorCode.UnsupportedFormat);
        }

        // Check file size limits
        var maxSizeBytes = _configuration.MaxUploadSizeMb * 1024L * 1024L;
        if (context.SizeBytes > maxSizeBytes)
        {
            return AssetValidationResult.Invalid(
                $"File size {context.SizeBytes} exceeds maximum {maxSizeBytes} bytes",
                ProcessingErrorCode.FileTooLarge);
        }

        // Check for potentially problematic scenarios
        if (context.SizeBytes > _configuration.ModelLargeFileWarningThresholdMb * 1024L * 1024L)
        {
            warnings.Add("Large model file may take significant time to process");
        }

        // Warn about binary octet-stream types that need extension validation
        if (context.ContentType == "application/octet-stream")
        {
            if (!isValidExtension)
            {
                return AssetValidationResult.Invalid(
                    "Binary file requires a supported model extension (.gltf, .glb, .obj, .fbx)",
                    ProcessingErrorCode.MissingExtension);
            }
            warnings.Add("Content type detected from extension rather than MIME type");
        }

        _logger.LogDebug(
            "Validated model asset {AssetId}: valid with {WarningCount} warnings",
            context.AssetId,
            warnings.Count);

        return AssetValidationResult.Valid(warnings.Count > 0 ? warnings : null);
    }

    /// <inheritdoc />
    public async Task<AssetProcessingResult> ProcessAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "ModelProcessor.ProcessAsync");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing model asset {AssetId} ({ContentType}, {Size} bytes)",
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
            var optimizeMeshes = GetProcessingOption(context, "optimize_meshes", true);
            var generateLods = GetProcessingOption(context, "generate_lods", true);
            var lodLevels = GetProcessingOption(context, "lod_levels", 3);

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
                ["optimize_meshes"] = optimizeMeshes,
                ["generate_lods"] = generateLods,
                ["lod_levels"] = lodLevels,
                ["format"] = Path.GetExtension(context.Filename).TrimStart('.')
            };

            _logger.LogInformation(
                "Successfully processed model asset {AssetId} in {Duration}ms",
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
                "Failed to process model asset {AssetId}",
                context.AssetId);

            return AssetProcessingResult.Failed(
                ex.Message,
                ProcessingErrorCode.ProcessingError,
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
