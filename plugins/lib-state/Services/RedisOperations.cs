#nullable enable

using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Redis implementation of low-level operations.
/// Shares the ConnectionMultiplexer with StateStoreFactory for efficiency.
/// </summary>
internal sealed class RedisOperations : IRedisOperations
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisOperations> _logger;

    /// <summary>
    /// Creates a new RedisOperations instance.
    /// This is internal - only StateStoreFactory should create instances.
    /// </summary>
    /// <param name="database">Redis database from shared ConnectionMultiplexer.</param>
    /// <param name="logger">Logger instance.</param>
    internal RedisOperations(IDatabase database, ILogger<RedisOperations> logger)
    {
        _database = database;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RedisResult> ScriptEvaluateAsync(
        string script,
        RedisKey[] keys,
        RedisValue[] values,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing Lua script with {KeyCount} keys", keys.Length);
        return await _database.ScriptEvaluateAsync(script, keys, values);
    }

    /// <inheritdoc/>
    public async Task<long> IncrementAsync(
        string key,
        long value = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await _database.StringIncrementAsync(key, value);
        _logger.LogDebug("INCRBY {Key} {Value} = {Result}", key, value, result);
        return result;
    }

    /// <inheritdoc/>
    public async Task<long> DecrementAsync(
        string key,
        long value = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await _database.StringDecrementAsync(key, value);
        _logger.LogDebug("DECRBY {Key} {Value} = {Result}", key, value, result);
        return result;
    }

    /// <inheritdoc/>
    public async Task<RedisValue> HashGetAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        return await _database.HashGetAsync(key, field);
    }

    /// <inheritdoc/>
    public async Task HashSetAsync(
        string key,
        string field,
        RedisValue value,
        CancellationToken cancellationToken = default)
    {
        await _database.HashSetAsync(key, field, value);
    }

    /// <inheritdoc/>
    public async Task HashSetAsync(
        string key,
        HashEntry[] entries,
        CancellationToken cancellationToken = default)
    {
        await _database.HashSetAsync(key, entries);
    }

    /// <inheritdoc/>
    public async Task<bool> HashDeleteAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        return await _database.HashDeleteAsync(key, field);
    }

    /// <inheritdoc/>
    public async Task<long> HashIncrementAsync(
        string key,
        string field,
        long value = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await _database.HashIncrementAsync(key, field, value);
        _logger.LogDebug("HINCRBY {Key} {Field} {Value} = {Result}", key, field, value, result);
        return result;
    }

    /// <inheritdoc/>
    public async Task<HashEntry[]> HashGetAllAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _database.HashGetAllAsync(key);
    }

    /// <inheritdoc/>
    public async Task<bool> ExpireAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        return await _database.KeyExpireAsync(key, expiry);
    }

    /// <inheritdoc/>
    public async Task<TimeSpan?> TimeToLiveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _database.KeyTimeToLiveAsync(key);
    }

    /// <inheritdoc/>
    public async Task<bool> PersistAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _database.KeyPersistAsync(key);
    }

    /// <inheritdoc/>
    public async Task<bool> KeyExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _database.KeyExistsAsync(key);
    }

    /// <inheritdoc/>
    public async Task<bool> KeyDeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _database.KeyDeleteAsync(key);
    }

    /// <inheritdoc/>
    public IDatabase GetDatabase() => _database;
}
