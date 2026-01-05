#nullable enable

using BeyondImmersion.BannouService.State;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Services;

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
    MySql,

    /// <summary>
    /// In-memory backend for testing/minimal infrastructure.
    /// Data is NOT persisted across restarts.
    /// </summary>
    Memory
}

/// <summary>
/// Generic state store interface for lib-state infrastructure.
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

    // ==================== Set Operations ====================
    // Sets are collections of unique items stored under a single key.
    // Supported by Redis (native sets) and InMemory backends.
    // MySQL throws NotSupportedException - use key-value with list serialization instead.

    /// <summary>
    /// Add an item to a set. Creates the set if it doesn't exist.
    /// </summary>
    /// <typeparam name="TItem">Type of item to add.</typeparam>
    /// <param name="key">The set key.</param>
    /// <param name="item">The item to add.</param>
    /// <param name="options">Optional state options (TTL applies to entire set).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if item was added, false if already existed.</returns>
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
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
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
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
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
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
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
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
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
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
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
    Task<long> SetCountAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entire set.
    /// </summary>
    /// <param name="key">The set key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if set existed and was deleted.</returns>
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
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
    /// <exception cref="NotSupportedException">Thrown by MySQL backend.</exception>
    Task<bool> RefreshSetTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Paged result for query operations.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>
    /// Whether there are more pages available.
    /// </summary>
    public bool HasMore => (Page + 1) * PageSize < TotalCount;

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

/// <summary>
/// Queryable state store - extends IStateStore for MySQL backends.
/// Redis stores do NOT implement this interface.
/// </summary>
/// <typeparam name="TValue">Value type stored</typeparam>
public interface IQueryableStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    /// <summary>
    /// Query with LINQ expression.
    /// </summary>
    /// <param name="predicate">Filter predicate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching values.</returns>
    Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query with pagination.
    /// </summary>
    /// <param name="predicate">Filter predicate (null for all).</param>
    /// <param name="page">Page number (0-indexed).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <param name="orderBy">Sort expression (null for default order).</param>
    /// <param name="descending">Whether to sort descending.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged result with items and metadata.</returns>
    Task<PagedResult<TValue>> QueryPagedAsync(
        Expression<Func<TValue, bool>>? predicate,
        int page,
        int pageSize,
        Expression<Func<TValue, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Count matching entries.
    /// </summary>
    /// <param name="predicate">Filter predicate (null for all).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of matching entries.</returns>
    Task<long> CountAsync(
        Expression<Func<TValue, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating typed state stores.
/// Manages connection pools and store lifecycle.
/// </summary>
public interface IStateStoreFactory
{
    /// <summary>
    /// Get or create a state store for the named store.
    /// Note: If InitializeAsync() was not called, this performs sync-over-async initialization.
    /// Prefer GetStoreAsync() for async contexts or call InitializeAsync() at startup.
    /// </summary>
    /// <typeparam name="TValue">Value type to store.</typeparam>
    /// <param name="storeName">Name of the configured store.</param>
    /// <returns>State store instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if store is not configured.</exception>
    IStateStore<TValue> GetStore<TValue>(string storeName)
        where TValue : class;

    /// <summary>
    /// Async version of GetStore - ensures initialization completes without blocking.
    /// Preferred over GetStore() in async contexts.
    /// </summary>
    /// <typeparam name="TValue">Value type to store.</typeparam>
    /// <param name="storeName">Name of the configured store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>State store instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if store is not configured.</exception>
    Task<IStateStore<TValue>> GetStoreAsync<TValue>(string storeName, CancellationToken cancellationToken = default)
        where TValue : class;

    /// <summary>
    /// Get queryable store (MySQL only, throws for Redis).
    /// </summary>
    /// <typeparam name="TValue">Value type to store.</typeparam>
    /// <param name="storeName">Name of the configured store.</param>
    /// <returns>Queryable state store instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if store is not configured or is not MySQL.</exception>
    IQueryableStateStore<TValue> GetQueryableStore<TValue>(string storeName)
        where TValue : class;

    /// <summary>
    /// Get JSON queryable store with efficient MySQL JSON functions (MySQL only).
    /// </summary>
    /// <typeparam name="TValue">Value type to store.</typeparam>
    /// <param name="storeName">Name of the configured store.</param>
    /// <returns>JSON queryable state store instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if store is not configured or is not MySQL.</exception>
    IJsonQueryableStateStore<TValue> GetJsonQueryableStore<TValue>(string storeName)
        where TValue : class;

    /// <summary>
    /// Get searchable store (Redis with RedisSearch enabled only).
    /// </summary>
    /// <typeparam name="TValue">Value type to store.</typeparam>
    /// <param name="storeName">Name of the configured store.</param>
    /// <returns>Searchable state store instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if store is not configured, not Redis, or search not enabled.</exception>
    ISearchableStateStore<TValue> GetSearchableStore<TValue>(string storeName)
        where TValue : class;

    /// <summary>
    /// Check if store supports full-text search.
    /// </summary>
    /// <param name="storeName">Name of the store to check.</param>
    /// <returns>True if the store supports search operations.</returns>
    bool SupportsSearch(string storeName);

    /// <summary>
    /// Check if store is configured.
    /// </summary>
    /// <param name="storeName">Name of the store to check.</param>
    /// <returns>True if the store is configured.</returns>
    bool HasStore(string storeName);

    /// <summary>
    /// Initialize connections to backend stores (Redis, MySQL).
    /// This should be called once at application startup for proper async initialization.
    /// If not called, initialization happens lazily on first GetStore call (sync-over-async).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when initialization is done.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get store backend type.
    /// </summary>
    /// <param name="storeName">Name of the store.</param>
    /// <returns>Backend type (Redis or MySQL).</returns>
    /// <exception cref="InvalidOperationException">Thrown if store is not configured.</exception>
    StateBackend GetBackendType(string storeName);

    /// <summary>
    /// Get all configured store names.
    /// </summary>
    /// <returns>Collection of store names.</returns>
    IEnumerable<string> GetStoreNames();

    /// <summary>
    /// Get store names filtered by backend type.
    /// </summary>
    /// <param name="backend">Backend type to filter by.</param>
    /// <returns>Collection of store names using that backend.</returns>
    IEnumerable<string> GetStoreNames(StateBackend backend);
}
