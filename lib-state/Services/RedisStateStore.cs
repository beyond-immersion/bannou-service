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
}
