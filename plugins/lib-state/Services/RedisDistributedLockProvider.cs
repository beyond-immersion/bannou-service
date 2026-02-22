#nullable enable

using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Distributed lock provider using IRedisOperations when available,
/// with in-memory fallback for testing/minimal infrastructure.
/// </summary>
public sealed class RedisDistributedLockProvider : IDistributedLockProvider, IAsyncDisposable
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<RedisDistributedLockProvider> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IRedisOperations? _redisOperations;
    private bool _initialized;
    private bool _useInMemory;

    // In-memory fallback for when Redis is not available
    private static readonly ConcurrentDictionary<string, InMemoryLockEntry> _inMemoryLocks = new();

    /// <summary>
    /// Creates a new distributed lock provider.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for accessing Redis operations.</param>
    /// <param name="logger">Logger instance.</param>
    public RedisDistributedLockProvider(
        IStateStoreFactory stateStoreFactory,
        ILogger<RedisDistributedLockProvider> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            _logger.LogDebug("Initializing distributed lock provider");

            // Get Redis operations from the factory (null if in-memory mode)
            _redisOperations = _stateStoreFactory.GetRedisOperations();

            if (_redisOperations == null)
            {
                _logger.LogInformation("Redis not available, using in-memory lock fallback");
                _useInMemory = true;
            }
            else
            {
                _logger.LogInformation("Using Redis for distributed locks");
                _useInMemory = false;
            }

            _initialized = true;
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
        await EnsureInitializedAsync();

        var lockKey = $"{storeName}:lock:{resourceId}";
        var lockValue = $"{lockOwner}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var expiry = TimeSpan.FromSeconds(expiryInSeconds);

        if (_useInMemory)
        {
            return AcquireInMemoryLock(lockKey, lockValue, lockOwner, expiry);
        }

        return await AcquireRedisLockAsync(lockKey, lockValue, lockOwner, expiry);
    }

    private ILockResponse AcquireInMemoryLock(string lockKey, string lockValue, string lockOwner, TimeSpan expiry)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = _inMemoryLocks.AddOrUpdate(
            lockKey,
            _ => new InMemoryLockEntry(lockOwner, lockValue, now.Add(expiry)),
            (_, existing) =>
            {
                // Check if existing lock is expired
                if (existing.ExpiresAt < now)
                {
                    return new InMemoryLockEntry(lockOwner, lockValue, now.Add(expiry));
                }
                return existing;
            });

        var acquired = entry.Owner == lockOwner && entry.Value == lockValue;
        if (acquired)
        {
            _logger.LogDebug("Successfully acquired in-memory lock {LockKey} for owner {Owner}", lockKey, lockOwner);
        }
        else
        {
            _logger.LogDebug("In-memory lock {LockKey} is held by {Owner}", lockKey, entry.Owner);
        }

        // Pass cleanup callback to remove lock on disposal (only if we acquired it)
        // Capture the exact entry we created so we can do atomic compare-and-remove
        var ourEntry = acquired ? entry : null;
        Action<string, string>? cleanupCallback = acquired
            ? (key, owner) =>
            {
                // Atomic compare-and-remove: only removes if both key AND value match exactly.
                // ConcurrentDictionary implements ICollection<KeyValuePair> with atomic Remove.
                // Since InMemoryLockEntry is a record, value comparison uses structural equality.
                // If someone else acquired the lock after ours expired, their entry will have
                // different Owner/Value/ExpiresAt, so this Remove will safely fail (no-op).
                if (ourEntry != null)
                {
                    ((ICollection<KeyValuePair<string, InMemoryLockEntry>>)_inMemoryLocks)
                        .Remove(new KeyValuePair<string, InMemoryLockEntry>(key, ourEntry));
                }
            }
        : null;

        return new InMemoryLockResponse(acquired, lockKey, lockOwner, _logger, cleanupCallback);
    }

    private async Task<ILockResponse> AcquireRedisLockAsync(string lockKey, string lockValue, string lockOwner, TimeSpan expiry)
    {
        var redisOps = _redisOperations ?? throw new InvalidOperationException(
            "Redis operations not available after initialization completed with Redis mode");
        var database = redisOps.GetDatabase();

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
                return new RedisLockResponse(true, _redisOperations, lockKey, lockOwner, _logger);
            }

            // Lock exists - check if it's expired (shouldn't happen with TTL, but defensive)
            var existingValue = await database.StringGetAsync(lockKey);
            _logger.LogDebug("Lock {LockKey} is held by {Value}", lockKey, existingValue);

            return new RedisLockResponse(false, _redisOperations, lockKey, lockOwner, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock {LockKey}", lockKey);
            return new RedisLockResponse(false, _redisOperations, lockKey, lockOwner, _logger);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Entry for in-memory lock storage.
    /// </summary>
    private sealed record InMemoryLockEntry(string Owner, string Value, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Redis-based lock response that releases lock on disposal.
/// Uses Lua script for safe unlock (only release if we own it).
/// </summary>
internal sealed class RedisLockResponse : ILockResponse
{
    private readonly IRedisOperations _redisOperations;
    private readonly string _lockKey;
    private readonly string _lockOwner;
    private readonly ILogger _logger;
    private bool _disposed;

    // Lua script for safe unlock - only delete if the value matches our owner prefix
    // NOTE: string.find uses plain=true (4th arg) to avoid Lua pattern interpretation
    // The hyphen in lockOwner (e.g., "auth-abc123") is a special pattern char otherwise
    private static readonly string UnlockScript = @"
        local value = redis.call('GET', KEYS[1])
        if value and string.find(value, ARGV[1], 1, true) == 1 then
            return redis.call('DEL', KEYS[1])
        end
        return 0
    ";

    public RedisLockResponse(bool success, IRedisOperations redisOperations, string lockKey, string lockOwner, ILogger logger)
    {
        Success = success;
        _redisOperations = redisOperations;
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
            var result = await _redisOperations.ScriptEvaluateAsync(
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

/// <summary>
/// In-memory lock response for fallback mode.
/// IMPLEMENTATION TENETS: Properly releases lock on disposal via cleanup callback.
/// </summary>
internal sealed class InMemoryLockResponse : ILockResponse
{
    private readonly string _lockKey;
    private readonly string _lockOwner;
    private readonly ILogger _logger;
    private readonly Action<string, string>? _cleanupCallback;
    private bool _disposed;

    /// <summary>
    /// Creates an in-memory lock response.
    /// </summary>
    /// <param name="success">Whether the lock was acquired.</param>
    /// <param name="lockKey">The lock key.</param>
    /// <param name="lockOwner">The lock owner.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cleanupCallback">Callback to remove lock on disposal (key, owner).</param>
    public InMemoryLockResponse(bool success, string lockKey, string lockOwner, ILogger logger, Action<string, string>? cleanupCallback = null)
    {
        Success = success;
        _lockKey = lockKey;
        _lockOwner = lockOwner;
        _logger = logger;
        _cleanupCallback = cleanupCallback;
    }

    public bool Success { get; }

    public ValueTask DisposeAsync()
    {
        if (_disposed || !Success) return ValueTask.CompletedTask;
        _disposed = true;

        // Invoke cleanup callback to remove lock from parent's dictionary
        _cleanupCallback?.Invoke(_lockKey, _lockOwner);
        _logger.LogDebug("Released in-memory lock {LockKey} for owner {Owner}", _lockKey, _lockOwner);

        return ValueTask.CompletedTask;
    }
}
