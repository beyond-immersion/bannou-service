#nullable enable

using StackExchange.Redis;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Low-level Redis operations interface for escape hatch scenarios.
/// Provides direct access to Redis commands not abstracted by IStateStore.
/// Keys passed to these methods are NOT prefixed - they are used as-is.
/// This enables cross-store atomic operations in Lua scripts.
/// </summary>
/// <remarks>
/// Use this interface when you need:
/// - Lua scripts for atomic operations
/// - Atomic counters (INCR/DECR)
/// - Hash operations (HGET/HSET/HINCRBY)
/// - TTL manipulation (EXPIRE/TTL/PERSIST)
/// - Key existence checks without deserialization
///
/// This interface returns null from GetRedisOperations() when running
/// in InMemory mode. Callers must handle the null case appropriately.
/// </remarks>
public interface IRedisOperations
{
    // ==================== Lua Scripts ====================

    /// <summary>
    /// Evaluates a Lua script against Redis.
    /// </summary>
    /// <param name="script">The Lua script to evaluate.</param>
    /// <param name="keys">Keys accessed by the script (KEYS array in Lua).</param>
    /// <param name="values">Values passed to the script (ARGV array in Lua).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The script result.</returns>
    Task<RedisResult> ScriptEvaluateAsync(
        string script,
        RedisKey[] keys,
        RedisValue[] values,
        CancellationToken cancellationToken = default);

    // ==================== Atomic Counters ====================

    /// <summary>
    /// Atomically increments a key's value.
    /// If the key doesn't exist, it's created with value 0 before incrementing.
    /// </summary>
    /// <param name="key">The key to increment.</param>
    /// <param name="value">Amount to increment by (default 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new value after incrementing.</returns>
    Task<long> IncrementAsync(
        string key,
        long value = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically decrements a key's value.
    /// If the key doesn't exist, it's created with value 0 before decrementing.
    /// </summary>
    /// <param name="key">The key to decrement.</param>
    /// <param name="value">Amount to decrement by (default 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new value after decrementing.</returns>
    Task<long> DecrementAsync(
        string key,
        long value = 1,
        CancellationToken cancellationToken = default);

    // ==================== Hash Operations ====================

    /// <summary>
    /// Gets a field value from a hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field within the hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The field value, or RedisValue.Null if not found.</returns>
    Task<RedisValue> HashGetAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a field value in a hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field within the hash.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HashSetAsync(
        string key,
        string field,
        RedisValue value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets multiple field values in a hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="entries">Field-value pairs to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HashSetAsync(
        string key,
        HashEntry[] entries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a field from a hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the field was deleted, false if it didn't exist.</returns>
    Task<bool> HashDeleteAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments a hash field's value.
    /// If the field doesn't exist, it's created with value 0 before incrementing.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field to increment.</param>
    /// <param name="value">Amount to increment by (default 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new value after incrementing.</returns>
    Task<long> HashIncrementAsync(
        string key,
        string field,
        long value = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all field-value pairs from a hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All field-value pairs in the hash.</returns>
    Task<HashEntry[]> HashGetAllAsync(
        string key,
        CancellationToken cancellationToken = default);

    // ==================== TTL Operations ====================

    /// <summary>
    /// Sets an expiration time on a key.
    /// </summary>
    /// <param name="key">The key to set expiration on.</param>
    /// <param name="expiry">Time until expiration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the timeout was set, false if key doesn't exist.</returns>
    Task<bool> ExpireAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the remaining time to live for a key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The TTL, or null if key doesn't exist or has no expiration.</returns>
    Task<TimeSpan?> TimeToLiveAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the expiration from a key, making it persistent.
    /// </summary>
    /// <param name="key">The key to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the timeout was removed, false if key doesn't exist or has no timeout.</returns>
    Task<bool> PersistAsync(
        string key,
        CancellationToken cancellationToken = default);

    // ==================== Key Operations ====================

    /// <summary>
    /// Checks if a key exists.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key exists.</returns>
    Task<bool> KeyExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a key.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key was deleted, false if it didn't exist.</returns>
    Task<bool> KeyDeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the underlying Redis database for advanced operations.
    /// Use sparingly - prefer using the typed methods above.
    /// </summary>
    /// <returns>The Redis database instance.</returns>
    IDatabase GetDatabase();
}
