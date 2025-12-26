namespace BeyondImmersion.BannouService.Services;

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
/// Uses Redis state store with SET NX EX pattern for reliable distributed locks.
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
