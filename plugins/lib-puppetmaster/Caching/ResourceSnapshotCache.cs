// =============================================================================
// Resource Snapshot Cache
// Caches resource snapshots for Event Brain actors.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;

namespace BeyondImmersion.BannouService.Puppetmaster.Caching;

/// <summary>
/// Cache implementation for resource snapshots used by Event Brain actors.
/// </summary>
/// <remarks>
/// <para>
/// Uses IResourceClient to fetch snapshots from the Resource service and caches
/// them locally. Snapshots are stored with TTL-based expiration.
/// </para>
/// <para>
/// <b>Thread Safety</b>: All operations are thread-safe via ConcurrentDictionary.
/// </para>
/// </remarks>
public sealed class ResourceSnapshotCache : IResourceSnapshotCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResourceSnapshotCache> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new resource snapshot cache.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for accessing IResourceClient.</param>
    /// <param name="logger">Logger instance.</param>
    public ResourceSnapshotCache(
        IServiceScopeFactory scopeFactory,
        ILogger<ResourceSnapshotCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResourceSnapshotData?> GetOrLoadAsync(
        string resourceType,
        Guid resourceId,
        IReadOnlyList<string>? filterSourceTypes,
        CancellationToken ct)
    {
        var cacheKey = GetCacheKey(resourceType, resourceId);

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug(
                "Resource snapshot cache hit: {ResourceType}:{ResourceId}",
                resourceType, resourceId);
            return ApplyFilter(cached.Data, filterSourceTypes);
        }

        // Load from Resource service
        _logger.LogDebug(
            "Loading resource snapshot: {ResourceType}:{ResourceId}",
            resourceType, resourceId);

        var snapshot = await LoadSnapshotAsync(resourceType, resourceId, ct);
        if (snapshot == null)
        {
            _logger.LogDebug(
                "Resource snapshot not found: {ResourceType}:{ResourceId}",
                resourceType, resourceId);
            return null;
        }

        // Cache the result
        var entry = new CacheEntry(snapshot, DateTimeOffset.UtcNow.Add(_defaultTtl));
        _cache[cacheKey] = entry;

        return ApplyFilter(snapshot, filterSourceTypes);
    }

    /// <inheritdoc />
    public async Task<int> PrefetchAsync(
        string resourceType,
        IReadOnlyList<Guid> resourceIds,
        IReadOnlyList<string>? filterSourceTypes,
        CancellationToken ct)
    {
        var successCount = 0;

        // Prefetch in parallel with bounded concurrency
        var semaphore = new SemaphoreSlim(5); // Max 5 concurrent fetches
        try
        {
            var tasks = resourceIds.Select(async id =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var result = await GetOrLoadAsync(resourceType, id, filterSourceTypes, ct);
                    if (result != null)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        finally
        {
            semaphore.Dispose();
        }

        _logger.LogDebug(
            "Prefetched {SuccessCount}/{TotalCount} {ResourceType} snapshots",
            successCount, resourceIds.Count, resourceType);

        return successCount;
    }

    /// <inheritdoc />
    public void Invalidate(string resourceType, Guid resourceId)
    {
        var cacheKey = GetCacheKey(resourceType, resourceId);
        if (_cache.TryRemove(cacheKey, out _))
        {
            _logger.LogDebug(
                "Invalidated cached snapshot: {ResourceType}:{ResourceId}",
                resourceType, resourceId);
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        var count = _cache.Count;
        _cache.Clear();
        if (count > 0)
        {
            _logger.LogDebug("Invalidated {Count} cached snapshots", count);
        }
    }

    private async Task<ResourceSnapshotData?> LoadSnapshotAsync(
        string resourceType,
        Guid resourceId,
        CancellationToken ct)
    {
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = _scopeFactory.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        try
        {
            var response = await resourceClient.ExecuteSnapshotAsync(
                new ExecuteSnapshotRequest
                {
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    SnapshotType = "event_actor"
                },
                ct);

            if (!response.Success || response.CallbackResults == null)
            {
                _logger.LogWarning(
                    "Snapshot failed for {ResourceType}:{ResourceId}: {Reason}",
                    resourceType, resourceId, response.AbortReason ?? "Unknown");
                return null;
            }

            // Need to fetch the actual snapshot data
            if (!response.SnapshotId.HasValue)
            {
                _logger.LogWarning(
                    "Snapshot succeeded but no snapshotId returned for {ResourceType}:{ResourceId}",
                    resourceType, resourceId);
                return null;
            }

            var getResponse = await resourceClient.GetSnapshotAsync(
                new GetSnapshotRequest { SnapshotId = response.SnapshotId.Value },
                ct);

            if (!getResponse.Found || getResponse.Snapshot == null)
            {
                _logger.LogWarning(
                    "Snapshot {SnapshotId} not found after creation",
                    response.SnapshotId);
                return null;
            }

            // Parse entries and decompress data
            var entries = new Dictionary<string, ResourceSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in getResponse.Snapshot.Entries)
            {
                try
                {
                    var decompressedData = DecompressData(entry.Data);
                    entries[entry.SourceType] = new ResourceSnapshotEntry(
                        entry.SourceType,
                        entry.ServiceName,
                        decompressedData,
                        entry.CompressedAt);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to decompress entry {SourceType} for {ResourceType}:{ResourceId}",
                        entry.SourceType, resourceType, resourceId);
                }
            }

            return new ResourceSnapshotData(
                resourceType,
                resourceId,
                entries,
                DateTimeOffset.UtcNow);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug(
                "Resource not found: {ResourceType}:{ResourceId}",
                resourceType, resourceId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error loading snapshot for {ResourceType}:{ResourceId}",
                resourceType, resourceId);
            return null;
        }
    }

    private static ResourceSnapshotData? ApplyFilter(
        ResourceSnapshotData data,
        IReadOnlyList<string>? filterSourceTypes)
    {
        if (filterSourceTypes == null || filterSourceTypes.Count == 0)
        {
            return data;
        }

        var filteredEntries = data.Entries
            .Where(kvp => filterSourceTypes.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        return new ResourceSnapshotData(
            data.ResourceType,
            data.ResourceId,
            filteredEntries,
            data.LoadedAt);
    }

    private static string GetCacheKey(string resourceType, Guid resourceId)
        => $"{resourceType}:{resourceId}";

    private static string DecompressData(string base64GzippedData)
    {
        var compressedBytes = Convert.FromBase64String(base64GzippedData);
        using var compressedStream = new MemoryStream(compressedBytes);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed record CacheEntry(ResourceSnapshotData Data, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }
}
