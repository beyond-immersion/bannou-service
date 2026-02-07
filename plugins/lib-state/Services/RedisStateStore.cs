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
/// Implements ICacheableStateStore for Set and Sorted Set operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class RedisStateStore<TValue> : ICacheableStateStore<TValue>
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

        try
        {
            var value = await _database.StringGetAsync(fullKey);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Key '{Key}' not found in store '{Store}'", key, _keyPrefix);
                return null;
            }

            return BannouJson.Deserialize<TValue>(value!);
        }
        catch (RedisConnectionException ex)
        {
            // IMPLEMENTATION TENETS: Log Redis connection failures as errors for monitoring
            _logger.LogError(ex, "Redis connection failed reading key '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout reading key '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed reading key '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout reading key '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
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

        try
        {
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

            // IMPLEMENTATION TENETS: Check transaction result and handle failures
            var executed = await transaction.ExecuteAsync();
            if (!executed)
            {
                // Unconditional transactions should always succeed - log unexpected failure
                _logger.LogError(
                    "Redis transaction unexpectedly failed for key '{Key}' in store '{Store}' - data may be inconsistent",
                    key, _keyPrefix);
                throw new InvalidOperationException($"Redis transaction failed unexpectedly for key '{key}'");
            }

            var version = await newVersion;
            _logger.LogDebug("Saved key '{Key}' in store '{Store}' (version: {Version})",
                key, _keyPrefix, version);

            return version.ToString();
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed saving key '{Key}' to store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout saving key '{Key}' to store '{Store}'", key, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed during optimistic save for key '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout during optimistic save for key '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        try
        {
            // Delete both value and metadata
            var valueDeleted = await _database.KeyDeleteAsync(fullKey);
            await _database.KeyDeleteAsync(metaKey);

            _logger.LogDebug("Deleted key '{Key}' from store '{Store}' (existed: {Existed})",
                key, _keyPrefix, valueDeleted);

            return valueDeleted;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed deleting key '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout deleting key '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);

        try
        {
            return await _database.KeyExistsAsync(fullKey);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed checking existence of key '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout checking existence of key '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed during bulk get of {Count} keys from store '{Store}'", keyList.Count, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout during bulk get of {Count} keys from store '{Store}'", keyList.Count, _keyPrefix);
            throw;
        }
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

        try
        {
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

            // IMPLEMENTATION TENETS: Check transaction result and handle failures
            var executed = await transaction.ExecuteAsync();
            if (!executed)
            {
                // Unconditional transactions should always succeed - log unexpected failure
                _logger.LogError(
                    "Redis bulk transaction unexpectedly failed for store '{Store}' ({Count} items) - data may be inconsistent",
                    _keyPrefix, itemList.Count);
                throw new InvalidOperationException($"Redis bulk transaction failed unexpectedly for {itemList.Count} items");
            }

            var result = new Dictionary<string, string>();
            foreach (var (key, versionTask) in versionTasks)
            {
                result[key] = (await versionTask).ToString();
            }

            _logger.LogDebug("Bulk save {Count} items to store '{Store}'", itemList.Count, _keyPrefix);
            return result;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed during bulk save of {Count} items to store '{Store}'", itemList.Count, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout during bulk save of {Count} items to store '{Store}'", itemList.Count, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed during bulk exists check of {Count} keys in store '{Store}'", keyList.Count, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout during bulk exists check of {Count} keys in store '{Store}'", keyList.Count, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed during bulk delete of {Count} keys from store '{Store}'", keyList.Count, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout during bulk delete of {Count} keys from store '{Store}'", keyList.Count, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed adding item to set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout adding item to set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed adding {Count} items to set '{Key}' in store '{Store}'", itemList.Count, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout adding {Count} items to set '{Key}' in store '{Store}'", itemList.Count, key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveFromSetAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {
        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);

        try
        {
            var removed = await _database.SetRemoveAsync(setKey, json);

            _logger.LogDebug("Removed item from set '{Key}' in store '{Store}' (existed: {Existed})",
                key, _keyPrefix, removed);

            return removed;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed removing item from set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout removing item from set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TItem>> GetSetAsync<TItem>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var setKey = GetSetKey(key);

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting set '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting set '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetContainsAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {
        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);

        try
        {
            return await _database.SetContainsAsync(setKey, json);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed checking set membership for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout checking set membership for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> SetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var setKey = GetSetKey(key);

        try
        {
            return await _database.SetLengthAsync(setKey);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting set count for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting set count for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var setKey = GetSetKey(key);

        try
        {
            var deleted = await _database.KeyDeleteAsync(setKey);

            _logger.LogDebug("Deleted set '{Key}' from store '{Store}' (existed: {Existed})",
                key, _keyPrefix, deleted);

            return deleted;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed deleting set '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout deleting set '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshSetTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        var setKey = GetSetKey(key);
        var ttl = TimeSpan.FromSeconds(ttlSeconds);

        try
        {
            var updated = await _database.KeyExpireAsync(setKey, ttl);

            _logger.LogDebug("Refreshed TTL on set '{Key}' in store '{Store}' to {Ttl}s (existed: {Existed})",
                key, _keyPrefix, ttlSeconds, updated);

            return updated;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed refreshing TTL on set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout refreshing TTL on set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed adding member to sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout adding member to sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
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

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed adding {Count} members to sorted set '{Key}' in store '{Store}'", entryList.Count, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout adding {Count} members to sorted set '{Key}' in store '{Store}'", entryList.Count, key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetRemoveAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
            var removed = await _database.SortedSetRemoveAsync(sortedSetKey, member);

            _logger.LogDebug("Removed member '{Member}' from sorted set '{Key}' (existed: {Existed})",
                member, key, removed);

            return removed;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed removing member from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout removing member from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<double?> SortedSetScoreAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
            return await _database.SortedSetScoreAsync(sortedSetKey, member);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting score from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting score from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long?> SortedSetRankAsync(
        string key,
        string member,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
            // ZRANK returns rank in ascending order (lowest score = rank 0)
            // ZREVRANK returns rank in descending order (highest score = rank 0)
            var rank = descending
                ? await _database.SortedSetRankAsync(sortedSetKey, member, Order.Descending)
                : await _database.SortedSetRankAsync(sortedSetKey, member, Order.Ascending);

            return rank;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting rank from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting rank from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
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
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting range from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting range from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
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
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
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
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting score range from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting score range from sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
            return await _database.SortedSetLengthAsync(sortedSetKey);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting sorted set count for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting sorted set count for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<double> SortedSetIncrementAsync(
        string key,
        string member,
        double increment,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
            var newScore = await _database.SortedSetIncrementAsync(sortedSetKey, member, increment);

            _logger.LogDebug("Incremented member '{Member}' in sorted set '{Key}' by {Increment} (new score: {NewScore})",
                member, key, increment, newScore);

            return newScore;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed incrementing member in sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout incrementing member in sorted set '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetDeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        try
        {
            var deleted = await _database.KeyDeleteAsync(sortedSetKey);

            _logger.LogDebug("Deleted sorted set '{Key}' from store '{Store}' (existed: {Existed})",
                key, _keyPrefix, deleted);

            return deleted;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed deleting sorted set '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout deleting sorted set '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    // ==================== Atomic Counter Operations ====================

    private string GetCounterKey(string key) => $"{_keyPrefix}:counter:{key}";

    /// <inheritdoc/>
    public async Task<long> IncrementAsync(
        string key,
        long increment = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        try
        {
            // INCRBY is atomic - no transaction needed
            var newValue = await _database.StringIncrementAsync(counterKey, increment);

            // Set TTL if specified (separate operation)
            if (ttl.HasValue)
            {
                await _database.KeyExpireAsync(counterKey, ttl);
            }

            _logger.LogDebug("Incremented counter '{Key}' in store '{Store}' by {Increment} to {Value}",
                key, _keyPrefix, increment, newValue);

            return newValue;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed incrementing counter '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout incrementing counter '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> DecrementAsync(
        string key,
        long decrement = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        try
        {
            // DECRBY is atomic - no transaction needed
            var newValue = await _database.StringDecrementAsync(counterKey, decrement);

            // Set TTL if specified (separate operation)
            if (ttl.HasValue)
            {
                await _database.KeyExpireAsync(counterKey, ttl);
            }

            _logger.LogDebug("Decremented counter '{Key}' in store '{Store}' by {Decrement} to {Value}",
                key, _keyPrefix, decrement, newValue);

            return newValue;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed decrementing counter '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout decrementing counter '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long?> GetCounterAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);

        try
        {
            var value = await _database.StringGetAsync(counterKey);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Counter '{Key}' not found in store '{Store}'", key, _keyPrefix);
                return null;
            }

            if (long.TryParse(value, out var result))
            {
                return result;
            }

            _logger.LogWarning("Counter '{Key}' in store '{Store}' has non-numeric value: {Value}",
                key, _keyPrefix, value);
            return null;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting counter '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting counter '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SetCounterAsync(
        string key,
        long value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        try
        {
            if (ttl.HasValue)
            {
                await _database.StringSetAsync(counterKey, value, ttl.Value);
            }
            else
            {
                await _database.StringSetAsync(counterKey, value);
            }

            _logger.LogDebug("Set counter '{Key}' in store '{Store}' to {Value}",
                key, _keyPrefix, value);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed setting counter '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout setting counter '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteCounterAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);

        try
        {
            var deleted = await _database.KeyDeleteAsync(counterKey);

            _logger.LogDebug("Deleted counter '{Key}' from store '{Store}' (existed: {Existed})",
                key, _keyPrefix, deleted);

            return deleted;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed deleting counter '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout deleting counter '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    // ==================== Hash Operations ====================

    private string GetHashKey(string key) => $"{_keyPrefix}:hash:{key}";

    /// <inheritdoc/>
    public async Task<TField?> HashGetAsync<TField>(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            var value = await _database.HashGetAsync(hashKey, field);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Hash field '{Field}' not found in hash '{Key}' in store '{Store}'",
                    field, key, _keyPrefix);
                return default;
            }

            return BannouJson.Deserialize<TField>(value!);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting hash field '{Field}' from hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting hash field '{Field}' from hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
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
        var hashKey = GetHashKey(key);
        var json = BannouJson.Serialize(value);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        try
        {
            // HashSet returns true if field was created (new), false if updated
            var isNew = await _database.HashSetAsync(hashKey, field, json);

            // Set TTL on the hash if specified
            if (ttl.HasValue)
            {
                await _database.KeyExpireAsync(hashKey, ttl);
            }

            _logger.LogDebug("Set hash field '{Field}' in hash '{Key}' in store '{Store}' (new: {IsNew})",
                field, key, _keyPrefix, isNew);

            return isNew;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed setting hash field '{Field}' in hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout setting hash field '{Field}' in hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task HashSetManyAsync<TField>(
        string key,
        IEnumerable<KeyValuePair<string, TField>> fields,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fieldList = fields.ToList();
        if (fieldList.Count == 0)
        {
            return;
        }

        var hashKey = GetHashKey(key);
        var entries = fieldList
            .Select(f => new HashEntry(f.Key, BannouJson.Serialize(f.Value)))
            .ToArray();

        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        try
        {
            await _database.HashSetAsync(hashKey, entries);

            // Set TTL on the hash if specified
            if (ttl.HasValue)
            {
                await _database.KeyExpireAsync(hashKey, ttl);
            }

            _logger.LogDebug("Set {Count} hash fields in hash '{Key}' in store '{Store}'",
                fieldList.Count, key, _keyPrefix);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed setting {Count} hash fields in hash '{Key}' in store '{Store}'", fieldList.Count, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout setting {Count} hash fields in hash '{Key}' in store '{Store}'", fieldList.Count, key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HashDeleteAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            var deleted = await _database.HashDeleteAsync(hashKey, field);

            _logger.LogDebug("Deleted hash field '{Field}' from hash '{Key}' in store '{Store}' (existed: {Existed})",
                field, key, _keyPrefix, deleted);

            return deleted;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed deleting hash field '{Field}' from hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout deleting hash field '{Field}' from hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HashExistsAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            return await _database.HashExistsAsync(hashKey, field);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed checking hash field existence for '{Field}' in hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout checking hash field existence for '{Field}' in hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> HashIncrementAsync(
        string key,
        string field,
        long increment = 1,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            // HINCRBY is atomic
            var newValue = await _database.HashIncrementAsync(hashKey, field, increment);

            _logger.LogDebug("Incremented hash field '{Field}' in hash '{Key}' in store '{Store}' by {Increment} to {Value}",
                field, key, _keyPrefix, increment, newValue);

            return newValue;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed incrementing hash field '{Field}' in hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout incrementing hash field '{Field}' in hash '{Key}' in store '{Store}'", field, key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TField>> HashGetAllAsync<TField>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            var entries = await _database.HashGetAllAsync(hashKey);

            var result = new Dictionary<string, TField>();
            foreach (var entry in entries)
            {
                var fieldValue = BannouJson.Deserialize<TField>(entry.Value!);
                if (fieldValue != null)
                {
                    result[entry.Name!] = fieldValue;
                }
            }

            _logger.LogDebug("Retrieved {Count} fields from hash '{Key}' in store '{Store}'",
                result.Count, key, _keyPrefix);

            return result;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting all hash fields from hash '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting all hash fields from hash '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> HashCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            return await _database.HashLengthAsync(hashKey);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed getting hash count for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout getting hash count for '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteHashAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            var deleted = await _database.KeyDeleteAsync(hashKey);

            _logger.LogDebug("Deleted hash '{Key}' from store '{Store}' (existed: {Existed})",
                key, _keyPrefix, deleted);

            return deleted;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed deleting hash '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout deleting hash '{Key}' from store '{Store}'", key, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshHashTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        try
        {
            var updated = await _database.KeyExpireAsync(hashKey, TimeSpan.FromSeconds(ttlSeconds));

            _logger.LogDebug("Refreshed TTL on hash '{Key}' in store '{Store}' to {Ttl}s (existed: {Existed})",
                key, _keyPrefix, ttlSeconds, updated);

            return updated;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed refreshing TTL on hash '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis timeout refreshing TTL on hash '{Key}' in store '{Store}'", key, _keyPrefix);
            throw;
        }
    }
}
