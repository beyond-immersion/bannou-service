using BeyondImmersion.Bannou.Asset.ClientEvents;

namespace BeyondImmersion.BannouService.Asset.Events;

/// <summary>
/// Interface for emitting asset-related events to client sessions.
/// Provides strongly-typed methods for each asset event type.
/// </summary>
public interface IAssetEventEmitter
{
    /// <summary>
    /// Emits an upload complete event to the specified session.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="uploadId">The upload ID.</param>
    /// <param name="success">Whether the upload succeeded.</param>
    /// <param name="assetId">The assigned asset ID (on success).</param>
    /// <param name="contentHash">SHA256 hash of the content.</param>
    /// <param name="size">File size in bytes.</param>
    /// <param name="errorCode">Error code (on failure).</param>
    /// <param name="errorMessage">Error message (on failure).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitUploadCompleteAsync(
        string sessionId,
        Guid uploadId,
        bool success,
        string? assetId = null,
        string? contentHash = null,
        long? size = null,
        UploadErrorCode? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits a processing complete event to the specified session.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="success">Whether processing succeeded.</param>
    /// <param name="processingType">Type of processing performed.</param>
    /// <param name="outputs">Generated derivative assets.</param>
    /// <param name="errorCode">Error code (on failure).</param>
    /// <param name="errorMessage">Error message (on failure).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitProcessingCompleteAsync(
        string sessionId,
        string assetId,
        bool success,
        ProcessingType? processingType = null,
        ICollection<ProcessingOutput>? outputs = null,
        ProcessingErrorCode? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits a processing failed event to the specified session.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="errorMessage">Error message.</param>
    /// <param name="retryAvailable">Whether retry is available.</param>
    /// <param name="retryAfterMs">Suggested retry delay in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitProcessingFailedAsync(
        string sessionId,
        string assetId,
        ProcessingErrorCode? errorCode = null,
        string? errorMessage = null,
        bool retryAvailable = true,
        int? retryAfterMs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits a bundle validation complete event to the specified session.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="uploadId">The upload ID.</param>
    /// <param name="success">Whether validation passed.</param>
    /// <param name="bundleId">The bundle ID (on success).</param>
    /// <param name="assetsRegistered">Number of assets registered.</param>
    /// <param name="duplicatesSkipped">Number of duplicate assets skipped.</param>
    /// <param name="warnings">Validation warnings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitBundleValidationCompleteAsync(
        string sessionId,
        Guid uploadId,
        bool success,
        string? bundleId = null,
        int? assetsRegistered = null,
        int? duplicatesSkipped = null,
        ICollection<ValidationWarning>? warnings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits a bundle validation failed event to the specified session.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="uploadId">The upload ID.</param>
    /// <param name="errors">Validation errors.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitBundleValidationFailedAsync(
        string sessionId,
        Guid uploadId,
        ICollection<ValidationError> errors,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits a bundle creation complete event to the specified session.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="bundleId">The bundle ID.</param>
    /// <param name="success">Whether creation succeeded.</param>
    /// <param name="downloadUrl">Pre-signed download URL (on success).</param>
    /// <param name="size">Bundle size in bytes.</param>
    /// <param name="assetCount">Number of assets in bundle.</param>
    /// <param name="errorCode">Error code (on failure).</param>
    /// <param name="errorMessage">Error message (on failure).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitBundleCreationCompleteAsync(
        string sessionId,
        string bundleId,
        bool success,
        Uri? downloadUrl = null,
        long? size = null,
        int? assetCount = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits an asset ready event to the specified session.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="versionId">Version ID.</param>
    /// <param name="contentHash">SHA256 hash.</param>
    /// <param name="size">File size in bytes.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="metadata">Asset metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitAssetReadyAsync(
        string sessionId,
        string assetId,
        string? versionId = null,
        string? contentHash = null,
        long? size = null,
        string? contentType = null,
        object? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits a metabundle creation complete event to the specified session.
    /// Used for async metabundle jobs to notify the requester of completion.
    /// </summary>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="jobId">The job ID.</param>
    /// <param name="metabundleId">The metabundle ID.</param>
    /// <param name="success">Whether creation succeeded.</param>
    /// <param name="status">Final job status.</param>
    /// <param name="downloadUrl">Pre-signed download URL (on success).</param>
    /// <param name="sizeBytes">Metabundle size in bytes.</param>
    /// <param name="assetCount">Total number of assets.</param>
    /// <param name="standaloneAssetCount">Number of standalone assets.</param>
    /// <param name="processingTimeMs">Processing time in milliseconds.</param>
    /// <param name="errorCode">Error code (on failure).</param>
    /// <param name="errorMessage">Error message (on failure).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully.</returns>
    Task<bool> EmitMetabundleCreationCompleteAsync(
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
        CancellationToken cancellationToken = default);
}
