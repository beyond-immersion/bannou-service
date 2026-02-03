#nullable enable

using BeyondImmersion.Bannou.Core;
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
/// Implements ICacheableStateStore for Set and Sorted Set operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class InMemoryStateStore<TValue> : ICacheableStateStore<TValue>
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

    // Shared set store for set operations
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SetEntry>> _allSetStores = new();

    // Shared sorted set store for sorted set operations
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SortedSetEntry>> _allSortedSetStores = new();

    // Shared counter store for atomic counter operations
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CounterEntry>> _allCounterStores = new();

    // Shared hash store for hash operations
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HashStoreEntry>> _allHashStores = new();

    /// <summary>
    /// Entry for a set in the in-memory store.
    /// </summary>
    private sealed class SetEntry
    {
        public HashSet<string> Items { get; } = new();
        public DateTimeOffset? ExpiresAt { get; set; }
        public readonly object Lock = new();
    }

    /// <summary>
    /// Entry for a sorted set in the in-memory store.
    /// Uses a dictionary for member->score lookup and a sorted list for ordered access.
    /// </summary>
    private sealed class SortedSetEntry
    {
        // Member -> Score mapping for O(1) lookup
        public Dictionary<string, double> MemberScores { get; } = new();
        public DateTimeOffset? ExpiresAt { get; set; }
        public readonly object Lock = new();
    }

    /// <summary>
    /// Entry for an atomic counter in the in-memory store.
    /// </summary>
    private sealed class CounterEntry
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
    private sealed class HashStoreEntry
    {
        // Field -> JSON value mapping
        public Dictionary<string, string> Fields { get; } = new();
        // Field -> numeric value for increment operations
        public Dictionary<string, long> NumericFields { get; } = new();
        public DateTimeOffset? ExpiresAt { get; set; }
        public readonly object Lock = new();
    }

    private readonly ConcurrentDictionary<string, StoreEntry> _store;
    private readonly ConcurrentDictionary<string, SetEntry> _setStore;
    private readonly ConcurrentDictionary<string, SortedSetEntry> _sortedSetStore;
    private readonly ConcurrentDictionary<string, CounterEntry> _counterStore;
    private readonly ConcurrentDictionary<string, HashStoreEntry> _hashStore;

    /// <summary>
    /// Creates a new in-memory state store.
    /// </summary>
    /// <param name="storeName">Store name for namespacing.</param>
    /// <param name="logger">Logger instance.</param>
    public InMemoryStateStore(
        string storeName,
        ILogger<InMemoryStateStore<TValue>> logger)
    {
        _storeName = storeName;
        _logger = logger;

        // Get or create the store for this name
        _store = _allStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, StoreEntry>());
        _setStore = _allSetStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, SetEntry>());
        _sortedSetStore = _allSortedSetStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, SortedSetEntry>());
        _counterStore = _allCounterStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, CounterEntry>());
        _hashStore = _allHashStores.GetOrAdd(storeName, _ => new ConcurrentDictionary<string, HashStoreEntry>());

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

            return BannouJson.Deserialize<TValue>(entry.Json);
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

            var value = BannouJson.Deserialize<TValue>(entry.Json);
            return (value, entry.Version.ToString());
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

        if (!long.TryParse(etag, out var expectedVersion))
        {
            _logger.LogDebug("Invalid ETag format for key '{Key}' in store '{Store}'", key, _storeName);
            return null;
        }

        var json = BannouJson.Serialize(value);

        // Use CompareExchange pattern for optimistic concurrency
        if (_store.TryGetValue(key, out var existing))
        {
            if (existing.Version != expectedVersion)
            {
                _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                    key, _storeName, etag, existing.Version);
                return null;
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
                return newEntry.Version.ToString();
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
                var value = BannouJson.Deserialize<TValue>(entry.Json);
                if (value != null)
                {
                    result[key] = value;
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

    // ==================== Set Operations ====================

    private bool IsSetExpired(SetEntry entry)
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

        var entry = _setStore.GetOrAdd(key, _ => new SetEntry());

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

        var entry = _setStore.GetOrAdd(key, _ => new SetEntry());

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
                var item = BannouJson.Deserialize<TItem>(json);
                if (item != null)
                {
                    result.Add(item);
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

    private bool IsSortedSetExpired(SortedSetEntry entry)
    {
        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Get members ordered by score for ranking operations.
    /// </summary>
    private IReadOnlyList<(string member, double score)> GetOrderedMembers(SortedSetEntry entry, bool descending)
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

        var entry = _sortedSetStore.GetOrAdd(key, _ => new SortedSetEntry());

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

        var sortedSetEntry = _sortedSetStore.GetOrAdd(key, _ => new SortedSetEntry());

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

        var entry = _sortedSetStore.GetOrAdd(key, _ => new SortedSetEntry());

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

    private bool IsCounterExpired(CounterEntry entry)
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

        var entry = _counterStore.GetOrAdd(key, _ => new CounterEntry());

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

        var entry = _counterStore.GetOrAdd(key, _ => new CounterEntry());

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

    private bool IsHashExpired(HashStoreEntry entry)
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
                return BannouJson.Deserialize<TField>(json);
            }

            // Check numeric fields (used by HashIncrementAsync)
            if (entry.NumericFields.TryGetValue(field, out var numValue))
            {
                // Serialize then deserialize to convert to TField
                var numJson = BannouJson.Serialize(numValue);
                return BannouJson.Deserialize<TField>(numJson);
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

        var entry = _hashStore.GetOrAdd(key, _ => new HashStoreEntry());

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

        var entry = _hashStore.GetOrAdd(key, _ => new HashStoreEntry());

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

        var entry = _hashStore.GetOrAdd(key, _ => new HashStoreEntry());

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
                    var parsed = BannouJson.Deserialize<long?>(json);
                    currentValue = parsed ?? 0;
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
                var value = BannouJson.Deserialize<TField>(json);
                if (value != null)
                {
                    result[fieldName] = value;
                }
            }

            // Get numeric fields (convert to TField if possible)
            foreach (var (fieldName, numValue) in entry.NumericFields)
            {
                var json = BannouJson.Serialize(numValue);
                var value = BannouJson.Deserialize<TField>(json);
                if (value != null)
                {
                    result[fieldName] = value;
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
