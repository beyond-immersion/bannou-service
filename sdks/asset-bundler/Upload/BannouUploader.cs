using BeyondImmersion.Bannou.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Upload;

/// <summary>
/// Uploads bundles to Bannou Asset Service.
/// </summary>
public sealed class BannouUploader : IAsyncDisposable
{
    private readonly UploaderOptions _options;
    private readonly ILogger<BannouUploader>? _logger;
    private BannouClient? _client;
    private bool _connected;

    /// <summary>
    /// Creates a new Bannou uploader.
    /// </summary>
    /// <param name="options">Uploader configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public BannouUploader(UploaderOptions options, ILogger<BannouUploader>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>
    /// Gets whether the uploader is connected.
    /// </summary>
    public bool IsConnected => _connected && _client != null;

    /// <summary>
    /// Gets the underlying BannouClient for advanced operations.
    /// </summary>
    public BannouClient? Client => _client;

    /// <summary>
    /// Connects to Bannou service.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if connection succeeded.</returns>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (_connected)
            return true;

        _client = new BannouClient();

        bool result;
        if (!string.IsNullOrEmpty(_options.ServiceToken))
        {
            result = await _client.ConnectWithTokenAsync(_options.ServerUrl, _options.ServiceToken, cancellationToken: ct);
        }
        else if (!string.IsNullOrEmpty(_options.Email) && !string.IsNullOrEmpty(_options.Password))
        {
            result = await _client.ConnectAsync(_options.ServerUrl, _options.Email, _options.Password, ct);
        }
        else
        {
            throw new InvalidOperationException("Either ServiceToken or Email/Password must be provided");
        }

        _connected = result;

        if (result)
        {
            _logger?.LogInformation("Connected to Bannou service at {ServerUrl}", _options.ServerUrl);
        }
        else
        {
            _logger?.LogError("Failed to connect to Bannou service at {ServerUrl}", _options.ServerUrl);
        }

        return result;
    }

    /// <summary>
    /// Uploads a bundle file to Bannou Asset Service.
    /// </summary>
    /// <param name="bundlePath">Path to the bundle file.</param>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Upload result with assigned IDs.</returns>
    public async Task<UploadResult> UploadAsync(string bundlePath, string bundleId, CancellationToken ct = default)
    {
        if (_client == null || !_connected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        var fileInfo = new FileInfo(bundlePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Bundle file not found", bundlePath);

        _logger?.LogDebug(
            "Requesting upload URL for bundle {BundleId} ({SizeBytes} bytes)",
            bundleId,
            fileInfo.Length);

        // Request upload URL via WebSocket
        var uploadRequest = new BundleUploadRequest
        {
            BundleId = bundleId,
            Filename = fileInfo.Name,
            SizeBytes = fileInfo.Length,
            Owner = _options.Owner
        };

        var uploadApiResponse = await _client.InvokeAsync<BundleUploadRequest, BundleUploadResponse>(
            "POST", "/bundles/upload/request", uploadRequest, cancellationToken: ct);

        if (!uploadApiResponse.IsSuccess || uploadApiResponse.Result == null)
        {
            var errorCode = uploadApiResponse.Error?.ResponseCode ?? 500;
            var errorMessage = uploadApiResponse.Error?.Message ?? "Unknown error";
            throw new InvalidOperationException($"Failed to request upload URL: {errorCode} - {errorMessage}");
        }

        var uploadResponse = uploadApiResponse.Result;
        _logger?.LogDebug("Received upload URL, uploading to {UploadId}...", uploadResponse.UploadId);

        // Upload file directly to pre-signed URL
        using var httpClient = new HttpClient();
        await using var fileStream = fileInfo.OpenRead();

        var content = new StreamContent(fileStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bannou-bundle");

        var response = await httpClient.PutAsync(uploadResponse.UploadUrl, content, ct);
        response.EnsureSuccessStatusCode();

        _logger?.LogDebug("Upload complete, marking upload {UploadId} as complete...", uploadResponse.UploadId);

        // Complete upload via WebSocket
        var completeRequest = new CompleteUploadRequest
        {
            UploadId = uploadResponse.UploadId
        };

        var completeApiResponse = await _client.InvokeAsync<CompleteUploadRequest, object>(
            "POST", "/assets/upload/complete", completeRequest, cancellationToken: ct);

        if (!completeApiResponse.IsSuccess)
        {
            var errorCode = completeApiResponse.Error?.ResponseCode ?? 500;
            var errorMessage = completeApiResponse.Error?.Message ?? "Unknown error";
            throw new InvalidOperationException($"Failed to complete upload: {errorCode} - {errorMessage}");
        }

        _logger?.LogInformation(
            "Successfully uploaded bundle {BundleId} ({SizeBytes} bytes)",
            bundleId,
            fileInfo.Length);

        return new UploadResult
        {
            BundleId = bundleId,
            UploadId = uploadResponse.UploadId,
            SizeBytes = fileInfo.Length
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisconnectAsync();
            _client = null;
        }
        _connected = false;
    }
}

// Internal DTOs for API calls - these match the Asset service API
internal sealed class BundleUploadRequest
{
    public required string BundleId { get; init; }
    public required string Filename { get; init; }
    public required long SizeBytes { get; init; }
    public required string Owner { get; init; }
}

internal sealed class BundleUploadResponse
{
    public required string UploadId { get; init; }
    public required string UploadUrl { get; init; }
}

internal sealed class CompleteUploadRequest
{
    public required string UploadId { get; init; }
}
