namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Result of an operation that requires a distributed lock.
/// Consumers check LockAcquired before using Value.
/// </summary>
/// <typeparam name="T">Type of the operation result.</typeparam>
/// <param name="LockAcquired">Whether the distributed lock was successfully acquired.</param>
/// <param name="Value">The operation result, or default if lock was not acquired.</param>
public record LockableResult<T>(bool LockAcquired, T? Value);
