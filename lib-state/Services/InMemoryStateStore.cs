#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// In-memory state store for testing and minimal infrastructure scenarios.
/// Data is NOT persisted across restarts.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class InMemoryStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    private readonly string _storeName;
    private readonly ILogger<InMemoryStateStore<TValue>> _logger;

    /// <summary>
    /// Entry in the in-memory store containing value and metadata.
    /// </summary>
    private sealed class StoreEntry
    {
        public string Json { get; set; } = string.Empty;
        public long Version { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    // Shared store across all instances with same store name
    // This allows different typed stores to share the same underlying data
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StoreEntry>> _allStores = new();

    private readonly ConcurrentDictionary<string, StoreEntry> _store;

    /// <summary>
    /// Creates a new in-memory state store.
    /// </summary>
    /// <param name="storeName">Store name for namespacing.</param>
    /// <param name="logger">Logger instance.</param>
    public InMemoryStateStore(
        string storeName,
        ILogger<InMemoryStateStore<TValue>> logger)
    {
        _storeName = storeName ?? throw new ArgumentNullException(nameof(storeName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Get or create the store for this name
        _store = _allStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, StoreEntry>());

        _logger.LogDebug("In-memory state store '{StoreName}' initialized", storeName);
    }

    private bool IsExpired(StoreEntry entry)
    {
        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    private void CleanupExpired()
    {
        // Lazy cleanup - remove expired entries when we encounter them
        var expiredKeys = _store
            .Where(kvp => IsExpired(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _store.TryRemove(key, out _);
        }
    }

    /// <inheritdoc/>
    public Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_store.TryGetValue(key, out var entry))
        {
            if (IsExpired(entry))
            {
                _store.TryRemove(key, out _);
                _logger.LogDebug("Key '{Key}' expired in store '{Store}'", key, _storeName);
                return Task.FromResult<TValue?>(null);
            }

            var value = BannouJson.Deserialize<TValue>(entry.Json);
            return Task.FromResult(value);
        }

        _logger.LogDebug("Key '{Key}' not found in store '{Store}'", key, _storeName);
        return Task.FromResult<TValue?>(null);
    }

    /// <inheritdoc/>
    public Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_store.TryGetValue(key, out var entry))
        {
            if (IsExpired(entry))
            {
                _store.TryRemove(key, out _);
                return Task.FromResult<(TValue?, string?)>((null, null));
            }

            var value = BannouJson.Deserialize<TValue>(entry.Json);
            return Task.FromResult<(TValue?, string?)>((value, entry.Version.ToString()));
        }

        return Task.FromResult<(TValue?, string?)>((null, null));
    }

    /// <inheritdoc/>
    public Task<string> SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var json = BannouJson.Serialize(value);
        var ttl = options?.Ttl;
        DateTimeOffset? expiresAt = ttl.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(ttl.Value)
            : null;

        var entry = _store.AddOrUpdate(
            key,
            _ => new StoreEntry
            {
                Json = json,
                Version = 1,
                ExpiresAt = expiresAt
            },
            (_, existing) => new StoreEntry
            {
                Json = json,
                Version = existing.Version + 1,
                ExpiresAt = expiresAt
            });

        _logger.LogDebug("Saved key '{Key}' in store '{Store}' (version: {Version})",
            key, _storeName, entry.Version);

        return Task.FromResult(entry.Version.ToString());
    }

    /// <inheritdoc/>
    public Task<bool> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(etag);

        if (!long.TryParse(etag, out var expectedVersion))
        {
            _logger.LogDebug("Invalid ETag format for key '{Key}' in store '{Store}'", key, _storeName);
            return Task.FromResult(false);
        }

        var json = BannouJson.Serialize(value);

        // Use CompareExchange pattern for optimistic concurrency
        if (_store.TryGetValue(key, out var existing))
        {
            if (existing.Version != expectedVersion)
            {
                _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                    key, _storeName, etag, existing.Version);
                return Task.FromResult(false);
            }

            var newEntry = new StoreEntry
            {
                Json = json,
                Version = existing.Version + 1,
                ExpiresAt = existing.ExpiresAt // Preserve TTL
            };

            // Attempt atomic update
            if (_store.TryUpdate(key, newEntry, existing))
            {
                _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _storeName);
                return Task.FromResult(true);
            }
        }

        _logger.LogDebug("Optimistic save failed for key '{Key}' in store '{Store}'", key, _storeName);
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var deleted = _store.TryRemove(key, out _);
        _logger.LogDebug("Deleted key '{Key}' from store '{Store}' (existed: {Existed})",
            key, _storeName, deleted);

        return Task.FromResult(deleted);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_store.TryGetValue(key, out var entry))
        {
            if (IsExpired(entry))
            {
                _store.TryRemove(key, out _);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<string, TValue>();
        var keyList = keys.ToList();

        foreach (var key in keyList)
        {
            if (_store.TryGetValue(key, out var entry) && !IsExpired(entry))
            {
                var value = BannouJson.Deserialize<TValue>(entry.Json);
                if (value != null)
                {
                    result[key] = value;
                }
            }
        }

        _logger.LogDebug("Bulk get {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _storeName, result.Count);

        return Task.FromResult<IReadOnlyDictionary<string, TValue>>(result);
    }

    /// <summary>
    /// Clear all entries in this store (useful for testing).
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        _logger.LogDebug("Cleared all entries from store '{Store}'", _storeName);
    }

    /// <summary>
    /// Get count of entries in store (useful for testing).
    /// </summary>
    public int Count
    {
        get
        {
            CleanupExpired();
            return _store.Count;
        }
    }
}
