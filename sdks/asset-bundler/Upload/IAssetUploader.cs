namespace BeyondImmersion.Bannou.AssetBundler.Upload;

/// <summary>
/// Interface for uploading bundles to a remote asset service.
/// </summary>
public interface IAssetUploader
{
    /// <summary>
    /// Gets whether the uploader is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to the asset service.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if connection succeeded.</returns>
    Task<bool> ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Uploads a bundle file to the asset service.
    /// </summary>
    /// <param name="bundlePath">Path to the bundle file.</param>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="progress">Optional progress callback for multipart uploads.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Upload result with assigned IDs.</returns>
    Task<UploadResult> UploadAsync(
        string bundlePath,
        string bundleId,
        IProgress<UploadProgress>? progress = null,
        CancellationToken ct = default);
}
