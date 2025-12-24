#nullable enable

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Configuration options for state operations.
/// </summary>
public class StateOptions
{
    /// <summary>
    /// Time-to-live in seconds (Redis only). Null means no expiration.
    /// </summary>
    public TimeSpan? Ttl { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency checks.
    /// If provided, save will fail if the stored ETag doesn't match.
    /// </summary>
    public string? Etag { get; set; }

    /// <summary>
    /// Consistency level for the operation.
    /// </summary>
    public StateConsistency Consistency { get; set; } = StateConsistency.Strong;
}

/// <summary>
/// Consistency level for state operations.
/// </summary>
public enum StateConsistency
{
    /// <summary>
    /// Strong consistency - reads always return latest writes.
    /// </summary>
    Strong,

    /// <summary>
    /// Eventual consistency - reads may return stale data for better performance.
    /// </summary>
    Eventual
}

/// <summary>
/// Backend type for a state store.
/// </summary>
public enum StateBackend
{
    /// <summary>
    /// Redis backend for ephemeral/session data.
    /// </summary>
    Redis,

    /// <summary>
    /// MySQL backend for durable/queryable data.
    /// </summary>
    MySql
}

/// <summary>
/// Generic state store interface replacing DaprClient.GetStateAsync()/SaveStateAsync().
/// Supports both Redis (ephemeral) and MySQL (durable) backends.
/// </summary>
/// <typeparam name="TValue">Value type stored</typeparam>
public interface IStateStore<TValue>
    where TValue : class
{
    /// <summary>
    /// Get state by key.
    /// </summary>
    /// <param name="key">The key to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value, or null if not found.</returns>
    Task<TValue?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get state with ETag for optimistic concurrency.
    /// </summary>
    /// <param name="key">The key to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple of the value (null if not found) and ETag (null if not found).</returns>
    Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save state.
    /// </summary>
    /// <param name="key">The key to save.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="options">Optional save options (TTL, consistency, ETag check).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new ETag after save.</returns>
    Task<string> SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save with ETag check (optimistic concurrency).
    /// </summary>
    /// <param name="key">The key to save.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="etag">The expected ETag - save fails if mismatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if save succeeded, false if ETag mismatch.</returns>
    Task<bool> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete state.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if key existed and was deleted, false if not found.</returns>
    Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key exists.</returns>
    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk get multiple keys.
    /// </summary>
    /// <param name="keys">The keys to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of key to value (missing keys are excluded).</returns>
    Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);
}
