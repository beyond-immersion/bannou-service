using BeyondImmersion.Bannou.AssetBundler.Upload;
using BeyondImmersion.Bannou.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Metabundles;

/// <summary>
/// Client for requesting server-side metabundle creation.
/// Use when combining already-uploaded bundles on the server.
/// </summary>
public sealed class MetabundleClient
{
    private readonly BannouClient _client;
    private readonly ILogger<MetabundleClient>? _logger;

    /// <summary>
    /// Creates a new metabundle client using an existing connected client.
    /// </summary>
    /// <param name="client">Connected Bannou client.</param>
    /// <param name="logger">Optional logger.</param>
    public MetabundleClient(BannouClient client, ILogger<MetabundleClient>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
    }

    /// <summary>
    /// Creates a new metabundle client from uploader (shares connection).
    /// </summary>
    /// <param name="uploader">Connected uploader.</param>
    /// <param name="logger">Optional logger.</param>
    public static MetabundleClient FromUploader(BannouUploader uploader, ILogger<MetabundleClient>? logger = null)
    {
        if (uploader.Client == null)
            throw new InvalidOperationException("Uploader must be connected before creating MetabundleClient");
        return new MetabundleClient(uploader.Client, logger);
    }

    /// <summary>
    /// Requests the server create a metabundle from existing bundles.
    /// </summary>
    /// <param name="request">The metabundle request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created metabundle result.</returns>
    public async Task<CreateMetabundleResult> CreateAsync(
        MetabundleRequest request,
        CancellationToken ct = default)
    {
        _logger?.LogInformation(
            "Creating metabundle {MetabundleId} from {SourceCount} sources",
            request.MetabundleId,
            request.SourceBundleIds.Count);

        var apiResponse = await _client.InvokeAsync<MetabundleRequest, CreateMetabundleResponse>(
            "POST", "/bundles/metabundle/create", request, cancellationToken: ct);

        if (!apiResponse.IsSuccess || apiResponse.Result == null)
        {
            var errorCode = apiResponse.Error?.ResponseCode ?? 500;
            var errorMessage = apiResponse.Error?.Message ?? "Unknown error";
            throw new InvalidOperationException($"Failed to create metabundle: {errorCode} - {errorMessage}");
        }

        var response = apiResponse.Result;

        _logger?.LogInformation(
            "Created metabundle {MetabundleId} with {AssetCount} assets",
            response.MetabundleId,
            response.AssetCount);

        return new CreateMetabundleResult
        {
            MetabundleId = response.MetabundleId,
            AssetCount = response.AssetCount,
            TotalSizeBytes = response.TotalSizeBytes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Request to create a metabundle.
/// </summary>
public sealed class MetabundleRequest
{
    /// <summary>
    /// Unique identifier for the metabundle.
    /// </summary>
    public required string MetabundleId { get; init; }

    /// <summary>
    /// Source bundle IDs to include.
    /// </summary>
    public required IReadOnlyList<string> SourceBundleIds { get; init; }

    /// <summary>
    /// Standalone asset IDs to include (optional).
    /// </summary>
    public IReadOnlyList<string>? StandaloneAssetIds { get; init; }

    /// <summary>
    /// Filter to specific asset IDs (optional).
    /// </summary>
    public IReadOnlyList<string>? AssetFilter { get; init; }

    /// <summary>
    /// Version string for the metabundle.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Owner identifier.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Target realm for the metabundle.
    /// </summary>
    public string? Realm { get; init; }
}

/// <summary>
/// Result of creating a metabundle.
/// </summary>
public sealed class CreateMetabundleResult
{
    /// <summary>
    /// The created metabundle ID.
    /// </summary>
    public required string MetabundleId { get; init; }

    /// <summary>
    /// Number of assets in the metabundle.
    /// </summary>
    public required int AssetCount { get; init; }

    /// <summary>
    /// Total size in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// When the metabundle was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

// Internal DTO for API response
internal sealed class CreateMetabundleResponse
{
    public required string MetabundleId { get; init; }
    public required int AssetCount { get; init; }
    public required long TotalSizeBytes { get; init; }
}
