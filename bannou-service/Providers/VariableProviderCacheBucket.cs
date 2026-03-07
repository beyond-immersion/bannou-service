#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// A single-dictionary cache bucket for variable provider data with TTL-based expiration
/// and stale-data fallback. Encapsulates the ConcurrentDictionary + CachedEntry + get-or-load
/// + stale fallback pattern that is duplicated across all variable provider cache implementations.
/// </summary>
/// <remarks>
/// <para>
/// This is a composition helper, not a base class. Concrete caches compose one or more buckets:
/// <c>BackstoryCache</c> uses one bucket (backstory by characterId),
/// <c>PersonalityDataCache</c> uses two (personality + combat preferences),
/// <c>EncounterDataCache</c> uses four (encounters, sentiment, hasMet, pairEncounters).
/// </para>
/// <para>
/// <b>Invalidation</b>: Callers MUST wire event-driven invalidation via IEventConsumer
/// (per IMPLEMENTATION TENETS — ConcurrentDictionary caches must invalidate via event
/// subscription, not inline method calls, for multi-node correctness).
/// </para>
/// </remarks>
/// <typeparam name="TKey">Cache key type (typically <c>Guid</c> for entity IDs or <c>string</c> for composite keys).</typeparam>
/// <typeparam name="TData">Cached data type (typically a generated response model).</typeparam>
public sealed class VariableProviderCacheBucket<TKey, TData> where TKey : notnull where TData : class
{
    private readonly ConcurrentDictionary<TKey, CachedEntry> _cache = new();
    private readonly TimeSpan _ttl;
    private readonly ILogger _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly string _serviceName;
    private readonly string _bucketName;

    /// <summary>
    /// Creates a new cache bucket.
    /// </summary>
    /// <param name="ttl">Time-to-live for cached entries.</param>
    /// <param name="logger">Logger for cache hit/miss/error messages.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="serviceName">Service name for telemetry spans (e.g., "bannou.character-personality").</param>
    /// <param name="bucketName">Human-readable bucket name for logging and spans (e.g., "PersonalityCache").</param>
    public VariableProviderCacheBucket(
        TimeSpan ttl,
        ILogger logger,
        ITelemetryProvider telemetryProvider,
        string serviceName,
        string bucketName)
    {
        _ttl = ttl;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _serviceName = serviceName;
        _bucketName = bucketName;
    }

    /// <summary>
    /// Gets data from cache or loads it via the provided loader function.
    /// On load failure, returns stale cached data if available (graceful degradation).
    /// On 404 from the source service, returns null (entity does not exist).
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="loader">
    /// Async function that loads the data from the source service.
    /// Typically creates a scope, resolves a client, and calls the service API.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached or freshly-loaded data, or null if not found.</returns>
    public async Task<TData?> GetOrLoadAsync(
        TKey key,
        Func<CancellationToken, Task<TData?>> loader,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity(_serviceName, $"{_bucketName}.GetOrLoad");

        if (_cache.TryGetValue(key, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("{Bucket} cache hit for {Key}", _bucketName, key);
            return cached.Data;
        }

        _logger.LogDebug("{Bucket} cache miss for {Key}, loading from service", _bucketName, key);

        try
        {
            var data = await loader(ct);
            if (data is not null)
            {
                _cache[key] = new CachedEntry(data, DateTimeOffset.UtcNow.Add(_ttl));
                _logger.LogDebug("{Bucket} cached data for {Key}", _bucketName, key);
            }

            return data;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("{Bucket} entity not found for {Key}", _bucketName, key);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Bucket} failed to load data for {Key}", _bucketName, key);
            return cached?.Data; // Return stale data if available
        }
    }

    /// <summary>
    /// Removes a single entry from the cache.
    /// </summary>
    /// <param name="key">Cache key to invalidate.</param>
    public void Invalidate(TKey key)
    {
        _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes all entries matching a predicate. Useful for composite-key caches
    /// where invalidating by entity ID requires scanning keys (e.g., encounter pair keys
    /// containing a character ID).
    /// </summary>
    /// <param name="predicate">Predicate to match keys for removal.</param>
    public void InvalidateWhere(Func<TKey, bool> predicate)
    {
        foreach (var key in _cache.Keys.Where(predicate).ToList())
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    public void InvalidateAll()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Cached data entry with expiration timestamp.
    /// </summary>
    private sealed record CachedEntry(TData Data, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
