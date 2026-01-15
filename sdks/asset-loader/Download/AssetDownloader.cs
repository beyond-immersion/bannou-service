using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetLoader.Download;

/// <summary>
/// Downloads assets and bundles from URLs with progress reporting and retry logic.
/// </summary>
public sealed class AssetDownloader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly DownloadOptions _options;
    private readonly ILogger? _logger;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new asset downloader with default HTTP client.
    /// </summary>
    public AssetDownloader(DownloadOptions? options = null, ILogger? logger = null)
        : this(new HttpClient(), options, logger)
    {
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a new asset downloader with custom HTTP client.
    /// </summary>
    public AssetDownloader(HttpClient httpClient, DownloadOptions? options = null, ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new DownloadOptions();
        _logger = logger;
        _ownsHttpClient = false;

        if (!string.IsNullOrEmpty(_options.UserAgent))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        }
    }

    /// <summary>
    /// Downloads data from a URL with progress reporting.
    /// </summary>
    /// <param name="url">URL to download from.</param>
    /// <param name="bundleId">Bundle ID for progress reporting.</param>
    /// <param name="expectedHash">Expected content hash for verification.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Download result with data stream and metadata.</returns>
    public async Task<DownloadResult> DownloadAsync(
        Uri url,
        string bundleId,
        string? expectedHash = null,
        IProgress<BundleDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        var attempt = 0;
        Exception? lastException = null;

        while (attempt < _options.MaxRetries)
        {
            attempt++;

            try
            {
                return await DownloadCoreAsync(url, bundleId, expectedHash, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger?.LogWarning(ex, "Download attempt {Attempt}/{MaxRetries} failed for {BundleId}",
                    attempt, _options.MaxRetries, bundleId);

                if (attempt < _options.MaxRetries)
                {
                    await Task.Delay(_options.RetryDelay, ct).ConfigureAwait(false);
                }
            }
        }

        ReportProgress(progress, bundleId, DownloadPhase.Failed, 0, 0, 0);
        throw new DownloadException($"Download failed after {_options.MaxRetries} attempts", lastException);
    }

    private async Task<DownloadResult> DownloadCoreAsync(
        Uri url,
        string bundleId,
        string? expectedHash,
        IProgress<BundleDownloadProgress>? progress,
        CancellationToken ct)
    {
        ReportProgress(progress, bundleId, DownloadPhase.Starting, 0, 0, 0);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.Timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var memoryStream = new MemoryStream(totalBytes > 0 ? (int)totalBytes : 1024 * 1024);

        await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);

        var buffer = new byte[_options.BufferSize];
        var bytesDownloaded = 0L;
        var stopwatch = Stopwatch.StartNew();
        var lastReportTime = stopwatch.Elapsed;
        var lastReportBytes = 0L;

        while (true)
        {
            var bytesRead = await contentStream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
            if (bytesRead == 0)
                break;

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token).ConfigureAwait(false);
            bytesDownloaded += bytesRead;

            // Report progress periodically (at most every 100ms)
            var elapsed = stopwatch.Elapsed;
            if (elapsed - lastReportTime >= TimeSpan.FromMilliseconds(100))
            {
                var bytesPerSecond = (long)((bytesDownloaded - lastReportBytes) / (elapsed - lastReportTime).TotalSeconds);
                ReportProgress(progress, bundleId, DownloadPhase.Downloading, bytesDownloaded, totalBytes, bytesPerSecond);
                lastReportTime = elapsed;
                lastReportBytes = bytesDownloaded;
            }
        }

        var elapsedTotal = stopwatch.Elapsed;
        var avgBytesPerSecond = (long)(bytesDownloaded / elapsedTotal.TotalSeconds);
        ReportProgress(progress, bundleId, DownloadPhase.Downloading, bytesDownloaded, totalBytes, avgBytesPerSecond);

        // Verify hash if expected
        string contentHash;
        if (_options.VerifyHash || expectedHash != null)
        {
            ReportProgress(progress, bundleId, DownloadPhase.Verifying, bytesDownloaded, totalBytes, 0);

            memoryStream.Position = 0;
            contentHash = ComputeHash(memoryStream);

            if (expectedHash != null && !string.Equals(contentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new DownloadException($"Content hash mismatch: expected {expectedHash}, got {contentHash}");
            }

            memoryStream.Position = 0;
        }
        else
        {
            contentHash = string.Empty;
            memoryStream.Position = 0;
        }

        ReportProgress(progress, bundleId, DownloadPhase.Complete, bytesDownloaded, bytesDownloaded, avgBytesPerSecond);

        _logger?.LogInformation("Downloaded {BundleId}: {Bytes} bytes in {Time}ms ({Speed}/s)",
            bundleId, bytesDownloaded, (int)elapsedTotal.TotalMilliseconds, FormatBytes(avgBytesPerSecond));

        return new DownloadResult
        {
            Stream = memoryStream,
            ContentHash = contentHash,
            SizeBytes = bytesDownloaded,
            DownloadTimeMs = (long)elapsedTotal.TotalMilliseconds
        };
    }

    private static void ReportProgress(
        IProgress<BundleDownloadProgress>? progress,
        string bundleId,
        DownloadPhase phase,
        long bytesDownloaded,
        long totalBytes,
        long bytesPerSecond)
    {
        progress?.Report(new BundleDownloadProgress
        {
            BundleId = bundleId,
            Phase = phase,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = totalBytes,
            BytesPerSecond = bytesPerSecond
        });
    }

    private static string ComputeHash(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1}GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1}MB",
            >= 1024 => $"{bytes / 1024.0:F1}KB",
            _ => $"{bytes}B"
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

/// <summary>
/// Result of a download operation.
/// </summary>
public sealed class DownloadResult : IDisposable
{
    /// <summary>Stream containing downloaded data.</summary>
    public required MemoryStream Stream { get; init; }

    /// <summary>Content hash (SHA256) of downloaded data.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Size of downloaded data in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Download time in milliseconds.</summary>
    public required long DownloadTimeMs { get; init; }

    /// <inheritdoc />
    public void Dispose()
    {
        Stream.Dispose();
    }
}

/// <summary>
/// Exception thrown when a download fails.
/// </summary>
public sealed class DownloadException : Exception
{
    /// <summary>Creates a new download exception.</summary>
    public DownloadException(string message) : base(message) { }

    /// <summary>Creates a new download exception with inner exception.</summary>
    public DownloadException(string message, Exception? innerException) : base(message, innerException) { }
}
