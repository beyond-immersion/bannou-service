using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Asset;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Upload;

/// <summary>
/// Uploads bundles to Bannou Asset Service.
/// Supports single-part and multipart uploads, RequiredHeaders for MinIO pre-signed URLs,
/// and manual HTTP login for environments without auto-redirect.
/// </summary>
public sealed class BannouUploader : IAsyncDisposable
{
    private readonly UploaderOptions _options;
    private readonly ILogger<BannouUploader>? _logger;
    private readonly HttpClient _httpClient;
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
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(_options.UploadTimeoutMs)
        };
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
            // Service token auth - direct WebSocket connection
            var wsUrl = ToWebSocketUrl(_options.ServerUrl);
            result = await _client.ConnectWithTokenAsync(wsUrl, _options.ServiceToken, cancellationToken: ct);
        }
        else if (!string.IsNullOrEmpty(_options.Email) && !string.IsNullOrEmpty(_options.Password))
        {
            if (_options.UseManualLogin)
            {
                // Manual HTTP login flow - useful for servers without auto-redirect
                result = await ConnectWithManualLoginAsync(ct);
            }
            else
            {
                // Standard BannouClient login
                result = await _client.ConnectAsync(_options.ServerUrl, _options.Email, _options.Password, ct);
            }
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
    /// <param name="progress">Optional progress callback for multipart uploads.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Upload result with assigned IDs.</returns>
    public async Task<UploadResult> UploadAsync(
        string bundlePath,
        string bundleId,
        IProgress<UploadProgress>? progress = null,
        CancellationToken ct = default)
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
            Filename = fileInfo.Name,
            Size = fileInfo.Length,
            Owner = _options.Owner
        };

        var uploadApiResponse = await _client.InvokeAsync<BundleUploadRequest, UploadResponse>(
            "POST", "/bundles/upload/request", uploadRequest, cancellationToken: ct);

        if (!uploadApiResponse.IsSuccess || uploadApiResponse.Result == null)
        {
            var errorCode = uploadApiResponse.Error?.ResponseCode ?? 500;
            var errorMessage = uploadApiResponse.Error?.Message ?? "Unknown error";
            throw new InvalidOperationException($"Failed to request upload URL: {errorCode} - {errorMessage}");
        }

        var uploadResponse = uploadApiResponse.Result;

        if (_options.VerboseLogging)
        {
            _logger?.LogDebug("Received upload response: UploadId={UploadId}, Multipart={MultipartRequired}",
                uploadResponse.UploadId,
                uploadResponse.Multipart?.Required ?? false);

            if (uploadResponse.RequiredHeaders?.Count > 0)
            {
                _logger?.LogDebug("Required headers: {Headers}",
                    string.Join(", ", uploadResponse.RequiredHeaders.Select(h => $"{h.Key}={h.Value}")));
            }
        }

        // Choose upload strategy
        await using var fileStream = fileInfo.OpenRead();

        if (uploadResponse.Multipart?.Required == true)
        {
            _logger?.LogDebug("Using multipart upload for {BundleId}", bundleId);
            await UploadMultipartAsync(fileStream, uploadResponse.Multipart, progress, ct);
        }
        else
        {
            _logger?.LogDebug("Using single-part upload for {BundleId}", bundleId);
            await UploadSinglePartAsync(fileStream, uploadResponse, ct);
        }

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
        _httpClient.Dispose();
        _connected = false;
    }

    private async Task<bool> ConnectWithManualLoginAsync(CancellationToken ct)
    {
        if (_client == null)
            throw new InvalidOperationException("Client not initialized");

        var httpUrl = ToHttpUrl(_options.ServerUrl);
        var loginUrl = httpUrl.TrimEnd('/') + "/auth/login";

        _logger?.LogDebug("Performing manual HTTP login at {LoginUrl}", loginUrl);

        try
        {
            var loginRequest = new { email = _options.Email, password = _options.Password };
            var response = await _httpClient.PostAsJsonAsync(loginUrl, loginRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger?.LogError("Manual login failed ({StatusCode}): {ErrorContent}",
                    response.StatusCode, errorContent);
                return false;
            }

            var loginResult = await response.Content.ReadFromJsonAsync<LoginResult>(ct);
            if (loginResult == null || string.IsNullOrEmpty(loginResult.AccessToken))
            {
                _logger?.LogError("Login response missing access token");
                return false;
            }

            _logger?.LogDebug("Manual login successful, connecting WebSocket with token");

            // Convert HTTP URL to WebSocket URL and connect
            var wsUrl = ToWebSocketUrl(_options.ServerUrl);

            return await _client.ConnectWithTokenAsync(
                wsUrl,
                loginResult.AccessToken,
                loginResult.RefreshToken,
                ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Manual login exception");
            return false;
        }
    }

    private async Task UploadSinglePartAsync(
        FileStream fileStream,
        UploadResponse uploadResponse,
        CancellationToken ct)
    {
        // Read entire file for single-part upload
        var fileBytes = new byte[fileStream.Length];
        await fileStream.ReadExactlyAsync(fileBytes, ct);

        using var request = new HttpRequestMessage(HttpMethod.Put, uploadResponse.UploadUrl);
        request.Content = new ByteArrayContent(fileBytes);

        // Apply server-required headers
        ApplyRequiredHeaders(request, uploadResponse.RequiredHeaders);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Failed to upload to storage: {response.StatusCode} - {errorContent}");
        }
    }

    private async Task UploadMultipartAsync(
        FileStream fileStream,
        MultipartConfig multipart,
        IProgress<UploadProgress>? progress,
        CancellationToken ct)
    {
        if (multipart.UploadUrls == null || multipart.UploadUrls.Count == 0)
        {
            throw new InvalidOperationException("Multipart upload required but no upload URLs provided");
        }

        var partSize = multipart.PartSize;
        var buffer = new byte[partSize];
        var totalParts = multipart.UploadUrls.Count;
        var completedParts = 0;
        var totalBytes = fileStream.Length;
        var uploadedBytes = 0L;

        foreach (var partInfo in multipart.UploadUrls)
        {
            ct.ThrowIfCancellationRequested();

            var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, partSize), ct);
            if (bytesRead == 0)
                break;

            _logger?.LogDebug("Uploading part {PartNumber}/{TotalParts} ({BytesRead} bytes)",
                partInfo.PartNumber, totalParts, bytesRead);

            using var partContent = new ByteArrayContent(buffer, 0, bytesRead);
            var response = await _httpClient.PutAsync(partInfo.UploadUrl, partContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Failed to upload part {partInfo.PartNumber}: {response.StatusCode} - {errorContent}");
            }

            completedParts++;
            uploadedBytes += bytesRead;

            progress?.Report(new UploadProgress
            {
                CompletedParts = completedParts,
                TotalParts = totalParts,
                BytesUploaded = uploadedBytes,
                TotalBytes = totalBytes
            });
        }
    }

    private void ApplyRequiredHeaders(HttpRequestMessage request, IDictionary<string, string>? requiredHeaders)
    {
        if (requiredHeaders == null || requiredHeaders.Count == 0)
        {
            // Default content type if no headers specified
            if (request.Content != null)
            {
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-bannou-bundle");
            }
            return;
        }

        // Clear default content headers to avoid conflicts with server-required headers
        request.Content?.Headers.Clear();

        foreach (var header in requiredHeaders)
        {
            // Try to add as content header first (for Content-Type, Content-Length, etc.)
            if (request.Content != null &&
                !request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                // Fall back to request-level header
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (_options.VerboseLogging)
            {
                _logger?.LogDebug("Applied header: {HeaderKey}={HeaderValue}", header.Key, header.Value);
            }
        }
    }

    private static string ToWebSocketUrl(string url)
    {
        return url
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToHttpUrl(string url)
    {
        return url
            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Response from /auth/login endpoint.
    /// </summary>
    private sealed class LoginResult
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public string? ConnectUrl { get; set; }
    }
}

/// <summary>
/// Progress information for multipart uploads.
/// </summary>
public sealed class UploadProgress
{
    /// <summary>Number of completed parts.</summary>
    public int CompletedParts { get; init; }

    /// <summary>Total number of parts.</summary>
    public int TotalParts { get; init; }

    /// <summary>Bytes uploaded so far.</summary>
    public long BytesUploaded { get; init; }

    /// <summary>Total bytes to upload.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Upload progress percentage (0-100).</summary>
    public double PercentComplete => TotalBytes > 0 ? (double)BytesUploaded / TotalBytes * 100 : 0;
}
