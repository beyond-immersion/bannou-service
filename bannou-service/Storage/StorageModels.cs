namespace BeyondImmersion.BannouService.Storage;

/// <summary>
/// Result of generating a pre-signed upload URL.
/// </summary>
/// <param name="UploadUrl">The pre-signed URL for uploading</param>
/// <param name="Key">The object key (path) in storage</param>
/// <param name="ExpiresAt">When the URL expires</param>
/// <param name="RequiredHeaders">Headers that must be included in the upload request</param>
public record PreSignedUploadResult(
    string UploadUrl,
    string Key,
    DateTime ExpiresAt,
    IDictionary<string, string> RequiredHeaders);

/// <summary>
/// Result of generating a pre-signed download URL.
/// </summary>
/// <param name="DownloadUrl">The pre-signed URL for downloading</param>
/// <param name="Key">The object key (path) in storage</param>
/// <param name="VersionId">The version ID if versioning is enabled</param>
/// <param name="ExpiresAt">When the URL expires</param>
/// <param name="ContentLength">Size of the object in bytes</param>
/// <param name="ContentType">MIME type of the object</param>
public record PreSignedDownloadResult(
    string DownloadUrl,
    string Key,
    string? VersionId,
    DateTime ExpiresAt,
    long? ContentLength,
    string? ContentType);

/// <summary>
/// Result of initiating a multipart upload.
/// </summary>
/// <param name="UploadId">Unique identifier for this multipart upload</param>
/// <param name="Key">The object key (path) in storage</param>
/// <param name="Parts">Pre-signed URLs for each part</param>
/// <param name="ExpiresAt">When the part URLs expire</param>
public record MultipartUploadResult(
    string UploadId,
    string Key,
    IList<PartUploadInfo> Parts,
    DateTime ExpiresAt);

/// <summary>
/// Information for uploading a single part in a multipart upload.
/// </summary>
/// <param name="PartNumber">1-based part number</param>
/// <param name="UploadUrl">Pre-signed URL for uploading this part</param>
/// <param name="MinSize">Minimum size for this part (except last part)</param>
/// <param name="MaxSize">Maximum size for this part</param>
public record PartUploadInfo(
    int PartNumber,
    string UploadUrl,
    long MinSize,
    long MaxSize);

/// <summary>
/// Information about a completed part for finalizing multipart upload.
/// </summary>
/// <param name="PartNumber">1-based part number</param>
/// <param name="ETag">ETag returned by storage provider after part upload</param>
public record StorageCompletedPart(
    int PartNumber,
    string ETag);

/// <summary>
/// Reference to a stored object.
/// </summary>
/// <param name="Bucket">The bucket containing the object</param>
/// <param name="Key">The object key (path)</param>
/// <param name="VersionId">Version ID if versioning is enabled</param>
/// <param name="ETag">Entity tag (content hash)</param>
/// <param name="Size">Size in bytes</param>
/// <param name="LastModified">When the object was last modified</param>
public record AssetReference(
    string Bucket,
    string Key,
    string? VersionId,
    string ETag,
    long Size,
    DateTime LastModified);

/// <summary>
/// Information about a specific version of an object.
/// </summary>
/// <param name="VersionId">The version identifier</param>
/// <param name="IsLatest">Whether this is the current version</param>
/// <param name="LastModified">When this version was created</param>
/// <param name="Size">Size of this version in bytes</param>
/// <param name="ETag">Entity tag (content hash) for this version</param>
/// <param name="IsDeleteMarker">Whether this version is a delete marker</param>
/// <param name="StorageClass">Storage tier (e.g., STANDARD, GLACIER, DEEP_ARCHIVE)</param>
public record ObjectVersionInfo(
    string VersionId,
    bool IsLatest,
    DateTime LastModified,
    long Size,
    string ETag,
    bool IsDeleteMarker,
    string? StorageClass = null)
{
    /// <summary>
    /// Returns true if the object is in an archival storage tier (GLACIER, DEEP_ARCHIVE, etc.)
    /// </summary>
    public bool IsArchived => StorageClass != null &&
        (StorageClass.Contains("GLACIER", StringComparison.OrdinalIgnoreCase) ||
        StorageClass.Contains("ARCHIVE", StringComparison.OrdinalIgnoreCase) ||
        StorageClass.Contains("COLD", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Metadata about a stored object.
/// </summary>
/// <param name="Key">The object key (path)</param>
/// <param name="VersionId">Version ID if versioning is enabled</param>
/// <param name="ContentType">MIME type of the object</param>
/// <param name="ContentLength">Size in bytes</param>
/// <param name="ETag">Entity tag (content hash)</param>
/// <param name="LastModified">When the object was last modified</param>
/// <param name="Metadata">Custom metadata attached to the object</param>
public record ObjectMetadata(
    string Key,
    string? VersionId,
    string ContentType,
    long ContentLength,
    string ETag,
    DateTime LastModified,
    IDictionary<string, string> Metadata);

/// <summary>
/// Session information for server-side multipart uploads.
/// Unlike client-side multipart uploads (which use pre-signed URLs),
/// server-side uploads stream data directly through the service to storage.
/// Used for streaming large bundle assembly without buffering in memory.
/// </summary>
/// <param name="UploadId">The S3/MinIO upload ID for this multipart session</param>
/// <param name="Bucket">Target bucket for the upload</param>
/// <param name="Key">Object key (path) being uploaded</param>
/// <param name="ContentType">MIME content type of the final object</param>
/// <param name="InitiatedAt">When this upload session was created</param>
public record ServerMultipartUploadSession(
    string UploadId,
    string Bucket,
    string Key,
    string ContentType,
    DateTime InitiatedAt);

/// <summary>
/// Information about a server-side uploaded part.
/// </summary>
/// <param name="PartNumber">1-based part number</param>
/// <param name="ETag">ETag returned by storage for this part</param>
/// <param name="Size">Size of this part in bytes</param>
public record ServerUploadedPart(
    int PartNumber,
    string ETag,
    long Size);
