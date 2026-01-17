namespace BeyondImmersion.BannouService.Storage;

/// <summary>
/// Abstract storage provider for asset files.
/// Implementations: MinIO, AWS S3, Cloudflare R2, Azure Blob, Local Filesystem.
/// Business logic in the Asset Service never interacts with storage directly - only through this abstraction.
/// </summary>
public interface IAssetStorageProvider
{
    /// <summary>
    /// Generate a pre-signed URL for uploading a new asset.
    /// </summary>
    /// <param name="bucket">Target bucket name</param>
    /// <param name="key">Object key (path) within the bucket</param>
    /// <param name="contentType">MIME content type of the file</param>
    /// <param name="expectedSize">Expected file size in bytes</param>
    /// <param name="expiration">How long the URL should be valid</param>
    /// <param name="metadata">Optional metadata to attach to the object</param>
    /// <returns>Pre-signed URL and related information</returns>
    Task<PreSignedUploadResult> GenerateUploadUrlAsync(
        string bucket,
        string key,
        string contentType,
        long expectedSize,
        TimeSpan expiration,
        IDictionary<string, string>? metadata = null);

    /// <summary>
    /// Generate a pre-signed URL for downloading an asset.
    /// </summary>
    /// <param name="bucket">Source bucket name</param>
    /// <param name="key">Object key (path) within the bucket</param>
    /// <param name="versionId">Optional version ID (null for latest)</param>
    /// <param name="expiration">How long the URL should be valid (null for default)</param>
    /// <returns>Pre-signed download URL and related information</returns>
    Task<PreSignedDownloadResult> GenerateDownloadUrlAsync(
        string bucket,
        string key,
        string? versionId = null,
        TimeSpan? expiration = null);

    /// <summary>
    /// Generate pre-signed URLs for multipart upload (large files).
    /// Used for files exceeding the multipart threshold.
    /// </summary>
    /// <param name="bucket">Target bucket name</param>
    /// <param name="key">Object key (path) within the bucket</param>
    /// <param name="contentType">MIME content type of the file</param>
    /// <param name="partCount">Number of parts to upload</param>
    /// <param name="partUrlExpiration">How long each part URL should be valid</param>
    /// <returns>Upload ID and pre-signed URLs for each part</returns>
    Task<MultipartUploadResult> InitiateMultipartUploadAsync(
        string bucket,
        string key,
        string contentType,
        int partCount,
        TimeSpan partUrlExpiration);

    /// <summary>
    /// Complete a multipart upload after all parts are uploaded.
    /// </summary>
    /// <param name="bucket">Target bucket name</param>
    /// <param name="key">Object key (path) within the bucket</param>
    /// <param name="uploadId">The upload ID from InitiateMultipartUploadAsync</param>
    /// <param name="parts">List of completed parts with ETags</param>
    /// <returns>Reference to the completed object</returns>
    Task<AssetReference> CompleteMultipartUploadAsync(
        string bucket,
        string key,
        string uploadId,
        IList<StorageCompletedPart> parts);

    /// <summary>
    /// Abort a multipart upload (cleanup on failure).
    /// </summary>
    /// <param name="bucket">Target bucket name</param>
    /// <param name="key">Object key (path) within the bucket</param>
    /// <param name="uploadId">The upload ID to abort</param>
    Task AbortMultipartUploadAsync(
        string bucket,
        string key,
        string uploadId);

    /// <summary>
    /// Copy an object within storage (for archival, versioning).
    /// </summary>
    /// <param name="sourceBucket">Source bucket name</param>
    /// <param name="sourceKey">Source object key</param>
    /// <param name="destBucket">Destination bucket name</param>
    /// <param name="destKey">Destination object key</param>
    /// <param name="sourceVersionId">Optional source version ID</param>
    /// <returns>Reference to the copied object</returns>
    Task<AssetReference> CopyObjectAsync(
        string sourceBucket,
        string sourceKey,
        string destBucket,
        string destKey,
        string? sourceVersionId = null);

    /// <summary>
    /// Delete an object or specific version.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="key">Object key</param>
    /// <param name="versionId">Optional version ID (null deletes latest/all)</param>
    Task DeleteObjectAsync(
        string bucket,
        string key,
        string? versionId = null);

    /// <summary>
    /// List all versions of an object.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="keyPrefix">Key prefix to list versions for</param>
    /// <returns>List of version information</returns>
    Task<IList<ObjectVersionInfo>> ListVersionsAsync(
        string bucket,
        string keyPrefix);

    /// <summary>
    /// Get object metadata without downloading content.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="key">Object key</param>
    /// <param name="versionId">Optional version ID</param>
    /// <returns>Object metadata</returns>
    Task<ObjectMetadata> GetObjectMetadataAsync(
        string bucket,
        string key,
        string? versionId = null);

    /// <summary>
    /// Check if an object exists.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="key">Object key</param>
    /// <param name="versionId">Optional version ID</param>
    /// <returns>True if object exists</returns>
    Task<bool> ObjectExistsAsync(
        string bucket,
        string key,
        string? versionId = null);

    /// <summary>
    /// Check if provider supports a capability.
    /// </summary>
    /// <param name="capability">The capability to check</param>
    /// <returns>True if the provider supports the capability</returns>
    bool SupportsCapability(StorageCapability capability);

    /// <summary>
    /// Get the storage provider name for logging/diagnostics.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Get an object's content as a stream.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="key">Object key</param>
    /// <param name="versionId">Optional version ID</param>
    /// <returns>Stream containing the object data</returns>
    Task<Stream> GetObjectAsync(
        string bucket,
        string key,
        string? versionId = null);

    /// <summary>
    /// Put an object from a stream.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="key">Object key</param>
    /// <param name="content">Stream containing the object data</param>
    /// <param name="contentLength">Length of the content</param>
    /// <param name="contentType">MIME content type</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>Reference to the created object</returns>
    Task<AssetReference> PutObjectAsync(
        string bucket,
        string key,
        Stream content,
        long contentLength,
        string contentType,
        IDictionary<string, string>? metadata = null);

    // === Server-Side Multipart Upload Methods ===
    // These methods support streaming large files directly through the service
    // without buffering the entire file in memory.

    /// <summary>
    /// Initiates a server-side multipart upload.
    /// Unlike client-side multipart (which generates pre-signed URLs),
    /// server-side uploads stream parts directly through the service.
    /// </summary>
    /// <param name="bucket">Target bucket name</param>
    /// <param name="key">Object key (path) for the final object</param>
    /// <param name="contentType">MIME content type of the final object</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Session information for tracking the upload</returns>
    Task<ServerMultipartUploadSession> InitiateServerMultipartUploadAsync(
        string bucket,
        string key,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a single part in a server-side multipart upload.
    /// Each part (except the last) must be at least 5MB per S3 specification.
    /// </summary>
    /// <param name="session">The upload session from InitiateServerMultipartUploadAsync</param>
    /// <param name="partNumber">1-based part number (1 to 10,000)</param>
    /// <param name="content">Stream containing the part data</param>
    /// <param name="contentLength">Length of the part data in bytes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Uploaded part information including ETag</returns>
    Task<ServerUploadedPart> UploadPartAsync(
        ServerMultipartUploadSession session,
        int partNumber,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a server-side multipart upload by assembling all parts.
    /// </summary>
    /// <param name="session">The upload session</param>
    /// <param name="parts">List of uploaded parts in order</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Reference to the completed object</returns>
    Task<AssetReference> CompleteServerMultipartUploadAsync(
        ServerMultipartUploadSession session,
        IList<ServerUploadedPart> parts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts a server-side multipart upload, cleaning up any uploaded parts.
    /// </summary>
    /// <param name="session">The upload session to abort</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AbortServerMultipartUploadAsync(
        ServerMultipartUploadSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an object's content as a streaming download without buffering to memory.
    /// The returned stream reads directly from the network connection.
    /// Caller must dispose the stream when done.
    /// </summary>
    /// <remarks>
    /// Unlike GetObjectAsync which buffers the entire object to MemoryStream,
    /// this method returns a stream that reads directly from the network.
    /// Use this for large objects to avoid memory pressure.
    /// Note: The stream may not support seeking depending on the storage provider.
    /// </remarks>
    /// <param name="bucket">Bucket name</param>
    /// <param name="key">Object key</param>
    /// <param name="versionId">Optional version ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream for reading object data directly from network</returns>
    Task<Stream> GetObjectStreamingAsync(
        string bucket,
        string key,
        string? versionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Storage capabilities that may or may not be supported by providers.
/// </summary>
public enum StorageCapability
{
    /// <summary>Object versioning support</summary>
    Versioning,

    /// <summary>Multipart upload for large files</summary>
    MultipartUpload,

    /// <summary>Webhook/event notifications on object changes</summary>
    EventNotifications,

    /// <summary>Object locking (WORM compliance)</summary>
    ObjectLocking,

    /// <summary>Server-side encryption</summary>
    ServerSideEncryption,

    /// <summary>Object tagging support</summary>
    ObjectTagging,

    /// <summary>Pre-signed URL generation</summary>
    PreSignedUrls,

    /// <summary>Custom metadata on objects</summary>
    CustomMetadata
}
