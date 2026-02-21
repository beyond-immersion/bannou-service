#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Static holder for shared in-memory store data.
/// This non-generic class ensures the dictionaries are truly shared across all generic instantiations
/// of InMemoryStateStore&lt;T&gt;. In C#, static fields in generic classes are per closed generic type,
/// so without this holder class, InMemoryStateStore&lt;Foo&gt;._allStores would be a different
/// dictionary than InMemoryStateStore&lt;Bar&gt;._allStores.
/// </summary>
internal static class InMemoryStoreData
{
    /// <summary>
    /// Entry in the in-memory store containing value and metadata.
    /// </summary>
    internal sealed class StoreEntry
    {
        public string Json { get; set; } = string.Empty;
        public long Version { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    /// <summary>
    /// Entry for a set in the in-memory store.
    /// </summary>
    internal sealed class SetEntry
    {
        public HashSet<string> Items { get; } = new();
        public DateTimeOffset? ExpiresAt { get; set; }
        public readonly object Lock = new();
    }

    /// <summary>
    /// Entry for a sorted set in the in-memory store.
    /// Uses a dictionary for member->score lookup.
    /// </summary>
    internal sealed class SortedSetEntry
    {
        // Member -> Score mapping for O(1) lookup
        public Dictionary<string, double> MemberScores { get; } = new();
        public DateTimeOffset? ExpiresAt { get; set; }
        public readonly object Lock = new();
    }

    /// <summary>
    /// Entry for a counter in the in-memory store.
    /// </summary>
    internal sealed class CounterEntry
    {
        private long _value;
        public long Value => Interlocked.Read(ref _value);
        public DateTimeOffset? ExpiresAt { get; set; }
        public readonly object Lock = new();

        public long Increment(long amount)
        {
            return Interlocked.Add(ref _value, amount);
        }

        public void SetValue(long value)
        {
            Interlocked.Exchange(ref _value, value);
        }
    }

    /// <summary>
    /// Entry for a hash in the in-memory store.
    /// </summary>
    internal sealed class HashStoreEntry
    {
        // Field -> JSON value mapping
        public Dictionary<string, string> Fields { get; } = new();
        // Field -> numeric value for increment operations
        public Dictionary<string, long> NumericFields { get; } = new();
        public DateTimeOffset? ExpiresAt { get; set; }
        public readonly object Lock = new();
    }

    // Shared store across all instances with same store name
    // This allows different typed stores to share the same underlying data
    internal static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StoreEntry>> AllStores = new();

    // Shared set store for set operations
    internal static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SetEntry>> AllSetStores = new();

    // Shared sorted set store for sorted set operations
    internal static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SortedSetEntry>> AllSortedSetStores = new();

    // Shared counter store for atomic counter operations
    internal static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CounterEntry>> AllCounterStores = new();

    // Shared hash store for hash operations
    internal static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HashStoreEntry>> AllHashStores = new();

    /// <summary>
    /// Get the key count for a store by name.
    /// Does not clean up expired entries - returns raw count for efficiency.
    /// </summary>
    /// <param name="storeName">Name of the store.</param>
    /// <returns>Key count, or 0 if store doesn't exist.</returns>
    public static long GetKeyCountForStore(string storeName)
    {
        if (AllStores.TryGetValue(storeName, out var store))
        {
            return store.Count;
        }
        return 0;
    }
}

/// <summary>
/// In-memory state store for testing and minimal infrastructure scenarios.
/// Data is NOT persisted across restarts.
/// Thread-safe via ConcurrentDictionary.
/// Implements ICacheableStateStore for Set and Sorted Set operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class InMemoryStateStore<TValue> : ICacheableStateStore<TValue>
    where TValue : class
{
    private readonly string _storeName;
    private readonly ILogger<InMemoryStateStore<TValue>> _logger;
    private readonly StateErrorPublisherAsync? _errorPublisher;

    // Instance references to the shared static stores for this store name
    private readonly ConcurrentDictionary<string, InMemoryStoreData.StoreEntry> _store;
    private readonly ConcurrentDictionary<string, InMemoryStoreData.SetEntry> _setStore;
    private readonly ConcurrentDictionary<string, InMemoryStoreData.SortedSetEntry> _sortedSetStore;
    private readonly ConcurrentDictionary<string, InMemoryStoreData.CounterEntry> _counterStore;
    private readonly ConcurrentDictionary<string, InMemoryStoreData.HashStoreEntry> _hashStore;

    /// <summary>
    /// Creates a new in-memory state store.
    /// </summary>
    /// <param name="storeName">Store name for namespacing.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="errorPublisher">Optional callback for publishing state errors with deduplication.</param>
    public InMemoryStateStore(
        string storeName,
        ILogger<InMemoryStateStore<TValue>> logger,
        StateErrorPublisherAsync? errorPublisher = null)
    {
        _storeName = storeName;
        _logger = logger;
        _errorPublisher = errorPublisher;

        // Get or create the store for this name from the shared static holder
        _store = InMemoryStoreData.AllStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, InMemoryStoreData.StoreEntry>());
        _setStore = InMemoryStoreData.AllSetStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, InMemoryStoreData.SetEntry>());
        _sortedSetStore = InMemoryStoreData.AllSortedSetStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, InMemoryStoreData.SortedSetEntry>());
        _counterStore = InMemoryStoreData.AllCounterStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, InMemoryStoreData.CounterEntry>());
        _hashStore = InMemoryStoreData.AllHashStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, InMemoryStoreData.HashStoreEntry>());

        _logger.LogDebug("In-memory state store '{StoreName}' initialized", storeName);
    }

    private bool IsExpired(InMemoryStoreData.StoreEntry entry)
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
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (_store.TryGetValue(key, out var entry))
        {
            if (IsExpired(entry))
            {
                _store.TryRemove(key, out _);
                _logger.LogDebug("Key '{Key}' expired in store '{Store}'", key, _storeName);
                return null;
            }

            try
            {
                return BannouJson.Deserialize<TValue>(entry.Json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _storeName);
                return null;
            }
        }

        _logger.LogDebug("Key '{Key}' not found in store '{Store}'", key, _storeName);
        return null;
    }

    /// <inheritdoc/>
    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (_store.TryGetValue(key, out var entry))
        {
            if (IsExpired(entry))
            {
                _store.TryRemove(key, out _);
                return (null, null);
            }

            try
            {
                var value = BannouJson.Deserialize<TValue>(entry.Json);
                return (value, entry.Version.ToString());
            }
            catch (System.Text.Json.JsonException ex)
            {
                // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _storeName);
                return (null, null);
            }
        }

        return (null, null);
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var json = BannouJson.Serialize(value);
        var ttl = options?.Ttl;
        DateTimeOffset? expiresAt = ttl.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(ttl.Value)
            : null;

        var entry = _store.AddOrUpdate(
            key,
            _ => new InMemoryStoreData.StoreEntry
            {
                Json = json,
                Version = 1,
                ExpiresAt = expiresAt
            },
            (_, existing) => new InMemoryStoreData.StoreEntry
            {
                Json = json,
                Version = existing.Version + 1,
                ExpiresAt = expiresAt
            });

        _logger.LogDebug("Saved key '{Key}' in store '{Store}' (version: {Version})",
            key, _storeName, entry.Version);

        await Task.CompletedTask;
        return entry.Version.ToString();
    }

    /// <inheritdoc/>
    public async Task<string?> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var json = BannouJson.Serialize(value);

        // Empty etag means "create new entry if it doesn't exist" (matches Redis/MySQL semantics)
        if (string.IsNullOrEmpty(etag))
        {
            var newEntry = new InMemoryStoreData.StoreEntry
            {
                Json = json,
                Version = 1,
                ExpiresAt = null
            };

            // TryAdd is atomic - returns false if key already exists
            if (_store.TryAdd(key, newEntry))
            {
                _logger.LogDebug("Created new key '{Key}' in store '{Store}'", key, _storeName);
                return "1";
            }
            else
            {
                _logger.LogDebug("Key '{Key}' already exists in store '{Store}' but empty etag provided (concurrent create)",
                    key, _storeName);
                return null;
            }
        }

        // Non-empty etag means "update existing entry with matching version"
        if (!long.TryParse(etag, out var expectedVersion))
        {
            _logger.LogDebug("Invalid ETag format for key '{Key}' in store '{Store}'", key, _storeName);
            return null;
        }

        // Use CompareExchange pattern for optimistic concurrency
        if (_store.TryGetValue(key, out var existing))
        {
            if (existing.Version != expectedVersion)
            {
                _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                    key, _storeName, etag, existing.Version);
                return null;
            }

            var updatedEntry = new InMemoryStoreData.StoreEntry
            {
                Json = json,
                Version = existing.Version + 1,
                ExpiresAt = existing.ExpiresAt // Preserve TTL
            };

            // Attempt atomic update
            if (_store.TryUpdate(key, updatedEntry, existing))
            {
                _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _storeName);
                return updatedEntry.Version.ToString();
            }
        }

        _logger.LogDebug("Optimistic save failed for key '{Key}' in store '{Store}'", key, _storeName);
        return null;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {

        var deleted = _store.TryRemove(key, out _);
        _logger.LogDebug("Deleted key '{Key}' from store '{Store}' (existed: {Existed})",
            key, _storeName, deleted);

        await Task.CompletedTask;
        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (_store.TryGetValue(key, out var entry))
        {
            if (IsExpired(entry))
            {
                _store.TryRemove(key, out _);
                return false;
            }
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {

        var result = new Dictionary<string, TValue>();
        var keyList = keys.ToList();

        foreach (var key in keyList)
        {
            if (_store.TryGetValue(key, out var entry) && !IsExpired(entry))
            {
                try
                {
                    var value = BannouJson.Deserialize<TValue>(entry.Json);
                    if (value != null)
                    {
                        result[key] = value;
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // IMPLEMENTATION TENETS: Log data corruption as error and skip the item
                    _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", key, _storeName);
                }
            }
        }

        _logger.LogDebug("Bulk get {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _storeName, result.Count);

        await Task.CompletedTask;
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> SaveBulkAsync(
        IEnumerable<KeyValuePair<string, TValue>> items,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>();
        var ttl = options?.Ttl;
        DateTimeOffset? expiresAt = ttl.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(ttl.Value)
            : null;

        foreach (var (key, value) in itemList)
        {
            var json = BannouJson.Serialize(value);
            var entry = _store.AddOrUpdate(
                key,
                _ => new InMemoryStoreData.StoreEntry
                {
                    Json = json,
                    Version = 1,
                    ExpiresAt = expiresAt
                },
                (_, existing) => new InMemoryStoreData.StoreEntry
                {
                    Json = json,
                    Version = existing.Version + 1,
                    ExpiresAt = expiresAt
                });
            result[key] = entry.Version.ToString();
        }

        _logger.LogDebug("Bulk save {Count} items to store '{Store}'", itemList.Count, _storeName);

        await Task.CompletedTask;
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> ExistsBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return new HashSet<string>();
        }

        var existing = new HashSet<string>();
        foreach (var key in keyList)
        {
            if (_store.TryGetValue(key, out var entry))
            {
                if (IsExpired(entry))
                {
                    _store.TryRemove(key, out _); // Lazy cleanup
                }
                else
                {
                    existing.Add(key);
                }
            }
        }

        _logger.LogDebug("Bulk exists check {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _storeName, existing.Count);

        await Task.CompletedTask;
        return existing;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return 0;
        }

        var deletedCount = 0;
        foreach (var key in keyList)
        {
            if (_store.TryRemove(key, out _))
            {
                deletedCount++;
            }
        }

        _logger.LogDebug("Bulk delete {RequestedCount} keys from store '{Store}', deleted {DeletedCount}",
            keyList.Count, _storeName, deletedCount);

        await Task.CompletedTask;
        return deletedCount;
    }

    /// <summary>
    /// Clear all entries in this store (useful for testing).
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        _setStore.Clear();
        _sortedSetStore.Clear();
        _counterStore.Clear();
        _hashStore.Clear();
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

    /// <summary>
    /// Get the key count for a store by name (static method for factory use).
    /// Does not clean up expired entries - returns raw count for efficiency.
    /// </summary>
    /// <param name="storeName">Name of the store.</param>
    /// <returns>Key count, or 0 if store doesn't exist.</returns>
    public static long GetKeyCountForStore(string storeName)
    {
        // Delegate to the shared holder class
        return InMemoryStoreData.GetKeyCountForStore(storeName);
    }

    // ==================== Set Operations ====================

    private bool IsSetExpired(InMemoryStoreData.SetEntry entry)
    {
        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<bool> AddToSetAsync<TItem>(
        string key,
        TItem item,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var json = BannouJson.Serialize(item);
        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var entry = _setStore.GetOrAdd(key, _ => new InMemoryStoreData.SetEntry());

        lock (entry.Lock)
        {
            if (IsSetExpired(entry))
            {
                entry.Items.Clear();
            }

            var added = entry.Items.Add(json);
            if (expiresAt.HasValue)
            {
                entry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Added item to set '{Key}' in store '{Store}' (new: {IsNew})",
                key, _storeName, added);

            return added;
        }
    }

    /// <inheritdoc/>
    public async Task<long> AddToSetAsync<TItem>(
        string key,
        IEnumerable<TItem> items,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return 0;
        }

        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var entry = _setStore.GetOrAdd(key, _ => new InMemoryStoreData.SetEntry());

        lock (entry.Lock)
        {
            if (IsSetExpired(entry))
            {
                entry.Items.Clear();
            }

            var added = 0L;
            foreach (var item in itemList)
            {
                var json = BannouJson.Serialize(item);
                if (entry.Items.Add(json))
                {
                    added++;
                }
            }

            if (expiresAt.HasValue)
            {
                entry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Added {Count} items to set '{Key}' in store '{Store}' (new: {Added})",
                itemList.Count, key, _storeName, added);

            return added;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveFromSetAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_setStore.TryGetValue(key, out var entry))
        {
            return false;
        }

        var json = BannouJson.Serialize(item);

        lock (entry.Lock)
        {
            if (IsSetExpired(entry))
            {
                _setStore.TryRemove(key, out _);
                return false;
            }

            var removed = entry.Items.Remove(json);

            _logger.LogDebug("Removed item from set '{Key}' in store '{Store}' (existed: {Existed})",
                key, _storeName, removed);

            return removed;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TItem>> GetSetAsync<TItem>(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_setStore.TryGetValue(key, out var entry))
        {
            _logger.LogDebug("Set '{Key}' not found in store '{Store}'", key, _storeName);
            return Array.Empty<TItem>();
        }

        lock (entry.Lock)
        {
            if (IsSetExpired(entry))
            {
                _setStore.TryRemove(key, out _);
                return Array.Empty<TItem>();
            }

            var result = new List<TItem>(entry.Items.Count);
            foreach (var json in entry.Items)
            {
                try
                {
                    var item = BannouJson.Deserialize<TItem>(json);
                    if (item != null)
                    {
                        result.Add(item);
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // IMPLEMENTATION TENETS: Log data corruption as error and skip the item
                    _logger.LogError(ex, "JSON deserialization failed for set item in '{Key}' in store '{Store}' - skipping corrupted item", key, _storeName);
                }
            }

            _logger.LogDebug("Retrieved {Count} items from set '{Key}' in store '{Store}'",
                result.Count, key, _storeName);

            return result;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetContainsAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_setStore.TryGetValue(key, out var entry))
        {
            return false;
        }

        var json = BannouJson.Serialize(item);

        lock (entry.Lock)
        {
            if (IsSetExpired(entry))
            {
                _setStore.TryRemove(key, out _);
                return false;
            }

            return entry.Items.Contains(json);
        }
    }

    /// <inheritdoc/>
    public async Task<long> SetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_setStore.TryGetValue(key, out var entry))
        {
            return 0;
        }

        lock (entry.Lock)
        {
            if (IsSetExpired(entry))
            {
                _setStore.TryRemove(key, out _);
                return 0;
            }

            return entry.Items.Count;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var deleted = _setStore.TryRemove(key, out _);

        _logger.LogDebug("Deleted set '{Key}' from store '{Store}' (existed: {Existed})",
            key, _storeName, deleted);

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshSetTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_setStore.TryGetValue(key, out var entry))
        {
            return false;
        }

        lock (entry.Lock)
        {
            if (IsSetExpired(entry))
            {
                _setStore.TryRemove(key, out _);
                return false;
            }

            entry.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);

            _logger.LogDebug("Refreshed TTL on set '{Key}' in store '{Store}' to {Ttl}s",
                key, _storeName, ttlSeconds);

            return true;
        }
    }

    // ==================== Sorted Set Operations ====================

    private bool IsSortedSetExpired(InMemoryStoreData.SortedSetEntry entry)
    {
        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Get members ordered by score for ranking operations.
    /// </summary>
    private IReadOnlyList<(string member, double score)> GetOrderedMembers(InMemoryStoreData.SortedSetEntry entry, bool descending)
    {
        var ordered = descending
            ? entry.MemberScores.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key)
            : entry.MemberScores.OrderBy(kvp => kvp.Value).ThenBy(kvp => kvp.Key);

        return ordered.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetAddAsync(
        string key,
        string member,
        double score,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var entry = _sortedSetStore.GetOrAdd(key, _ => new InMemoryStoreData.SortedSetEntry());

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                entry.MemberScores.Clear();
            }

            var isNew = !entry.MemberScores.ContainsKey(member);
            entry.MemberScores[member] = score;

            if (expiresAt.HasValue)
            {
                entry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Added member '{Member}' to sorted set '{Key}' with score {Score} (new: {IsNew})",
                member, key, score, isNew);

            return isNew;
        }
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetAddBatchAsync(
        string key,
        IEnumerable<(string member, double score)> entries,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return 0;
        }

        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var sortedSetEntry = _sortedSetStore.GetOrAdd(key, _ => new InMemoryStoreData.SortedSetEntry());

        lock (sortedSetEntry.Lock)
        {
            if (IsSortedSetExpired(sortedSetEntry))
            {
                sortedSetEntry.MemberScores.Clear();
            }

            var added = 0L;
            foreach (var (member, score) in entryList)
            {
                if (!sortedSetEntry.MemberScores.ContainsKey(member))
                {
                    added++;
                }
                sortedSetEntry.MemberScores[member] = score;
            }

            if (expiresAt.HasValue)
            {
                sortedSetEntry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Batch added {Count} entries to sorted set '{Key}', {NewCount} new",
                entryList.Count, key, added);

            return added;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetRemoveAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_sortedSetStore.TryGetValue(key, out var entry))
        {
            return false;
        }

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                _sortedSetStore.TryRemove(key, out _);
                return false;
            }

            var removed = entry.MemberScores.Remove(member);

            _logger.LogDebug("Removed member '{Member}' from sorted set '{Key}' (existed: {Existed})",
                member, key, removed);

            return removed;
        }
    }

    /// <inheritdoc/>
    public async Task<double?> SortedSetScoreAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_sortedSetStore.TryGetValue(key, out var entry))
        {
            return null;
        }

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                _sortedSetStore.TryRemove(key, out _);
                return null;
            }

            return entry.MemberScores.TryGetValue(member, out var score) ? score : null;
        }
    }

    /// <inheritdoc/>
    public async Task<long?> SortedSetRankAsync(
        string key,
        string member,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_sortedSetStore.TryGetValue(key, out var entry))
        {
            return null;
        }

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                _sortedSetStore.TryRemove(key, out _);
                return null;
            }

            if (!entry.MemberScores.ContainsKey(member))
            {
                return null;
            }

            var orderedMembers = GetOrderedMembers(entry, descending);
            for (var i = 0; i < orderedMembers.Count; i++)
            {
                if (orderedMembers[i].member == member)
                {
                    return i;
                }
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByRankAsync(
        string key,
        long start,
        long stop,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_sortedSetStore.TryGetValue(key, out var entry))
        {
            return Array.Empty<(string, double)>();
        }

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                _sortedSetStore.TryRemove(key, out _);
                return Array.Empty<(string, double)>();
            }

            var orderedMembers = GetOrderedMembers(entry, descending);
            var count = orderedMembers.Count;

            // Handle negative indices (like Redis)
            if (start < 0) start = Math.Max(0, count + start);
            if (stop < 0) stop = count + stop;

            // Clamp to valid range
            start = Math.Max(0, start);
            stop = Math.Min(count - 1, stop);

            if (start > stop || start >= count)
            {
                return Array.Empty<(string, double)>();
            }

            return orderedMembers
                .Skip((int)start)
                .Take((int)(stop - start + 1))
                .ToList();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByScoreAsync(
        string key,
        double minScore,
        double maxScore,
        int offset = 0,
        int count = -1,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_sortedSetStore.TryGetValue(key, out var entry))
        {
            return Array.Empty<(string, double)>();
        }

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                _sortedSetStore.TryRemove(key, out _);
                return Array.Empty<(string, double)>();
            }

            var orderedMembers = GetOrderedMembers(entry, descending);

            var filtered = orderedMembers
                .Where(m => m.score >= minScore && m.score <= maxScore);

            if (offset > 0)
            {
                filtered = filtered.Skip(offset);
            }

            if (count >= 0)
            {
                filtered = filtered.Take(count);
            }

            return filtered.ToList();
        }
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_sortedSetStore.TryGetValue(key, out var entry))
        {
            return 0;
        }

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                _sortedSetStore.TryRemove(key, out _);
                return 0;
            }

            return entry.MemberScores.Count;
        }
    }

    /// <inheritdoc/>
    public async Task<double> SortedSetIncrementAsync(
        string key,
        string member,
        double increment,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var entry = _sortedSetStore.GetOrAdd(key, _ => new InMemoryStoreData.SortedSetEntry());

        lock (entry.Lock)
        {
            if (IsSortedSetExpired(entry))
            {
                entry.MemberScores.Clear();
            }

            if (!entry.MemberScores.TryGetValue(member, out var currentScore))
            {
                currentScore = 0;
            }

            var newScore = currentScore + increment;
            entry.MemberScores[member] = newScore;

            _logger.LogDebug("Incremented member '{Member}' in sorted set '{Key}' by {Increment} to {NewScore}",
                member, key, increment, newScore);

            return newScore;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetDeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var deleted = _sortedSetStore.TryRemove(key, out _);

        _logger.LogDebug("Deleted sorted set '{Key}' from store '{Store}' (existed: {Existed})",
            key, _storeName, deleted);

        return deleted;
    }

    // ==================== Atomic Counter Operations ====================

    private bool IsCounterExpired(InMemoryStoreData.CounterEntry entry)
    {
        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<long> IncrementAsync(
        string key,
        long increment = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var entry = _counterStore.GetOrAdd(key, _ => new InMemoryStoreData.CounterEntry());

        lock (entry.Lock)
        {
            if (IsCounterExpired(entry))
            {
                entry.SetValue(0);
            }

            var newValue = entry.Increment(increment);

            if (expiresAt.HasValue)
            {
                entry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Incremented counter '{Key}' in store '{Store}' by {Increment} to {Value}",
                key, _storeName, increment, newValue);

            return newValue;
        }
    }

    /// <inheritdoc/>
    public async Task<long> DecrementAsync(
        string key,
        long decrement = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Decrement is just increment with negative value
        return await IncrementAsync(key, -decrement, options, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long?> GetCounterAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_counterStore.TryGetValue(key, out var entry))
        {
            return null;
        }

        lock (entry.Lock)
        {
            if (IsCounterExpired(entry))
            {
                _counterStore.TryRemove(key, out _);
                return null;
            }

            return entry.Value;
        }
    }

    /// <inheritdoc/>
    public async Task SetCounterAsync(
        string key,
        long value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var entry = _counterStore.GetOrAdd(key, _ => new InMemoryStoreData.CounterEntry());

        lock (entry.Lock)
        {
            entry.SetValue(value);

            if (expiresAt.HasValue)
            {
                entry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Set counter '{Key}' in store '{Store}' to {Value}",
                key, _storeName, value);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteCounterAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var deleted = _counterStore.TryRemove(key, out _);

        _logger.LogDebug("Deleted counter '{Key}' from store '{Store}' (existed: {Existed})",
            key, _storeName, deleted);

        return deleted;
    }

    // ==================== Hash Operations ====================

    private bool IsHashExpired(InMemoryStoreData.HashStoreEntry entry)
    {
        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<TField?> HashGetAsync<TField>(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_hashStore.TryGetValue(key, out var entry))
        {
            return default;
        }

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                _hashStore.TryRemove(key, out _);
                return default;
            }

            // Check string fields first
            if (entry.Fields.TryGetValue(field, out var json))
            {
                try
                {
                    return BannouJson.Deserialize<TField>(json);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                    _logger.LogError(ex, "JSON deserialization failed for hash field '{Field}' in hash '{Key}' in store '{Store}' - data may be corrupted", field, key, _storeName);
                    return default;
                }
            }

            // Check numeric fields (used by HashIncrementAsync)
            if (entry.NumericFields.TryGetValue(field, out var numValue))
            {
                try
                {
                    // Serialize then deserialize to convert to TField
                    var numJson = BannouJson.Serialize(numValue);
                    return BannouJson.Deserialize<TField>(numJson);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                    _logger.LogError(ex, "JSON deserialization failed for numeric hash field '{Field}' in hash '{Key}' in store '{Store}' - data may be corrupted", field, key, _storeName);
                    return default;
                }
            }

            return default;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HashSetAsync<TField>(
        string key,
        string field,
        TField value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var json = BannouJson.Serialize(value);
        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var entry = _hashStore.GetOrAdd(key, _ => new InMemoryStoreData.HashStoreEntry());

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                entry.Fields.Clear();
                entry.NumericFields.Clear();
            }

            var isNew = !entry.Fields.ContainsKey(field);
            entry.Fields[field] = json;

            // Remove from numeric fields if it was there (value changed to non-numeric)
            entry.NumericFields.Remove(field);

            if (expiresAt.HasValue)
            {
                entry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Set hash field '{Field}' in hash '{Key}' in store '{Store}' (new: {IsNew})",
                field, key, _storeName, isNew);

            return isNew;
        }
    }

    /// <inheritdoc/>
    public async Task HashSetManyAsync<TField>(
        string key,
        IEnumerable<KeyValuePair<string, TField>> fields,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var fieldList = fields.ToList();
        if (fieldList.Count == 0)
        {
            return;
        }

        DateTimeOffset? expiresAt = options?.Ttl.HasValue == true
            ? DateTimeOffset.UtcNow.AddSeconds(options.Ttl.Value)
            : null;

        var entry = _hashStore.GetOrAdd(key, _ => new InMemoryStoreData.HashStoreEntry());

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                entry.Fields.Clear();
                entry.NumericFields.Clear();
            }

            foreach (var (fieldName, value) in fieldList)
            {
                var json = BannouJson.Serialize(value);
                entry.Fields[fieldName] = json;
                // Remove from numeric fields if it was there
                entry.NumericFields.Remove(fieldName);
            }

            if (expiresAt.HasValue)
            {
                entry.ExpiresAt = expiresAt;
            }

            _logger.LogDebug("Set {Count} hash fields in hash '{Key}' in store '{Store}'",
                fieldList.Count, key, _storeName);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HashDeleteAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_hashStore.TryGetValue(key, out var entry))
        {
            return false;
        }

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                _hashStore.TryRemove(key, out _);
                return false;
            }

            var deleted = entry.Fields.Remove(field);
            entry.NumericFields.Remove(field);

            _logger.LogDebug("Deleted hash field '{Field}' from hash '{Key}' in store '{Store}' (existed: {Existed})",
                field, key, _storeName, deleted);

            return deleted;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HashExistsAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_hashStore.TryGetValue(key, out var entry))
        {
            return false;
        }

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                _hashStore.TryRemove(key, out _);
                return false;
            }

            return entry.Fields.ContainsKey(field) || entry.NumericFields.ContainsKey(field);
        }
    }

    /// <inheritdoc/>
    public async Task<long> HashIncrementAsync(
        string key,
        string field,
        long increment = 1,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var entry = _hashStore.GetOrAdd(key, _ => new InMemoryStoreData.HashStoreEntry());

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                entry.Fields.Clear();
                entry.NumericFields.Clear();
            }

            if (!entry.NumericFields.TryGetValue(field, out var currentValue))
            {
                // Try to parse from string field if exists
                if (entry.Fields.TryGetValue(field, out var json))
                {
                    try
                    {
                        var parsed = BannouJson.Deserialize<long?>(json);
                        currentValue = parsed ?? 0;
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        // IMPLEMENTATION TENETS: Log data corruption as error, default to 0
                        _logger.LogError(ex, "JSON deserialization failed for hash field '{Field}' in hash '{Key}' in store '{Store}' - defaulting to 0", field, key, _storeName);
                        currentValue = 0;
                    }
                }
                else
                {
                    currentValue = 0;
                }
            }

            var newValue = currentValue + increment;
            entry.NumericFields[field] = newValue;

            // Remove from string fields - numeric field takes precedence
            entry.Fields.Remove(field);

            _logger.LogDebug("Incremented hash field '{Field}' in hash '{Key}' in store '{Store}' by {Increment} to {Value}",
                field, key, _storeName, increment, newValue);

            return newValue;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TField>> HashGetAllAsync<TField>(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_hashStore.TryGetValue(key, out var entry))
        {
            return new Dictionary<string, TField>();
        }

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                _hashStore.TryRemove(key, out _);
                return new Dictionary<string, TField>();
            }

            var result = new Dictionary<string, TField>();

            // Get string fields
            foreach (var (fieldName, json) in entry.Fields)
            {
                try
                {
                    var value = BannouJson.Deserialize<TField>(json);
                    if (value != null)
                    {
                        result[fieldName] = value;
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // IMPLEMENTATION TENETS: Log data corruption as error and skip the field
                    _logger.LogError(ex, "JSON deserialization failed for hash field '{Field}' in hash '{Key}' in store '{Store}' - skipping corrupted field", fieldName, key, _storeName);
                }
            }

            // Get numeric fields (convert to TField if possible)
            foreach (var (fieldName, numValue) in entry.NumericFields)
            {
                try
                {
                    var json = BannouJson.Serialize(numValue);
                    var value = BannouJson.Deserialize<TField>(json);
                    if (value != null)
                    {
                        result[fieldName] = value;
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // IMPLEMENTATION TENETS: Log data corruption as error and skip the field
                    _logger.LogError(ex, "JSON deserialization failed for numeric hash field '{Field}' in hash '{Key}' in store '{Store}' - skipping corrupted field", fieldName, key, _storeName);
                }
            }

            _logger.LogDebug("Retrieved {Count} fields from hash '{Key}' in store '{Store}'",
                result.Count, key, _storeName);

            return result;
        }
    }

    /// <inheritdoc/>
    public async Task<long> HashCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_hashStore.TryGetValue(key, out var entry))
        {
            return 0;
        }

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                _hashStore.TryRemove(key, out _);
                return 0;
            }

            // Count unique field names across both stores
            var allFields = new HashSet<string>(entry.Fields.Keys);
            foreach (var field in entry.NumericFields.Keys)
            {
                allFields.Add(field);
            }

            return allFields.Count;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteHashAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var deleted = _hashStore.TryRemove(key, out _);

        _logger.LogDebug("Deleted hash '{Key}' from store '{Store}' (existed: {Existed})",
            key, _storeName, deleted);

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshHashTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (!_hashStore.TryGetValue(key, out var entry))
        {
            return false;
        }

        lock (entry.Lock)
        {
            if (IsHashExpired(entry))
            {
                _hashStore.TryRemove(key, out _);
                return false;
            }

            entry.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);

            _logger.LogDebug("Refreshed TTL on hash '{Key}' in store '{Store}' to {Ttl}s",
                key, _storeName, ttlSeconds);

            return true;
        }
    }
}
