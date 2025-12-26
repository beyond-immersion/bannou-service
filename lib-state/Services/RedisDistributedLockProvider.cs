#nullable enable

using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Redis-based distributed lock provider using SET NX EX pattern.
/// Provides reliable distributed locking without Dapr dependencies.
/// </summary>
public sealed class RedisDistributedLockProvider : IDistributedLockProvider, IAsyncDisposable
{
    private readonly StateStoreFactoryConfiguration _configuration;
    private readonly ILogger<RedisDistributedLockProvider> _logger;
    private ConnectionMultiplexer? _redis;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Creates a new Redis distributed lock provider.
    /// </summary>
    /// <param name="configuration">State store configuration containing Redis connection string.</param>
    /// <param name="logger">Logger instance.</param>
    public RedisDistributedLockProvider(
        StateStoreFactoryConfiguration configuration,
        ILogger<RedisDistributedLockProvider> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private async Task<IDatabase> EnsureInitializedAsync()
    {
        if (_initialized && _redis != null)
        {
            return _redis.GetDatabase();
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized && _redis != null)
            {
                return _redis.GetDatabase();
            }

            _logger.LogInformation("Initializing Redis connection for distributed locks");
            _redis = await ConnectionMultiplexer.ConnectAsync(_configuration.RedisConnectionString);
            _initialized = true;
            _logger.LogInformation("Redis connection established for distributed locks");

            return _redis.GetDatabase();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ILockResponse> LockAsync(
        string storeName,
        string resourceId,
        string lockOwner,
        int expiryInSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storeName);
        ArgumentNullException.ThrowIfNull(resourceId);
        ArgumentNullException.ThrowIfNull(lockOwner);

        var database = await EnsureInitializedAsync();
        var lockKey = $"{storeName}:lock:{resourceId}";
        var lockValue = $"{lockOwner}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var expiry = TimeSpan.FromSeconds(expiryInSeconds);

        try
        {
            // Use SET NX EX pattern for atomic lock acquisition
            // NX = only set if not exists, EX = set expiration
            var acquired = await database.StringSetAsync(
                lockKey,
                lockValue,
                expiry,
                When.NotExists);

            if (acquired)
            {
                _logger.LogDebug("Successfully acquired lock {LockKey} for owner {Owner}", lockKey, lockOwner);
                return new RedisLockResponse(true, database, lockKey, lockOwner, _logger);
            }

            // Lock exists - check if it's expired (shouldn't happen with TTL, but defensive)
            var existingValue = await database.StringGetAsync(lockKey);
            _logger.LogDebug("Lock {LockKey} is held by {Value}", lockKey, existingValue);

            return new RedisLockResponse(false, database, lockKey, lockOwner, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock {LockKey}", lockKey);
            return new RedisLockResponse(false, database, lockKey, lockOwner, _logger);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.DisposeAsync();
            _redis = null;
        }
        _initLock.Dispose();
    }
}

/// <summary>
/// Redis-based lock response that releases lock on disposal.
/// Uses Lua script for safe unlock (only release if we own it).
/// </summary>
internal sealed class RedisLockResponse : ILockResponse
{
    private readonly IDatabase _database;
    private readonly string _lockKey;
    private readonly string _lockOwner;
    private readonly ILogger _logger;
    private bool _disposed;

    // Lua script for safe unlock - only delete if the value matches our owner prefix
    private static readonly string UnlockScript = @"
        local value = redis.call('GET', KEYS[1])
        if value and string.find(value, ARGV[1]) == 1 then
            return redis.call('DEL', KEYS[1])
        end
        return 0
    ";

    public RedisLockResponse(bool success, IDatabase database, string lockKey, string lockOwner, ILogger logger)
    {
        Success = success;
        _database = database;
        _lockKey = lockKey;
        _lockOwner = lockOwner;
        _logger = logger;
    }

    public bool Success { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed || !Success) return;
        _disposed = true;

        try
        {
            // Use Lua script to safely release lock only if we own it
            var result = await _database.ScriptEvaluateAsync(
                UnlockScript,
                new RedisKey[] { _lockKey },
                new RedisValue[] { _lockOwner });

            var deleted = (int)result;
            if (deleted > 0)
            {
                _logger.LogDebug("Released lock {LockKey} for owner {Owner}", _lockKey, _lockOwner);
            }
            else
            {
                _logger.LogWarning("Lock {LockKey} was not released (already expired or stolen)", _lockKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error releasing lock {LockKey}", _lockKey);
        }
    }
}
