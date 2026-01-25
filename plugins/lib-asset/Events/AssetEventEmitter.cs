using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.BannouService.ClientEvents;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Events;

/// <summary>
/// Implementation of IAssetEventEmitter that uses IClientEventPublisher
/// to emit asset events to client sessions via RabbitMQ pub/sub.
/// </summary>
public class AssetEventEmitter : IAssetEventEmitter
{
    private readonly IClientEventPublisher _publisher;
    private readonly ILogger<AssetEventEmitter> _logger;

    /// <summary>
    /// Creates a new AssetEventEmitter.
    /// </summary>
    /// <param name="publisher">Client event publisher.</param>
    /// <param name="logger">Logger.</param>
    public AssetEventEmitter(
        IClientEventPublisher publisher,
        ILogger<AssetEventEmitter> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> EmitUploadCompleteAsync(
        string sessionId,
        Guid uploadId,
        bool success,
        string? assetId = null,
        string? contentHash = null,
        long? size = null,
        UploadErrorCode? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new AssetUploadCompleteEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            UploadId = uploadId,
            Success = success,
            AssetId = assetId,
            ContentHash = contentHash,
            Size = size,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

        _logger.LogDebug(
            "Emitting upload complete event: uploadId={UploadId}, success={Success}, assetId={AssetId}",
            uploadId, success, assetId);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EmitProcessingCompleteAsync(
        string sessionId,
        string assetId,
        bool success,
        ProcessingType? processingType = null,
        ICollection<ProcessingOutput>? outputs = null,
        ProcessingErrorCode? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new AssetProcessingCompleteEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AssetId = assetId,
            Success = success,
            ProcessingType = processingType,
            Outputs = outputs,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

        _logger.LogDebug(
            "Emitting processing complete event: assetId={AssetId}, success={Success}",
            assetId, success);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EmitProcessingFailedAsync(
        string sessionId,
        string assetId,
        ProcessingErrorCode? errorCode = null,
        string? errorMessage = null,
        bool retryAvailable = true,
        int? retryAfterMs = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new AssetProcessingFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AssetId = assetId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            RetryAvailable = retryAvailable,
            RetryAfterMs = retryAfterMs
        };

        _logger.LogDebug(
            "Emitting processing failed event: assetId={AssetId}, errorCode={ErrorCode}",
            assetId, errorCode);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EmitBundleValidationCompleteAsync(
        string sessionId,
        Guid uploadId,
        bool success,
        Guid? bundleId = null,
        int? assetsRegistered = null,
        int? duplicatesSkipped = null,
        ICollection<ValidationWarning>? warnings = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new BundleValidationCompleteEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            UploadId = uploadId,
            Success = success,
            BundleId = bundleId,
            AssetsRegistered = assetsRegistered,
            DuplicatesSkipped = duplicatesSkipped,
            Warnings = warnings
        };

        _logger.LogDebug(
            "Emitting bundle validation complete event: uploadId={UploadId}, success={Success}, bundleId={BundleId}",
            uploadId, success, bundleId);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EmitBundleValidationFailedAsync(
        string sessionId,
        Guid uploadId,
        ICollection<ValidationError> errors,
        CancellationToken cancellationToken = default)
    {
        var eventData = new BundleValidationFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            UploadId = uploadId,
            Errors = errors
        };

        _logger.LogDebug(
            "Emitting bundle validation failed event: uploadId={UploadId}, errorCount={ErrorCount}",
            uploadId, errors.Count);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EmitBundleCreationCompleteAsync(
        string sessionId,
        Guid bundleId,
        bool success,
        Uri? downloadUrl = null,
        long? size = null,
        int? assetCount = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new BundleCreationCompleteEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            BundleId = bundleId,
            Success = success,
            DownloadUrl = downloadUrl,
            Size = size,
            AssetCount = assetCount,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

        _logger.LogDebug(
            "Emitting bundle creation complete event: bundleId={BundleId}, success={Success}",
            bundleId, success);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EmitAssetReadyAsync(
        string sessionId,
        string assetId,
        string? versionId = null,
        string? contentHash = null,
        long? size = null,
        string? contentType = null,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new AssetReadyEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AssetId = assetId,
            VersionId = versionId,
            ContentHash = contentHash,
            Size = size,
            ContentType = contentType,
            Metadata = metadata
        };

        _logger.LogDebug(
            "Emitting asset ready event: assetId={AssetId}, versionId={VersionId}",
            assetId, versionId);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EmitMetabundleCreationCompleteAsync(
        string sessionId,
        Guid jobId,
        Guid metabundleId,
        bool success,
        MetabundleJobStatus? status = null,
        Uri? downloadUrl = null,
        long? sizeBytes = null,
        int? assetCount = null,
        int? standaloneAssetCount = null,
        long? processingTimeMs = null,
        MetabundleErrorCode? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new MetabundleCreationCompleteEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JobId = jobId,
            MetabundleId = metabundleId,
            Success = success,
            Status = status,
            DownloadUrl = downloadUrl,
            SizeBytes = sizeBytes,
            AssetCount = assetCount,
            StandaloneAssetCount = standaloneAssetCount,
            ProcessingTimeMs = processingTimeMs,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

        _logger.LogDebug(
            "Emitting metabundle creation complete event: jobId={JobId}, metabundleId={MetabundleId}, success={Success}",
            jobId, metabundleId, success);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }
}
