using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Asset;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetLoader.Client;

/// <summary>
/// IAssetSource implementation using BannouClient (WebSocket).
/// For game clients and developer tools that connect via WebSocket.
/// </summary>
public sealed class BannouWebSocketAssetSource : IAssetSource, IAsyncDisposable
{
    private readonly BannouClient _client;
    private readonly ILogger<BannouWebSocketAssetSource>? _logger;
    private bool _ownsClient;

    /// <inheritdoc />
    public bool RequiresAuthentication => true;

    /// <inheritdoc />
    public bool IsAvailable => _client.IsConnected;

    /// <summary>
    /// Creates an asset source using an existing client connection.
    /// </summary>
    /// <param name="client">Connected BannouClient instance.</param>
    /// <param name="logger">Optional logger.</param>
    public BannouWebSocketAssetSource(BannouClient client, ILogger<BannouWebSocketAssetSource>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
        _ownsClient = false;
    }

    /// <summary>
    /// Connects to Bannou and creates an asset source.
    /// </summary>
    /// <param name="serverUrl">WebSocket server URL.</param>
    /// <param name="email">Account email.</param>
    /// <param name="password">Account password.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connected asset source.</returns>
    public static async Task<BannouWebSocketAssetSource> ConnectAsync(
        string serverUrl,
        string email,
        string password,
        ILogger<BannouWebSocketAssetSource>? logger = null,
        CancellationToken ct = default)
    {
        var client = new BannouClient();
        var connected = await client.ConnectAsync(serverUrl, email, password, ct).ConfigureAwait(false);

        if (!connected)
            throw new InvalidOperationException("Failed to connect to Bannou server");

        return new BannouWebSocketAssetSource(client, logger) { _ownsClient = true };
    }

    /// <summary>
    /// Connects to Bannou using a service token.
    /// </summary>
    /// <param name="serverUrl">WebSocket server URL.</param>
    /// <param name="serviceToken">Service authentication token.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connected asset source.</returns>
    public static async Task<BannouWebSocketAssetSource> ConnectWithTokenAsync(
        string serverUrl,
        string serviceToken,
        ILogger<BannouWebSocketAssetSource>? logger = null,
        CancellationToken ct = default)
    {
        var client = new BannouClient();
        var connected = await client.ConnectWithTokenAsync(serverUrl, serviceToken, refreshToken: null, ct).ConfigureAwait(false);

        if (!connected)
            throw new InvalidOperationException("Failed to connect to Bannou server");

        return new BannouWebSocketAssetSource(client, logger) { _ownsClient = true };
    }

    /// <inheritdoc />
    public async Task<BundleResolutionResult> ResolveBundlesAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<string>? excludeBundleIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Bannou server");

        _logger?.LogDebug("Resolving bundles for {Count} assets", assetIds.Count);

        var request = new ResolveBundlesRequest
        {
            AssetIds = assetIds.ToList(),
            ExcludeBundleIds = excludeBundleIds?.ToList()
        };

        var apiResponse = await _client.InvokeAsync<ResolveBundlesRequest, ResolveBundlesResponse>(
            "POST", "/bundles/resolve", request, cancellationToken: ct).ConfigureAwait(false);

        if (!apiResponse.IsSuccess || apiResponse.Result == null)
        {
            var errorMessage = apiResponse.Error?.Message ?? "Unknown error";
            _logger?.LogError("Failed to resolve bundles: {Error}", errorMessage);
            throw new AssetSourceException($"Failed to resolve bundles: {errorMessage}");
        }

        var response = apiResponse.Result;
        var bundles = response.Bundles?.Select(b => new ResolvedBundleInfo
        {
            BundleId = b.BundleId,
            DownloadUrl = b.DownloadUrl,
            SizeBytes = 0, // Not provided in response
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), // Default expiry
            IncludedAssetIds = b.IncludedAssetIds?.ToList() ?? new List<string>(),
            IsMetabundle = b.BundleType == BundleType.Metabundle
        }).ToList() ?? new List<ResolvedBundleInfo>();

        var standaloneAssets = response.StandaloneAssets?.Select(a => new ResolvedAssetInfo
        {
            AssetId = a.AssetId,
            DownloadUrl = a.DownloadUrl,
            SizeBytes = a.Size,
            ExpiresAt = a.ExpiresAt,
            ContentType = a.ContentType
        }).ToList() ?? new List<ResolvedAssetInfo>();

        _logger?.LogDebug("Resolved {BundleCount} bundles and {AssetCount} standalone assets",
            bundles.Count, standaloneAssets.Count);

        return new BundleResolutionResult
        {
            Bundles = bundles,
            StandaloneAssets = standaloneAssets,
            UnresolvedAssetIds = null // Server returns only resolved items
        };
    }

    /// <inheritdoc />
    public async Task<BundleDownloadInfo?> GetBundleDownloadInfoAsync(string bundleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Bannou server");

        var request = new GetBundleRequest { BundleId = bundleId };

        var apiResponse = await _client.InvokeAsync<GetBundleRequest, BundleWithDownloadUrl>(
            "POST", "/bundles/get", request, cancellationToken: ct).ConfigureAwait(false);

        if (!apiResponse.IsSuccess)
        {
            // Treat any failure as "not found" for simplicity
            _logger?.LogDebug("Bundle {BundleId} not found or error: {Error}",
                bundleId, apiResponse.Error?.Message ?? "Unknown");
            return null;
        }

        var response = apiResponse.Result;
        if (response == null)
            return null;

        return new BundleDownloadInfo
        {
            BundleId = response.BundleId,
            DownloadUrl = response.DownloadUrl,
            SizeBytes = response.Size,
            ExpiresAt = response.ExpiresAt,
            AssetIds = new List<string>() // Not provided in this response
        };
    }

    /// <inheritdoc />
    public async Task<AssetDownloadInfo?> GetAssetDownloadInfoAsync(string assetId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Bannou server");

        var request = new GetAssetRequest
        {
            AssetId = assetId,
            IncludeDownloadUrl = true
        };

        var apiResponse = await _client.InvokeAsync<GetAssetRequest, AssetWithDownloadUrl>(
            "POST", "/assets/get", request, cancellationToken: ct).ConfigureAwait(false);

        if (!apiResponse.IsSuccess)
        {
            _logger?.LogDebug("Asset {AssetId} not found or error: {Error}",
                assetId, apiResponse.Error?.Message ?? "Unknown");
            return null;
        }

        var response = apiResponse.Result;
        if (response == null)
            return null;

        return new AssetDownloadInfo
        {
            AssetId = response.AssetId,
            DownloadUrl = response.DownloadUrl,
            SizeBytes = response.Size,
            ExpiresAt = response.ExpiresAt,
            ContentType = response.ContentType,
            ContentHash = response.ContentHash
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Exception thrown when asset source operations fail.
/// </summary>
public sealed class AssetSourceException : Exception
{
    /// <summary>Creates a new asset source exception.</summary>
    public AssetSourceException(string message) : base(message) { }

    /// <summary>Creates a new asset source exception with inner exception.</summary>
    public AssetSourceException(string message, Exception innerException) : base(message, innerException) { }
}

// TODO: These DTOs duplicate the generated models from the asset-api schema.
// This duplication is technical debt - changes to the schema won't propagate here.
// Proper fix: Create a shared BeyondImmersion.Bannou.AssetService.Models package
// containing these types, generated from the schema, that both client and server SDKs consume.

internal sealed class ResolveBundlesRequest
{
    public List<string> AssetIds { get; set; } = new();
    public List<string>? ExcludeBundleIds { get; set; }
}

internal sealed class ResolveBundlesResponse
{
    public List<ResolvedBundle>? Bundles { get; set; }
    public List<AssetWithDownloadUrl>? StandaloneAssets { get; set; }
}

internal sealed class ResolvedBundle
{
    public string BundleId { get; set; } = string.Empty;
    public Uri DownloadUrl { get; set; } = null!;
    public List<string>? IncludedAssetIds { get; set; }
    public BundleType BundleType { get; set; }
}

internal enum BundleType
{
    Source,
    Metabundle
}

internal sealed class GetBundleRequest
{
    public string BundleId { get; set; } = string.Empty;
}

internal sealed class BundleWithDownloadUrl
{
    public string BundleId { get; set; } = string.Empty;
    public Uri DownloadUrl { get; set; } = null!;
    public long Size { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class GetAssetRequest
{
    public string AssetId { get; set; } = string.Empty;
    public bool IncludeDownloadUrl { get; set; }
}

internal sealed class AssetWithDownloadUrl
{
    public string AssetId { get; set; } = string.Empty;
    public Uri DownloadUrl { get; set; } = null!;
    public long Size { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
}
