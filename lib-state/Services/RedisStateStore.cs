#nullable enable

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
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        _defaultTtl = defaultTtl;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string GetFullKey(string key) => $"{_keyPrefix}:{key}";
    private string GetMetaKey(string key) => $"{_keyPrefix}:{key}:meta";

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

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
    public async Task<bool> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(etag);

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        // Check current version
        var currentVersion = await _database.HashGetAsync(metaKey, "version");
        if (currentVersion.ToString() != etag)
        {
            _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                key, _keyPrefix, etag, currentVersion.ToString());
            return false;
        }

        // Perform optimistic update
        var json = BannouJson.Serialize(value);
        var transaction = _database.CreateTransaction();

        // Add condition for optimistic concurrency
        transaction.AddCondition(Condition.HashEqual(metaKey, "version", etag));

        _ = transaction.StringSetAsync(fullKey, json);
        _ = transaction.HashIncrementAsync(metaKey, "version", 1);
        _ = transaction.HashSetAsync(metaKey, new HashEntry[]
        {
            new("updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        });

        var success = await transaction.ExecuteAsync();

        if (success)
        {
            _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _keyPrefix);
        }
        else
        {
            _logger.LogDebug("Optimistic save failed (concurrent modification) for key '{Key}' in store '{Store}'",
                key, _keyPrefix);
        }

        return success;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);

        var fullKey = GetFullKey(key);
        return await _database.KeyExistsAsync(fullKey);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

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

    // ==================== Set Operations ====================

    private string GetSetKey(string key) => $"{_keyPrefix}:set:{key}";

    /// <inheritdoc/>
    public async Task<bool> AddToSetAsync<TItem>(
        string key,
        TItem item,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(item);

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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(items);

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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(item);

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
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(item);

        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);
        return await _database.SetContainsAsync(setKey, json);
    }

    /// <inheritdoc/>
    public async Task<long> SetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var setKey = GetSetKey(key);
        return await _database.SetLengthAsync(setKey);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);

        var setKey = GetSetKey(key);
        var ttl = TimeSpan.FromSeconds(ttlSeconds);
        var updated = await _database.KeyExpireAsync(setKey, ttl);

        _logger.LogDebug("Refreshed TTL on set '{Key}' in store '{Store}' to {Ttl}s (existed: {Existed})",
            key, _keyPrefix, ttlSeconds, updated);

        return updated;
    }
}
