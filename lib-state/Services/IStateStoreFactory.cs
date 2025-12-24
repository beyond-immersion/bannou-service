#nullable enable

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Factory for creating typed state stores.
/// Manages connection pools and store lifecycle.
/// </summary>
public interface IStateStoreFactory
{
    /// <summary>
    /// Get or create a state store for the named store.
    /// </summary>
    /// <typeparam name="TValue">Value type to store.</typeparam>
    /// <param name="storeName">Name of the configured store.</param>
    /// <returns>State store instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if store is not configured.</exception>
    IStateStore<TValue> GetStore<TValue>(string storeName)
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
