namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Error codes for asset processing operations.
/// Used by <see cref="AssetProcessingResult"/> and <see cref="AssetValidationResult"/>
/// to provide structured error categorization.
/// </summary>
public enum ProcessorError
{
    /// <summary>
    /// Content type is not supported by this processor.
    /// </summary>
    UnsupportedContentType,

    /// <summary>
    /// Model format is not supported (content type and extension both invalid).
    /// </summary>
    UnsupportedFormat,

    /// <summary>
    /// File exceeds the maximum allowed size.
    /// </summary>
    FileTooLarge,

    /// <summary>
    /// Binary file requires a supported extension for format detection.
    /// </summary>
    MissingExtension,

    /// <summary>
    /// Source file was not found in storage.
    /// </summary>
    SourceNotFound,

    /// <summary>
    /// Transcoding operation failed (FFmpeg or similar).
    /// </summary>
    TranscodingFailed,

    /// <summary>
    /// General processing error (unexpected exception).
    /// </summary>
    ProcessingError
}

/// <summary>
/// Interface for asset processing operations.
/// Implementations handle specific asset types (textures, models, audio).
/// </summary>
public interface IAssetProcessor
{
    /// <summary>
    /// Gets the processor pool type this processor handles.
    /// </summary>
    string PoolType { get; }

    /// <summary>
    /// Gets the supported content types for this processor.
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>
    /// Determines if this processor can handle the specified content type.
    /// </summary>
    /// <param name="contentType">The MIME type of the asset.</param>
    /// <returns>True if the processor can handle this content type.</returns>
    bool CanProcess(string contentType);

    /// <summary>
    /// Processes an asset file.
    /// </summary>
    /// <param name="context">The processing context containing asset information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the processing operation.</returns>
    Task<AssetProcessingResult> ProcessAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an asset before processing.
    /// </summary>
    /// <param name="context">The processing context containing asset information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result indicating if the asset can be processed.</returns>
    Task<AssetValidationResult> ValidateAsync(
        AssetProcessingContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for asset processing operations.
/// </summary>
public sealed class AssetProcessingContext
{
    /// <summary>
    /// The unique identifier of the asset being processed.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// The storage key where the asset is located.
    /// </summary>
    public required string StorageKey { get; init; }

    /// <summary>
    /// The MIME type of the asset.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// The size of the asset in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// The original filename of the asset.
    /// </summary>
    public required string Filename { get; init; }

    /// <summary>
    /// Owner of this processing operation. NOT a session ID.
    /// Contains either an accountId (UUID format) for user-initiated processing
    /// or a service name for service-initiated processing. Null when the dispatched
    /// event does not include owner information.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Optional realm-specific context for the asset.
    /// </summary>
    public Guid? RealmId { get; init; }

    /// <summary>
    /// Optional metadata tags associated with the asset.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Processing options specific to the asset type.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ProcessingOptions { get; init; }
}

/// <summary>
/// Result of an asset processing operation.
/// </summary>
public sealed class AssetProcessingResult
{
    /// <summary>
    /// Whether the processing was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The processed storage key (may differ from input if asset was transformed).
    /// </summary>
    public string? ProcessedStorageKey { get; init; }

    /// <summary>
    /// The new size in bytes after processing.
    /// </summary>
    public long? ProcessedSizeBytes { get; init; }

    /// <summary>
    /// The new content type if it changed during processing.
    /// </summary>
    public string? ProcessedContentType { get; init; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code if processing failed.
    /// </summary>
    public ProcessorError? ErrorCode { get; init; }

    /// <summary>
    /// Time taken to process the asset in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>
    /// Additional metadata generated during processing.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static AssetProcessingResult Succeeded(
        string processedStorageKey,
        long processedSizeBytes,
        long processingTimeMs,
        string? processedContentType = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        return new AssetProcessingResult
        {
            Success = true,
            ProcessedStorageKey = processedStorageKey,
            ProcessedSizeBytes = processedSizeBytes,
            ProcessedContentType = processedContentType,
            ProcessingTimeMs = processingTimeMs,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="errorMessage">Human-readable error description.</param>
    /// <param name="errorCode">Structured error code for categorization.</param>
    /// <param name="processingTimeMs">Time spent before failure in milliseconds.</param>
    public static AssetProcessingResult Failed(
        string errorMessage,
        ProcessorError? errorCode = null,
        long processingTimeMs = 0)
    {
        return new AssetProcessingResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            ProcessingTimeMs = processingTimeMs
        };
    }
}

/// <summary>
/// Result of asset validation.
/// </summary>
public sealed class AssetValidationResult
{
    /// <summary>
    /// Whether the asset is valid for processing.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code if validation failed.
    /// </summary>
    public ProcessorError? ErrorCode { get; init; }

    /// <summary>
    /// Validation warnings that don't prevent processing.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    public static AssetValidationResult Valid(IReadOnlyList<string>? warnings = null)
    {
        return new AssetValidationResult
        {
            IsValid = true,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    /// <param name="errorMessage">Human-readable validation failure description.</param>
    /// <param name="errorCode">Structured error code for categorization.</param>
    public static AssetValidationResult Invalid(string errorMessage, ProcessorError? errorCode = null)
    {
        return new AssetValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }
}
