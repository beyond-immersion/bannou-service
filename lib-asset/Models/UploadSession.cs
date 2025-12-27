namespace BeyondImmersion.BannouService.Asset.Models;

/// <summary>
/// Tracks the state of an upload session in the lib-state store.
/// Used by both AssetService and MinioWebhookHandler.
/// </summary>
public sealed class UploadSession
{
    /// <summary>
    /// Unique identifier for this upload session.
    /// </summary>
    public Guid UploadId { get; set; }

    /// <summary>
    /// Original filename provided by the client.
    /// </summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// Expected file size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// MIME content type of the file.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Optional metadata provided by the client.
    /// </summary>
    public AssetMetadataInput? Metadata { get; set; }

    /// <summary>
    /// Storage key where the file will be stored in MinIO.
    /// </summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a multipart upload.
    /// </summary>
    public bool IsMultipart { get; set; }

    /// <summary>
    /// Number of parts for multipart upload.
    /// </summary>
    public int PartCount { get; set; }

    /// <summary>
    /// When the upload session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the upload session expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// The ETag returned by MinIO after upload completes.
    /// Populated by webhook when upload completes.
    /// </summary>
    public string? UploadedEtag { get; set; }

    /// <summary>
    /// Actual uploaded size in bytes.
    /// Populated by webhook when upload completes.
    /// </summary>
    public long UploadedSize { get; set; }

    /// <summary>
    /// Whether the file has been uploaded to MinIO.
    /// Set to true by webhook when upload completes.
    /// </summary>
    public bool IsComplete { get; set; }
}
