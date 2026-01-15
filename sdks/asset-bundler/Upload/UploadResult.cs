namespace BeyondImmersion.Bannou.AssetBundler.Upload;

/// <summary>
/// Result of uploading a bundle to Bannou.
/// </summary>
public sealed class UploadResult
{
    /// <summary>
    /// Bundle ID assigned by the service.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Upload ID from the upload request.
    /// </summary>
    public required string UploadId { get; init; }

    /// <summary>
    /// Size of the uploaded bundle in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// When the upload completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}
