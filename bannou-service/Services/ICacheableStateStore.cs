#nullable enable

using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// State store with Set and Sorted Set operations for caching patterns.
/// Extends IStateStore with collection operations needed for leaderboards, indexes, and caches.
/// Supported by Redis and InMemory backends.
/// MySQL backends do NOT implement this interface.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public interface ICacheableStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    // ==================== Set Operations ====================
    // Sets are collections of unique items stored under a single key.
    // Supported by Redis (native sets) and InMemory backends.

    /// <summary>
    /// Add an item to a set. Creates the set if it doesn't exist.
    /// </summary>
    /// <typeparam name="TItem">Type of item to add.</typeparam>
    /// <param name="key">The set key.</param>
    /// <param name="item">The item to add.</param>
    /// <param name="options">Optional state options (TTL applies to entire set).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if item was added, false if already existed.</returns>
    Task<bool> AddToSetAsync<TItem>(
        string key,
        TItem item,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add multiple items to a set. Creates the set if it doesn't exist.
    /// </summary>
    /// <typeparam name="TItem">Type of items to add.</typeparam>
    /// <param name="key">The set key.</param>
    /// <param name="items">The items to add.</param>
    /// <param name="options">Optional state options (TTL applies to entire set).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of items actually added (excludes duplicates).</returns>
    Task<long> AddToSetAsync<TItem>(
        string key,
        IEnumerable<TItem> items,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove an item from a set.
    /// </summary>
    /// <typeparam name="TItem">Type of item to remove.</typeparam>
    /// <param name="key">The set key.</param>
    /// <param name="item">The item to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if item was removed, false if not found.</returns>
    Task<bool> RemoveFromSetAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all items in a set.
    /// </summary>
    /// <typeparam name="TItem">Type of items in the set.</typeparam>
    /// <param name="key">The set key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All items in the set, or empty list if set doesn't exist.</returns>
    Task<IReadOnlyList<TItem>> GetSetAsync<TItem>(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if an item exists in a set.
    /// </summary>
    /// <typeparam name="TItem">Type of item to check.</typeparam>
    /// <param name="key">The set key.</param>
    /// <param name="item">The item to check for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if item exists in set.</returns>
    Task<bool> SetContainsAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the number of items in a set.
    /// </summary>
    /// <param name="key">The set key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of items in the set, or 0 if set doesn't exist.</returns>
    Task<long> SetCountAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entire set.
    /// </summary>
    /// <param name="key">The set key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if set existed and was deleted.</returns>
    Task<bool> DeleteSetAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh/extend the TTL on a set without modifying its contents.
    /// Useful for keeping a set alive while it's still in use.
    /// </summary>
    /// <param name="key">The set key.</param>
    /// <param name="ttlSeconds">New TTL in seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if set exists and TTL was updated.</returns>
    Task<bool> RefreshSetTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default);

    // ==================== Sorted Set Operations ====================
    // Sorted sets store members with scores, enabling ranked queries (leaderboards).
    // Supported by Redis (native sorted sets) and InMemory backends.

    /// <summary>
    /// Add a member to a sorted set with the given score.
    /// Creates the sorted set if it doesn't exist.
    /// If member already exists, its score is updated.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="member">The member to add (typically entity_type:entity_id).</param>
    /// <param name="score">The score for ranking.</param>
    /// <param name="options">Optional state options (TTL applies to entire sorted set).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if member was newly added, false if score was updated.</returns>
    Task<bool> SortedSetAddAsync(
        string key,
        string member,
        double score,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add multiple members to a sorted set with their scores.
    /// Creates the sorted set if it doesn't exist.
    /// Existing members have their scores updated.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="entries">Members and their scores.</param>
    /// <param name="options">Optional state options (TTL applies to entire sorted set).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of members newly added (not including score updates).</returns>
    Task<long> SortedSetAddBatchAsync(
        string key,
        IEnumerable<(string member, double score)> entries,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a member from a sorted set.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="member">The member to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if member was removed, false if not found.</returns>
    Task<bool> SortedSetRemoveAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the score of a member in a sorted set.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="member">The member to get score for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The member's score, or null if member not found.</returns>
    Task<double?> SortedSetScoreAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the rank (position) of a member in a sorted set.
    /// Rank is 0-based (first place = 0).
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="member">The member to get rank for.</param>
    /// <param name="descending">If true, rank by highest score first (default for leaderboards).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The member's 0-based rank, or null if member not found.</returns>
    Task<long?> SortedSetRankAsync(
        string key,
        string member,
        bool descending = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get members by rank range (e.g., top 10, ranks 50-60).
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="start">Start rank (0-based, inclusive).</param>
    /// <param name="stop">Stop rank (0-based, inclusive). Use -1 for end.</param>
    /// <param name="descending">If true, rank by highest score first (default for leaderboards).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of members with their scores, ordered by rank.</returns>
    Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByRankAsync(
        string key,
        long start,
        long stop,
        bool descending = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get members by score range (e.g., all scores between min and max).
    /// Supports cursor-based pagination via score boundaries.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="minScore">Minimum score (inclusive). Use double.NegativeInfinity for unbounded.</param>
    /// <param name="maxScore">Maximum score (exclusive). Use double.PositiveInfinity for unbounded.</param>
    /// <param name="offset">Number of results to skip within the range.</param>
    /// <param name="count">Maximum number of results to return. Use -1 for unlimited.</param>
    /// <param name="descending">If true, iterate from maxScore to minScore.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of members with their scores, ordered by score.</returns>
    Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByScoreAsync(
        string key,
        double minScore,
        double maxScore,
        int offset = 0,
        int count = -1,
        bool descending = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the number of members in a sorted set.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of members, or 0 if sorted set doesn't exist.</returns>
    Task<long> SortedSetCountAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment a member's score in a sorted set.
    /// Creates the member with the increment as score if it doesn't exist.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="member">The member whose score to increment.</param>
    /// <param name="increment">Amount to add to score (can be negative).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new score after incrementing.</returns>
    Task<double> SortedSetIncrementAsync(
        string key,
        string member,
        double increment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entire sorted set.
    /// </summary>
    /// <param name="key">The sorted set key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if sorted set existed and was deleted.</returns>
    Task<bool> SortedSetDeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    // ==================== Atomic Counter Operations ====================
    // Counters are simple long values with atomic increment/decrement.
    // Supported by Redis (native INCR/DECR) and InMemory backends.
    // Use these instead of IRedisOperations for common counter patterns.

    /// <summary>
    /// Atomically increment a counter by the specified value.
    /// If the key doesn't exist, it's created with value 0 before incrementing.
    /// </summary>
    /// <param name="key">The counter key.</param>
    /// <param name="increment">Amount to increment by (default 1). Can be negative.</param>
    /// <param name="options">Optional state options (TTL applies to the counter).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new value after incrementing.</returns>
    Task<long> IncrementAsync(
        string key,
        long increment = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically decrement a counter by the specified value.
    /// If the key doesn't exist, it's created with value 0 before decrementing.
    /// </summary>
    /// <param name="key">The counter key.</param>
    /// <param name="decrement">Amount to decrement by (default 1).</param>
    /// <param name="options">Optional state options (TTL applies to the counter).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new value after decrementing.</returns>
    Task<long> DecrementAsync(
        string key,
        long decrement = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current value of a counter.
    /// </summary>
    /// <param name="key">The counter key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counter value, or null if the counter doesn't exist.</returns>
    Task<long?> GetCounterAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a counter to an absolute value.
    /// Creates the counter if it doesn't exist.
    /// </summary>
    /// <param name="key">The counter key.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="options">Optional state options (TTL applies to the counter).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetCounterAsync(
        string key,
        long value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a counter.
    /// </summary>
    /// <param name="key">The counter key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the counter existed and was deleted.</returns>
    Task<bool> DeleteCounterAsync(
        string key,
        CancellationToken cancellationToken = default);

    // ==================== Hash Operations ====================
    // Hashes store multiple field-value pairs under a single key.
    // Supported by Redis (native HGET/HSET) and InMemory backends.
    // Use these instead of IRedisOperations for common hash patterns.

    /// <summary>
    /// Get a field value from a hash.
    /// </summary>
    /// <typeparam name="TField">Type of the field value.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field within the hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The field value, or default if not found.</returns>
    Task<TField?> HashGetAsync<TField>(
        string key,
        string field,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a field value in a hash.
    /// Creates the hash if it doesn't exist.
    /// </summary>
    /// <typeparam name="TField">Type of the field value.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field within the hash.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="options">Optional state options (TTL applies to entire hash).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the field was newly created, false if it was updated.</returns>
    Task<bool> HashSetAsync<TField>(
        string key,
        string field,
        TField value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set multiple field values in a hash.
    /// Creates the hash if it doesn't exist.
    /// </summary>
    /// <typeparam name="TField">Type of the field values.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="fields">Field-value pairs to set.</param>
    /// <param name="options">Optional state options (TTL applies to entire hash).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HashSetManyAsync<TField>(
        string key,
        IEnumerable<KeyValuePair<string, TField>> fields,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a field from a hash.
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
    /// Check if a field exists in a hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the field exists.</returns>
    Task<bool> HashExistsAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increment a numeric field in a hash.
    /// If the field doesn't exist, it's created with value 0 before incrementing.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field to increment.</param>
    /// <param name="increment">Amount to increment by (default 1). Can be negative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new value after incrementing.</returns>
    Task<long> HashIncrementAsync(
        string key,
        string field,
        long increment = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all field-value pairs from a hash.
    /// </summary>
    /// <typeparam name="TField">Type of the field values.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All field-value pairs, or empty dictionary if hash doesn't exist.</returns>
    Task<IReadOnlyDictionary<string, TField>> HashGetAllAsync<TField>(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the number of fields in a hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of fields, or 0 if hash doesn't exist.</returns>
    Task<long> HashCountAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entire hash.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the hash existed and was deleted.</returns>
    Task<bool> DeleteHashAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh/extend the TTL on a hash without modifying its contents.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="ttlSeconds">New TTL in seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if hash exists and TTL was updated.</returns>
    Task<bool> RefreshHashTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default);
}
