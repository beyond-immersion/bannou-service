using Dapr.Client;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Result of a distributed lock acquisition attempt.
/// </summary>
public interface ILockResponse : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the lock was successfully acquired.
    /// </summary>
    bool Success { get; }
}

/// <summary>
/// Provides distributed locking functionality for services.
/// Abstraction over Dapr distributed lock API to enable unit testing.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire a distributed lock.
    /// </summary>
    /// <param name="storeName">The name of the lock store component</param>
    /// <param name="resourceId">The identifier of the resource to lock</param>
    /// <param name="lockOwner">The identifier of the lock owner</param>
    /// <param name="expiryInSeconds">Lock expiration time in seconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Lock response indicating success and providing disposal</returns>
    Task<ILockResponse> LockAsync(
        string storeName,
        string resourceId,
        string lockOwner,
        int expiryInSeconds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Wrapper around Dapr's TryLockResponse implementing ILockResponse.
/// </summary>
#pragma warning disable DAPR_DISTRIBUTEDLOCK
internal class DaprLockResponse : ILockResponse
{
    private readonly TryLockResponse _daprResponse;

    public DaprLockResponse(TryLockResponse daprResponse)
    {
        _daprResponse = daprResponse ?? throw new ArgumentNullException(nameof(daprResponse));
    }

    public bool Success => _daprResponse.Success;

    public async ValueTask DisposeAsync()
    {
        await _daprResponse.DisposeAsync();
    }
}
#pragma warning restore DAPR_DISTRIBUTEDLOCK

/// <summary>
/// Redis-based implementation using Dapr state store SET NX pattern.
/// More reliable than experimental Dapr lock API.
/// </summary>
public class RedisDistributedLockProvider : IDistributedLockProvider
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<RedisDistributedLockProvider> _logger;

    /// <inheritdoc/>
    public RedisDistributedLockProvider(DaprClient daprClient, ILogger<RedisDistributedLockProvider> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ILockResponse> LockAsync(
        string storeName,
        string resourceId,
        string lockOwner,
        int expiryInSeconds,
        CancellationToken cancellationToken = default)
    {
        // Use Redis SET NX (set if not exists) pattern via Dapr state store
        // This is more reliable than the experimental Dapr lock API
        var lockKey = $"lock:{resourceId}";
        var lockValue = new LockData
        {
            Owner = lockOwner,
            AcquiredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiryInSeconds)
        };

        try
        {
            // Try to acquire lock using Dapr state store with ETag (optimistic concurrency)
            var existingLock = await _daprClient.GetStateAndETagAsync<LockData>(storeName, lockKey, cancellationToken: cancellationToken);

            // Check if lock exists and is still valid
            if (existingLock.value != null && existingLock.value.ExpiresAt > DateTimeOffset.UtcNow)
            {
                // Lock is held by someone else
                _logger.LogDebug("Lock {LockKey} is held by {Owner}, expires at {ExpiresAt}",
                    lockKey, existingLock.value.Owner, existingLock.value.ExpiresAt);
                return new RedisLockResponse(false, storeName, lockKey, lockOwner, _daprClient, _logger);
            }

            // Try to save with ETag check (atomic operation)
            bool acquired = await _daprClient.TrySaveStateAsync(storeName, lockKey, lockValue, existingLock.etag, cancellationToken: cancellationToken);

            if (acquired)
            {
                _logger.LogDebug("Successfully acquired lock {LockKey} for owner {Owner}", lockKey, lockOwner);
                return new RedisLockResponse(true, storeName, lockKey, lockOwner, _daprClient, _logger);
            }
            else
            {
                _logger.LogDebug("Failed to acquire lock {LockKey} (ETag mismatch)", lockKey);
                return new RedisLockResponse(false, storeName, lockKey, lockOwner, _daprClient, _logger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock {LockKey}", lockKey);
            return new RedisLockResponse(false, storeName, lockKey, lockOwner, _daprClient, _logger);
        }
    }

    private class LockData
    {
        public string Owner { get; set; } = "";
        public DateTimeOffset AcquiredAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}

/// <summary>
/// Redis-based lock response that releases lock on disposal.
/// </summary>
internal class RedisLockResponse : ILockResponse
{
    private readonly string _storeName;
    private readonly string _lockKey;
    private readonly string _lockOwner;
    private readonly DaprClient _daprClient;
    private readonly ILogger _logger;
    private bool _disposed;

    public RedisLockResponse(bool success, string storeName, string lockKey, string lockOwner, DaprClient daprClient, ILogger logger)
    {
        Success = success;
        _storeName = storeName;
        _lockKey = lockKey;
        _lockOwner = lockOwner;
        _daprClient = daprClient;
        _logger = logger;
    }

    public bool Success { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed || !Success) return;
        _disposed = true;

        try
        {
            // Release the lock by deleting the key
            await _daprClient.DeleteStateAsync(_storeName, _lockKey);
            _logger.LogDebug("Released lock {LockKey} for owner {Owner}", _lockKey, _lockOwner);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error releasing lock {LockKey}", _lockKey);
        }
    }
}
