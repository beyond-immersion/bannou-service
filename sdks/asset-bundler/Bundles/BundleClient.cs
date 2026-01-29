using BeyondImmersion.Bannou.AssetBundler.Helpers;
using BeyondImmersion.Bannou.AssetBundler.Upload;
using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Asset;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Bundles;

/// <summary>
/// Client for managing bundle metadata, lifecycle, and version history.
/// Provides high-level operations for bundle CRUD, soft-delete/restore, and querying.
/// </summary>
public sealed class BundleClient
{
    private readonly BannouClient _client;
    private readonly ILogger<BundleClient>? _logger;

    /// <summary>
    /// Creates a new bundle client using an existing connected client.
    /// </summary>
    /// <param name="client">Connected Bannou client.</param>
    /// <param name="logger">Optional logger.</param>
    public BundleClient(BannouClient client, ILogger<BundleClient>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
    }

    /// <summary>
    /// Creates a new bundle client from uploader (shares connection).
    /// </summary>
    /// <param name="uploader">Connected uploader.</param>
    /// <param name="logger">Optional logger.</param>
    public static BundleClient FromUploader(BannouUploader uploader, ILogger<BundleClient>? logger = null)
    {
        if (uploader.Client == null)
            throw new InvalidOperationException("Uploader must be connected before creating BundleClient");
        return new BundleClient(uploader.Client, logger);
    }

    /// <summary>
    /// Gets bundle metadata and download URL.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="format">Optional bundle format (default: zip).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Bundle information with download URL.</returns>
    public async Task<BundleResult> GetAsync(
        string bundleId,
        BundleFormat format = BundleFormat.Zip,
        CancellationToken ct = default)
    {
        _logger?.LogInformation("Getting bundle {BundleId}", bundleId);

        var request = new GetBundleRequest
        {
            BundleId = bundleId,
            Format = format
        };

        var response = await _client.InvokeAsync<GetBundleRequest, BundleWithDownloadUrl>(
            "/bundles/get", request, cancellationToken: ct);

        var result = AssetApiHelpers.EnsureSuccess(response, "get bundle");

        return new BundleResult
        {
            BundleId = result.BundleId,
            Version = result.Version,
            DownloadUrl = result.DownloadUrl.ToString(),
            Format = result.Format.ToString(),
            ExpiresAt = result.ExpiresAt,
            Size = result.Size,
            AssetCount = result.AssetCount,
            FromCache = result.FromCache
        };
    }

    /// <summary>
    /// Updates bundle metadata.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="update">Metadata updates to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Update result with new version.</returns>
    public async Task<UpdateResult> UpdateAsync(
        string bundleId,
        BundleMetadataUpdate update,
        CancellationToken ct = default)
    {
        _logger?.LogInformation("Updating bundle {BundleId}", bundleId);

        var request = new UpdateBundleRequest
        {
            BundleId = bundleId,
            Name = update.Name,
            Description = update.Description,
            Tags = update.Tags,
            AddTags = update.AddTags,
            RemoveTags = update.RemoveTags?.ToList(),
            Reason = update.Reason
        };

        var response = await _client.InvokeAsync<UpdateBundleRequest, UpdateBundleResponse>(
            "/bundles/update", request, cancellationToken: ct);

        var result = AssetApiHelpers.EnsureSuccess(response, "update bundle");

        _logger?.LogInformation(
            "Updated bundle {BundleId} to version {Version}",
            bundleId, result.Version);

        return new UpdateResult
        {
            BundleId = result.BundleId,
            Version = result.Version,
            PreviousVersion = result.PreviousVersion,
            Changes = result.Changes.ToList(),
            UpdatedAt = result.UpdatedAt
        };
    }

    /// <summary>
    /// Soft-deletes a bundle (can be restored within retention period).
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="reason">Optional deletion reason.</param>
    /// <param name="permanent">If true, permanently deletes the bundle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deletion result.</returns>
    public async Task<DeleteResult> DeleteAsync(
        string bundleId,
        string? reason = null,
        bool permanent = false,
        CancellationToken ct = default)
    {
        _logger?.LogInformation(
            "Deleting bundle {BundleId} (permanent={Permanent})",
            bundleId, permanent);

        var request = new DeleteBundleRequest
        {
            BundleId = bundleId,
            Reason = reason,
            Permanent = permanent
        };

        var response = await _client.InvokeAsync<DeleteBundleRequest, DeleteBundleResponse>(
            "/bundles/delete", request, cancellationToken: ct);

        var result = AssetApiHelpers.EnsureSuccess(response, "delete bundle");

        _logger?.LogInformation(
            "Deleted bundle {BundleId}, status={Status}",
            bundleId, result.Status);

        return new DeleteResult
        {
            BundleId = result.BundleId,
            Status = result.Status.ToString(),
            DeletedAt = result.DeletedAt,
            RetentionUntil = result.RetentionUntil
        };
    }

    /// <summary>
    /// Restores a soft-deleted bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="reason">Optional restore reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Restore result.</returns>
    public async Task<RestoreResult> RestoreAsync(
        string bundleId,
        string? reason = null,
        CancellationToken ct = default)
    {
        _logger?.LogInformation("Restoring bundle {BundleId}", bundleId);

        var request = new RestoreBundleRequest
        {
            BundleId = bundleId,
            Reason = reason
        };

        var response = await _client.InvokeAsync<RestoreBundleRequest, RestoreBundleResponse>(
            "/bundles/restore", request, cancellationToken: ct);

        var result = AssetApiHelpers.EnsureSuccess(response, "restore bundle");

        _logger?.LogInformation(
            "Restored bundle {BundleId} from version {FromVersion}",
            bundleId, result.RestoredFromVersion);

        return new RestoreResult
        {
            BundleId = result.BundleId,
            Status = result.Status,
            RestoredAt = result.RestoredAt,
            RestoredFromVersion = result.RestoredFromVersion
        };
    }

    /// <summary>
    /// Lists version history for a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="limit">Maximum versions to return (default 50).</param>
    /// <param name="offset">Pagination offset (default 0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Version history list.</returns>
    public async Task<VersionHistoryResult> GetVersionsAsync(
        string bundleId,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
    {
        _logger?.LogInformation(
            "Getting version history for bundle {BundleId}",
            bundleId);

        var request = new ListBundleVersionsRequest
        {
            BundleId = bundleId,
            Limit = limit,
            Offset = offset
        };

        var response = await _client.InvokeAsync<ListBundleVersionsRequest, ListBundleVersionsResponse>(
            "/bundles/list-versions", request, cancellationToken: ct);

        var result = AssetApiHelpers.EnsureSuccess(response, "list bundle versions");

        return new VersionHistoryResult
        {
            BundleId = result.BundleId,
            CurrentVersion = result.CurrentVersion,
            Versions = result.Versions.Select(v => new VersionRecord
            {
                Version = v.Version,
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy,
                Changes = v.Changes.ToList(),
                Reason = v.Reason
            }).ToList(),
            TotalCount = result.TotalCount
        };
    }

    /// <summary>
    /// Queries bundles with filters.
    /// </summary>
    /// <param name="query">Query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Query results.</returns>
    public async Task<QueryResult> QueryAsync(
        BundleQuery query,
        CancellationToken ct = default)
    {
        _logger?.LogInformation(
            "Querying bundles for owner {Owner}",
            query.Owner);

        var request = new QueryBundlesRequest
        {
            Owner = query.Owner,
            Tags = query.Tags,
            TagExists = query.TagExists?.ToList(),
            TagNotExists = query.TagNotExists?.ToList(),
            Status = AssetApiHelpers.ParseLifecycle(query.Status),
            CreatedAfter = query.CreatedAfter,
            CreatedBefore = query.CreatedBefore,
            NameContains = query.NameContains,
            Realm = AssetApiHelpers.ParseRealm(query.Realm),
            BundleType = AssetApiHelpers.ParseBundleType(query.BundleType),
            Limit = query.Limit,
            Offset = query.Offset,
            IncludeDeleted = query.IncludeDeleted
        };

        var response = await _client.InvokeAsync<QueryBundlesRequest, QueryBundlesResponse>(
            "/bundles/query", request, cancellationToken: ct);

        var result = AssetApiHelpers.EnsureSuccess(response, "query bundles");

        return new QueryResult
        {
            Bundles = result.Bundles.Select(MapToBundleMetadata).ToList(),
            TotalCount = result.TotalCount,
            Limit = result.Limit,
            Offset = result.Offset
        };
    }

    /// <summary>
    /// Enumerates all bundles matching query (handles pagination automatically).
    /// </summary>
    /// <param name="query">Query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async enumerable of bundle metadata.</returns>
    public async IAsyncEnumerable<BundleMetadataResult> QueryAllAsync(
        BundleQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var currentQuery = query with { Offset = 0 };

        while (!ct.IsCancellationRequested)
        {
            var result = await QueryAsync(currentQuery, ct);

            foreach (var bundle in result.Bundles)
            {
                yield return bundle;
            }

            if (result.Bundles.Count < currentQuery.Limit)
            {
                break;
            }

            currentQuery = currentQuery with { Offset = currentQuery.Offset + currentQuery.Limit };
        }
    }

    private static BundleMetadataResult MapToBundleMetadata(BundleInfo info)
    {
        return new BundleMetadataResult
        {
            BundleId = info.BundleId,
            BundleType = info.BundleType.ToString(),
            Version = info.Version,
            MetadataVersion = info.MetadataVersion,
            Name = info.Name,
            Description = info.Description,
            Owner = info.Owner,
            Realm = info.Realm,
            Tags = info.Tags,
            Status = info.Status.ToString(),
            AssetCount = info.AssetCount,
            SizeBytes = info.SizeBytes ?? 0,
            CreatedAt = info.CreatedAt,
            UpdatedAt = info.UpdatedAt,
            DeletedAt = info.DeletedAt
        };
    }

}

/// <summary>
/// Result of getting a bundle with download URL.
/// </summary>
public sealed class BundleResult
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Bundle version string.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Presigned download URL.
    /// </summary>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// Format of the downloadable bundle.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>
    /// When the download URL expires.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Bundle file size in bytes.
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// Number of assets in the bundle.
    /// </summary>
    public required int AssetCount { get; init; }

    /// <summary>
    /// True if ZIP format was served from cache.
    /// </summary>
    public required bool FromCache { get; init; }
}

/// <summary>
/// Bundle metadata information.
/// </summary>
public sealed class BundleMetadataResult
{
    /// <summary>
    /// Unique bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Bundle type (source or metabundle).
    /// </summary>
    public required string BundleType { get; init; }

    /// <summary>
    /// Bundle content version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Metadata version number.
    /// </summary>
    public required int MetadataVersion { get; init; }

    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Owner identifier (null for system-owned bundles).
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Target realm.
    /// </summary>
    public required string Realm { get; init; }

    /// <summary>
    /// Key-value tags.
    /// </summary>
    public IDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Lifecycle status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Number of assets in bundle.
    /// </summary>
    public required int AssetCount { get; init; }

    /// <summary>
    /// Bundle size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// When the bundle was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the bundle was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// When the bundle was deleted.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; init; }
}

/// <summary>
/// Metadata update request.
/// </summary>
public sealed record BundleMetadataUpdate
{
    /// <summary>
    /// New name (null to keep existing).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// New description (null to keep existing).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Replace all tags with these.
    /// </summary>
    public IDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Add or update these tags.
    /// </summary>
    public IDictionary<string, string>? AddTags { get; init; }

    /// <summary>
    /// Remove these tag keys.
    /// </summary>
    public IReadOnlyList<string>? RemoveTags { get; init; }

    /// <summary>
    /// Reason for the update (optional, for audit trail).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Result of updating a bundle.
/// </summary>
public sealed class UpdateResult
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// New metadata version.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Previous metadata version.
    /// </summary>
    public required int PreviousVersion { get; init; }

    /// <summary>
    /// List of changes made.
    /// </summary>
    public required IReadOnlyList<string> Changes { get; init; }

    /// <summary>
    /// When the update was made.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Result of deleting a bundle.
/// </summary>
public sealed class DeleteResult
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Deletion status (deleted or permanently_deleted).
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// When the bundle was deleted.
    /// </summary>
    public required DateTimeOffset DeletedAt { get; init; }

    /// <summary>
    /// When the bundle will be permanently purged (null if permanent).
    /// </summary>
    public DateTimeOffset? RetentionUntil { get; init; }
}

/// <summary>
/// Result of restoring a bundle.
/// </summary>
public sealed class RestoreResult
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Current status (should be "active").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// When the bundle was restored.
    /// </summary>
    public required DateTimeOffset RestoredAt { get; init; }

    /// <summary>
    /// Version the bundle was restored from.
    /// </summary>
    public required int RestoredFromVersion { get; init; }
}

/// <summary>
/// Result of getting version history.
/// </summary>
public sealed class VersionHistoryResult
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Current metadata version.
    /// </summary>
    public required int CurrentVersion { get; init; }

    /// <summary>
    /// Version records.
    /// </summary>
    public required IReadOnlyList<VersionRecord> Versions { get; init; }

    /// <summary>
    /// Total number of versions.
    /// </summary>
    public required int TotalCount { get; init; }
}

/// <summary>
/// A single version record.
/// </summary>
public sealed class VersionRecord
{
    /// <summary>
    /// Version number.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// When this version was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Who created this version.
    /// </summary>
    public required string CreatedBy { get; init; }

    /// <summary>
    /// Changes made in this version.
    /// </summary>
    public required IReadOnlyList<string> Changes { get; init; }

    /// <summary>
    /// Reason for the change (optional).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Query parameters for searching bundles.
/// </summary>
public sealed record BundleQuery
{
    /// <summary>
    /// Owner to filter by (required).
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// Filter by exact tag matches.
    /// </summary>
    public IDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Filter by tags that exist (any value).
    /// </summary>
    public IReadOnlyList<string>? TagExists { get; init; }

    /// <summary>
    /// Filter by tags that don't exist.
    /// </summary>
    public IReadOnlyList<string>? TagNotExists { get; init; }

    /// <summary>
    /// Filter by status (active, deleted, processing).
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Filter bundles created after this time.
    /// </summary>
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>
    /// Filter bundles created before this time.
    /// </summary>
    public DateTimeOffset? CreatedBefore { get; init; }

    /// <summary>
    /// Filter by name containing this string.
    /// </summary>
    public string? NameContains { get; init; }

    /// <summary>
    /// Filter by realm.
    /// </summary>
    public string? Realm { get; init; }

    /// <summary>
    /// Filter by bundle type (source, metabundle).
    /// </summary>
    public string? BundleType { get; init; }

    /// <summary>
    /// Maximum results to return.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Pagination offset.
    /// </summary>
    public int Offset { get; init; } = 0;

    /// <summary>
    /// Include soft-deleted bundles.
    /// </summary>
    public bool IncludeDeleted { get; init; } = false;
}

/// <summary>
/// Result of querying bundles.
/// </summary>
public sealed class QueryResult
{
    /// <summary>
    /// Matching bundles.
    /// </summary>
    public required IReadOnlyList<BundleMetadataResult> Bundles { get; init; }

    /// <summary>
    /// Total count of matching bundles.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Limit used in query.
    /// </summary>
    public required int Limit { get; init; }

    /// <summary>
    /// Offset used in query.
    /// </summary>
    public required int Offset { get; init; }
}
