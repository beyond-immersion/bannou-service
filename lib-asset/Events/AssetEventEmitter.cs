using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.BannouService.ClientEvents;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Events;

/// <summary>
/// Implementation of IAssetEventEmitter that uses IClientEventPublisher
/// to emit asset events to client sessions via Dapr pub/sub.
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
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Event_name = AssetUploadCompleteEventEvent_name.Asset_upload_complete,
            Upload_id = uploadId,
            Success = success,
            Asset_id = assetId,
            Content_hash = contentHash,
            Size = size,
            Error_code = errorCode,
            Error_message = errorMessage
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
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Event_name = AssetProcessingCompleteEventEvent_name.Asset_processing_complete,
            Asset_id = assetId,
            Success = success,
            Processing_type = processingType,
            Outputs = outputs,
            Error_code = errorCode,
            Error_message = errorMessage
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
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Event_name = AssetProcessingFailedEventEvent_name.Asset_processing_failed,
            Asset_id = assetId,
            Error_code = errorCode,
            Error_message = errorMessage,
            Retry_available = retryAvailable,
            Retry_after_ms = retryAfterMs
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
        string? bundleId = null,
        int? assetsRegistered = null,
        int? duplicatesSkipped = null,
        ICollection<ValidationWarning>? warnings = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new BundleValidationCompleteEvent
        {
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Event_name = BundleValidationCompleteEventEvent_name.Asset_bundle_validation_complete,
            Upload_id = uploadId,
            Success = success,
            Bundle_id = bundleId,
            Assets_registered = assetsRegistered,
            Duplicates_skipped = duplicatesSkipped,
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
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Event_name = BundleValidationFailedEventEvent_name.Asset_bundle_validation_failed,
            Upload_id = uploadId,
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
        string bundleId,
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
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Event_name = BundleCreationCompleteEventEvent_name.Asset_bundle_creation_complete,
            Bundle_id = bundleId,
            Success = success,
            Download_url = downloadUrl,
            Size = size,
            Asset_count = assetCount,
            Error_code = errorCode,
            Error_message = errorMessage
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
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Event_name = AssetReadyEventEvent_name.Asset_ready,
            Asset_id = assetId,
            Version_id = versionId,
            Content_hash = contentHash,
            Size = size,
            Content_type = contentType,
            Metadata = metadata
        };

        _logger.LogDebug(
            "Emitting asset ready event: assetId={AssetId}, versionId={VersionId}",
            assetId, versionId);

        return await _publisher.PublishToSessionAsync(sessionId, eventData, cancellationToken);
    }
}
