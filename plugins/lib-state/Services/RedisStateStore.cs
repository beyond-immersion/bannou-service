#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Redis-backed state store for ephemeral/session data.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class RedisStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly TimeSpan? _defaultTtl;
    private readonly ILogger<RedisStateStore<TValue>> _logger;

    /// <summary>
    /// Creates a new Redis state store.
    /// </summary>
    /// <param name="database">Redis database connection.</param>
    /// <param name="keyPrefix">Key prefix for namespacing.</param>
    /// <param name="defaultTtl">Default TTL for entries (null = no expiration).</param>
    /// <param name="logger">Logger instance.</param>
    public RedisStateStore(
        IDatabase database,
        string keyPrefix,
        TimeSpan? defaultTtl,
        ILogger<RedisStateStore<TValue>> logger)
    {
        _database = database;
        _keyPrefix = keyPrefix;
        _defaultTtl = defaultTtl;
        _logger = logger;
    }

    private string GetFullKey(string key) => $"{_keyPrefix}:{key}";
    private string GetMetaKey(string key) => $"{_keyPrefix}:{key}:meta";

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var value = await _database.StringGetAsync(fullKey);

        if (value.IsNullOrEmpty)
        {
            _logger.LogDebug("Key '{Key}' not found in store '{Store}'", key, _keyPrefix);
            return null;
        }

        return BannouJson.Deserialize<TValue>(value!);
    }

    /// <inheritdoc/>
    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        // Pipeline both gets for efficiency
        var valueTask = _database.StringGetAsync(fullKey);
        var versionTask = _database.HashGetAsync(metaKey, "version");

        await Task.WhenAll(valueTask, versionTask);

        var value = await valueTask;
        var version = await versionTask;

        if (value.IsNullOrEmpty)
        {
            return (null, null);
        }

        var etag = version.HasValue ? version.ToString() : "0";
        return (BannouJson.Deserialize<TValue>(value!), etag);
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);
        var json = BannouJson.Serialize(value);
        // Convert int? TTL (seconds) to TimeSpan?
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        // Use transaction for atomicity
        var transaction = _database.CreateTransaction();

        // Set the value
        if (ttl.HasValue)
        {
            _ = transaction.StringSetAsync(fullKey, json, ttl.Value);
        }
        else
        {
            _ = transaction.StringSetAsync(fullKey, json);
        }

        // Update metadata
        var newVersion = transaction.HashIncrementAsync(metaKey, "version", 1);
        _ = transaction.HashSetAsync(metaKey, new HashEntry[]
        {
            new("updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        });

        // Set TTL on metadata too
        if (ttl.HasValue)
        {
            _ = transaction.KeyExpireAsync(metaKey, ttl);
        }

        await transaction.ExecuteAsync();

        var version = await newVersion;
        _logger.LogDebug("Saved key '{Key}' in store '{Store}' (version: {Version})",
            key, _keyPrefix, version);

        return version.ToString();
    }

    /// <inheritdoc/>
    public async Task<string?> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);
        var json = BannouJson.Serialize(value);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Empty etag means "create new entry if it doesn't exist"
        if (string.IsNullOrEmpty(etag))
        {
            // Check if key already exists
            var exists = await _database.KeyExistsAsync(fullKey);
            if (exists)
            {
                _logger.LogDebug("Key '{Key}' already exists in store '{Store}' but empty etag provided (concurrent create)",
                    key, _keyPrefix);
                return null;
            }

            // Use transaction to atomically create only if key doesn't exist
            var createTransaction = _database.CreateTransaction();
            createTransaction.AddCondition(Condition.KeyNotExists(fullKey));

            _ = createTransaction.StringSetAsync(fullKey, json);
            _ = createTransaction.HashSetAsync(metaKey, new HashEntry[]
            {
                new("version", 1),
                new("created", now),
                new("updated", now)
            });

            var createSuccess = await createTransaction.ExecuteAsync();
            if (createSuccess)
            {
                _logger.LogDebug("Created new key '{Key}' in store '{Store}'", key, _keyPrefix);
                return "1";
            }
            else
            {
                _logger.LogDebug("Concurrent create conflict for key '{Key}' in store '{Store}'", key, _keyPrefix);
                return null;
            }
        }

        // Non-empty etag means "update existing entry with matching version"
        var currentVersion = await _database.HashGetAsync(metaKey, "version");
        if (currentVersion.ToString() != etag)
        {
            _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                key, _keyPrefix, etag, currentVersion.ToString());
            return null;
        }

        // Perform optimistic update
        var transaction = _database.CreateTransaction();
        transaction.AddCondition(Condition.HashEqual(metaKey, "version", etag));

        _ = transaction.StringSetAsync(fullKey, json);
        _ = transaction.HashIncrementAsync(metaKey, "version", 1);
        _ = transaction.HashSetAsync(metaKey, new HashEntry[]
        {
            new("updated", now)
        });

        var success = await transaction.ExecuteAsync();

        if (success)
        {
            var newVersion = long.Parse(etag) + 1;
            _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _keyPrefix);
            return newVersion.ToString();
        }
        else
        {
            _logger.LogDebug("Optimistic save failed (concurrent modification) for key '{Key}' in store '{Store}'",
                key, _keyPrefix);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        // Delete both value and metadata
        var valueDeleted = await _database.KeyDeleteAsync(fullKey);
        await _database.KeyDeleteAsync(metaKey);

        _logger.LogDebug("Deleted key '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, valueDeleted);

        return valueDeleted;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        return await _database.KeyExistsAsync(fullKey);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {

        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return new Dictionary<string, TValue>();
        }

        // Create RedisKey array for MGET
        var redisKeys = keyList.Select(k => (RedisKey)GetFullKey(k)).ToArray();
        var values = await _database.StringGetAsync(redisKeys);

        var result = new Dictionary<string, TValue>();
        for (var i = 0; i < keyList.Count; i++)
        {
            if (!values[i].IsNullOrEmpty)
            {
                var deserialized = BannouJson.Deserialize<TValue>(values[i]!);
                if (deserialized != null)
                {
                    result[keyList[i]] = deserialized;
                }
            }
        }

        _logger.LogDebug("Bulk get {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _keyPrefix, result.Count);

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

        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Use a transaction for atomicity
        var transaction = _database.CreateTransaction();
        var versionTasks = new List<(string Key, Task<long> VersionTask)>();

        foreach (var (key, value) in itemList)
        {
            var fullKey = GetFullKey(key);
            var metaKey = GetMetaKey(key);
            var json = BannouJson.Serialize(value);

            // Set the value
            if (ttl.HasValue)
            {
                _ = transaction.StringSetAsync(fullKey, json, ttl.Value);
            }
            else
            {
                _ = transaction.StringSetAsync(fullKey, json);
            }

            // Update metadata
            var versionTask = transaction.HashIncrementAsync(metaKey, "version", 1);
            _ = transaction.HashSetAsync(metaKey, new HashEntry[]
            {
                new("updated", now)
            });

            // Set TTL on metadata too
            if (ttl.HasValue)
            {
                _ = transaction.KeyExpireAsync(metaKey, ttl);
            }

            versionTasks.Add((key, versionTask));
        }

        await transaction.ExecuteAsync();

        var result = new Dictionary<string, string>();
        foreach (var (key, versionTask) in versionTasks)
        {
            result[key] = (await versionTask).ToString();
        }

        _logger.LogDebug("Bulk save {Count} items to store '{Store}'", itemList.Count, _keyPrefix);
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

        // Pipeline exists checks for efficiency
        var tasks = keyList.Select(k => _database.KeyExistsAsync(GetFullKey(k))).ToArray();
        var results = await Task.WhenAll(tasks);

        var existing = new HashSet<string>();
        for (var i = 0; i < keyList.Count; i++)
        {
            if (results[i])
            {
                existing.Add(keyList[i]);
            }
        }

        _logger.LogDebug("Bulk exists check {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _keyPrefix, existing.Count);
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

        // Delete both value keys and metadata keys
        var allKeys = new List<RedisKey>();
        foreach (var key in keyList)
        {
            allKeys.Add(GetFullKey(key));
            allKeys.Add(GetMetaKey(key));
        }

        var totalDeleted = await _database.KeyDeleteAsync(allKeys.ToArray());
        // Each logical delete is 2 keys (value + meta), return logical count
        // But only count those that actually existed (value key)
        var deletedCount = (int)(totalDeleted / 2);

        _logger.LogDebug("Bulk delete {RequestedCount} keys from store '{Store}', deleted {DeletedCount}",
            keyList.Count, _keyPrefix, deletedCount);
        return deletedCount;
    }

    // ==================== Set Operations ====================

    private string GetSetKey(string key) => $"{_keyPrefix}:set:{key}";

    /// <inheritdoc/>
    public async Task<bool> AddToSetAsync<TItem>(
        string key,
        TItem item,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);
        var added = await _database.SetAddAsync(setKey, json);

        // Apply TTL if specified
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(setKey, ttl);
        }

        _logger.LogDebug("Added item to set '{Key}' in store '{Store}' (new: {IsNew})",
            key, _keyPrefix, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<long> AddToSetAsync<TItem>(
        string key,
        IEnumerable<TItem> items,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return 0;
        }

        var setKey = GetSetKey(key);
        var values = itemList.Select(item => (RedisValue)BannouJson.Serialize(item)).ToArray();
        var added = await _database.SetAddAsync(setKey, values);

        // Apply TTL if specified
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(setKey, ttl);
        }

        _logger.LogDebug("Added {Count} items to set '{Key}' in store '{Store}' (new: {Added})",
            itemList.Count, key, _keyPrefix, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveFromSetAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);
        var removed = await _database.SetRemoveAsync(setKey, json);

        _logger.LogDebug("Removed item from set '{Key}' in store '{Store}' (existed: {Existed})",
            key, _keyPrefix, removed);

        return removed;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TItem>> GetSetAsync<TItem>(
        string key,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var members = await _database.SetMembersAsync(setKey);

        if (members.Length == 0)
        {
            _logger.LogDebug("Set '{Key}' is empty or not found in store '{Store}'", key, _keyPrefix);
            return Array.Empty<TItem>();
        }

        var result = new List<TItem>(members.Length);
        foreach (var member in members)
        {
            if (!member.IsNullOrEmpty)
            {
                var item = BannouJson.Deserialize<TItem>(member!);
                if (item != null)
                {
                    result.Add(item);
                }
            }
        }

        _logger.LogDebug("Retrieved {Count} items from set '{Key}' in store '{Store}'",
            result.Count, key, _keyPrefix);

        return result;
    }

    /// <inheritdoc/>
    public async Task<bool> SetContainsAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);
        return await _database.SetContainsAsync(setKey, json);
    }

    /// <inheritdoc/>
    public async Task<long> SetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        return await _database.SetLengthAsync(setKey);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var deleted = await _database.KeyDeleteAsync(setKey);

        _logger.LogDebug("Deleted set '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, deleted);

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshSetTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var ttl = TimeSpan.FromSeconds(ttlSeconds);
        var updated = await _database.KeyExpireAsync(setKey, ttl);

        _logger.LogDebug("Refreshed TTL on set '{Key}' in store '{Store}' to {Ttl}s (existed: {Existed})",
            key, _keyPrefix, ttlSeconds, updated);

        return updated;
    }

    // ==================== Sorted Set Operations ====================

    private string GetSortedSetKey(string key) => $"{_keyPrefix}:zset:{key}";

    /// <inheritdoc/>
    public async Task<bool> SortedSetAddAsync(
        string key,
        string member,
        double score,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);
        var added = await _database.SortedSetAddAsync(sortedSetKey, member, score);

        // Apply TTL if specified
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(sortedSetKey, ttl);
        }

        _logger.LogDebug("Added member '{Member}' to sorted set '{Key}' with score {Score} (new: {IsNew})",
            member, key, score, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetAddBatchAsync(
        string key,
        IEnumerable<(string member, double score)> entries,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return 0;
        }

        var sortedSetKey = GetSortedSetKey(key);
        var sortedSetEntries = entryList
            .Select(e => new SortedSetEntry(e.member, e.score))
            .ToArray();

        var added = await _database.SortedSetAddAsync(sortedSetKey, sortedSetEntries);

        // Apply TTL if specified
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(sortedSetKey, ttl);
        }

        _logger.LogDebug("Added {Count} members to sorted set '{Key}' (new: {Added})",
            entryList.Count, key, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetRemoveAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);
        var removed = await _database.SortedSetRemoveAsync(sortedSetKey, member);

        _logger.LogDebug("Removed member '{Member}' from sorted set '{Key}' (existed: {Existed})",
            member, key, removed);

        return removed;
    }

    /// <inheritdoc/>
    public async Task<double?> SortedSetScoreAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);
        var score = await _database.SortedSetScoreAsync(sortedSetKey, member);

        return score;
    }

    /// <inheritdoc/>
    public async Task<long?> SortedSetRankAsync(
        string key,
        string member,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);

        // ZRANK returns rank in ascending order (lowest score = rank 0)
        // ZREVRANK returns rank in descending order (highest score = rank 0)
        var rank = descending
            ? await _database.SortedSetRankAsync(sortedSetKey, member, Order.Descending)
            : await _database.SortedSetRankAsync(sortedSetKey, member, Order.Ascending);

        return rank;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByRankAsync(
        string key,
        long start,
        long stop,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);

        // ZRANGE with WITHSCORES - use REV for descending
        var entries = descending
            ? await _database.SortedSetRangeByRankWithScoresAsync(sortedSetKey, start, stop, Order.Descending)
            : await _database.SortedSetRangeByRankWithScoresAsync(sortedSetKey, start, stop, Order.Ascending);

        var result = entries
            .Select(e => (member: e.Element.ToString(), score: e.Score))
            .ToList();

        _logger.LogDebug("Retrieved {Count} entries from sorted set '{Key}' (range: {Start}-{Stop}, descending: {Descending})",
            result.Count, key, start, stop, descending);

        return result;
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
        var sortedSetKey = GetSortedSetKey(key);

        // ZRANGEBYSCORE with LIMIT offset count
        // For descending, we use ZREVRANGEBYSCORE (swap min/max)
        var entries = descending
            ? await _database.SortedSetRangeByScoreWithScoresAsync(
                sortedSetKey,
                minScore,
                maxScore,
                Exclude.None,
                Order.Descending,
                offset,
                count)
            : await _database.SortedSetRangeByScoreWithScoresAsync(
                sortedSetKey,
                minScore,
                maxScore,
                Exclude.None,
                Order.Ascending,
                offset,
                count);

        var result = entries
            .Select(e => (member: e.Element.ToString(), score: e.Score))
            .ToList();

        _logger.LogDebug(
            "Retrieved {Count} entries from sorted set '{Key}' by score (min: {Min}, max: {Max}, offset: {Offset}, count: {RequestedCount}, descending: {Descending})",
            result.Count, key, minScore, maxScore, offset, count, descending);

        return result;
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);
        return await _database.SortedSetLengthAsync(sortedSetKey);
    }

    /// <inheritdoc/>
    public async Task<double> SortedSetIncrementAsync(
        string key,
        string member,
        double increment,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);
        var newScore = await _database.SortedSetIncrementAsync(sortedSetKey, member, increment);

        _logger.LogDebug("Incremented member '{Member}' in sorted set '{Key}' by {Increment} (new score: {NewScore})",
            member, key, increment, newScore);

        return newScore;
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetDeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var sortedSetKey = GetSortedSetKey(key);
        var deleted = await _database.KeyDeleteAsync(sortedSetKey);

        _logger.LogDebug("Deleted sorted set '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, deleted);

        return deleted;
    }
}
