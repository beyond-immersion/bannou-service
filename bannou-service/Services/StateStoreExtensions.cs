using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Result of an optimistic concurrency update operation.
/// </summary>
public enum UpdateResult
{
    /// <summary>The entity was successfully mutated and saved.</summary>
    Success,

    /// <summary>The entity was not found at the given key.</summary>
    NotFound,

    /// <summary>All retry attempts were exhausted due to concurrent modifications.</summary>
    Conflict
}

/// <summary>
/// Extension methods for <see cref="IStateStore{T}"/> providing common
/// patterns like optimistic concurrency retry loops.
/// </summary>
public static class StateStoreExtensions
{
    /// <summary>
    /// Reads an entity, applies a mutation, and saves with ETag-based optimistic
    /// concurrency, retrying on conflicts up to <paramref name="maxRetries"/> times.
    /// </summary>
    /// <typeparam name="T">The entity type stored in the state store.</typeparam>
    /// <param name="store">The state store to operate on.</param>
    /// <param name="key">The state store key for the entity.</param>
    /// <param name="mutate">
    /// Action that modifies the entity in place. Called on each attempt with the
    /// freshly-read entity. Must be idempotent (same logical mutation each time).
    /// </param>
    /// <param name="maxRetries">Maximum number of attempts before giving up.</param>
    /// <param name="logger">Logger for per-attempt debug and exhaustion warning messages.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of (result, entity). On <see cref="UpdateResult.Success"/>, entity
    /// contains the saved state. On <see cref="UpdateResult.NotFound"/> or
    /// <see cref="UpdateResult.Conflict"/>, entity is null.
    /// </returns>
    public static async Task<(UpdateResult result, T? entity)> UpdateWithRetryAsync<T>(
        this IStateStore<T> store,
        string key,
        Action<T> mutate,
        int maxRetries,
        ILogger logger,
        CancellationToken ct = default)
        where T : class
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (current, etag) = await store.GetWithETagAsync(key, ct);
            if (current == null)
            {
                return (UpdateResult.NotFound, default);
            }

            mutate(current);

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saved = await store.TrySaveAsync(key, current, etag ?? string.Empty, cancellationToken: ct);
            if (saved == null)
            {
                logger.LogDebug("Concurrency conflict on attempt {Attempt} for {Key}", attempt + 1, key);
                continue;
            }

            return (UpdateResult.Success, current);
        }

        logger.LogWarning("Exhausted {MaxRetries} concurrency retries for {Key}", maxRetries, key);
        return (UpdateResult.Conflict, default);
    }
}
