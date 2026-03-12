using BeyondImmersion.Bannou.Core;
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
    Conflict,

    /// <summary>The validateAndMutate callback determined the operation should be skipped (validation failure).</summary>
    ValidationFailed,

    /// <summary>The validateAndMutate callback requested deletion and the entity was deleted.</summary>
    Deleted
}

/// <summary>
/// Outcome of a validateAndMutate callback, indicating what the retry helper should do next.
/// </summary>
public enum MutationOutcome
{
    /// <summary>The entity was mutated in place; save it with ETag concurrency.</summary>
    Mutated,

    /// <summary>Validation failed; abort the loop and return the error status to the caller.</summary>
    Skip,

    /// <summary>The entity should be deleted from the store.</summary>
    Delete
}

/// <summary>
/// Result returned by a validateAndMutate callback to control the retry helper's behavior.
/// Use the static factory members for clean call-site ergonomics.
/// </summary>
public readonly record struct MutationResult(MutationOutcome Outcome, StatusCodes ErrorStatus = default)
{
    /// <summary>Entity was mutated in place; the helper will save it.</summary>
    public static readonly MutationResult Mutated = new(MutationOutcome.Mutated);

    /// <summary>Entity should be deleted from the store.</summary>
    public static readonly MutationResult Delete = new(MutationOutcome.Delete);

    /// <summary>Validation failed; abort with the given error status code.</summary>
    public static MutationResult SkipWith(StatusCodes errorStatus) => new(MutationOutcome.Skip, errorStatus);
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

    /// <summary>
    /// Reads an entity, applies an async validate-and-mutate callback, and saves with
    /// ETag-based optimistic concurrency, retrying on conflicts up to
    /// <paramref name="maxRetries"/> times.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the <c>Action&lt;T&gt;</c> overload, the callback can perform validation
    /// (including async inter-service reads) and return a <see cref="MutationResult"/>
    /// indicating whether the entity was mutated, should be skipped (validation failure),
    /// or should be deleted.
    /// </para>
    /// <para>
    /// The callback mutates the entity in place and returns
    /// <see cref="MutationResult.Mutated"/>. For validation failures, return
    /// <see cref="MutationResult.SkipWith"/> with the appropriate status code.
    /// For conditional deletion, return <see cref="MutationResult.Delete"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type stored in the state store.</typeparam>
    /// <param name="store">The state store to operate on.</param>
    /// <param name="key">The state store key for the entity.</param>
    /// <param name="validateAndMutate">
    /// Async callback that validates and optionally mutates the entity. Called on each
    /// attempt with the freshly-read entity. Must be idempotent (same logical outcome
    /// each time for the same entity state).
    /// </param>
    /// <param name="maxRetries">Maximum number of attempts before giving up.</param>
    /// <param name="logger">Logger for per-attempt debug and exhaustion warning messages.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of (result, entity, errorStatus). On <see cref="UpdateResult.Success"/>,
    /// entity contains the saved state. On <see cref="UpdateResult.ValidationFailed"/>,
    /// errorStatus contains the status code from the callback. On other results, entity
    /// is null and errorStatus is default.
    /// </returns>
    public static async Task<(UpdateResult result, T? entity, StatusCodes errorStatus)> UpdateWithRetryAsync<T>(
        this IStateStore<T> store,
        string key,
        Func<T, Task<MutationResult>> validateAndMutate,
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
                return (UpdateResult.NotFound, default, default);
            }

            var mutation = await validateAndMutate(current);

            switch (mutation.Outcome)
            {
                case MutationOutcome.Skip:
                    return (UpdateResult.ValidationFailed, default, mutation.ErrorStatus);

                case MutationOutcome.Delete:
                    await store.DeleteAsync(key, ct);
                    return (UpdateResult.Deleted, default, default);

                case MutationOutcome.Mutated:
                    // GetWithETagAsync returns non-null etag for existing records;
                    // coalesce satisfies compiler's nullable analysis (will never execute)
                    var saved = await store.TrySaveAsync(key, current, etag ?? string.Empty, cancellationToken: ct);
                    if (saved == null)
                    {
                        logger.LogDebug("Concurrency conflict on attempt {Attempt} for {Key}", attempt + 1, key);
                        continue;
                    }
                    return (UpdateResult.Success, current, default);

                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation.Outcome, "Unknown mutation outcome");
            }
        }

        logger.LogWarning("Exhausted {MaxRetries} concurrency retries for {Key}", maxRetries, key);
        return (UpdateResult.Conflict, default, default);
    }

    #region String List Index Helpers (Reverse Indexes)

    /// <summary>
    /// Adds a value to a JSON-serialized string list at the given key with optimistic concurrency.
    /// Used for reverse indexes (e.g., template→instance list). Idempotent — skips if value already present.
    /// </summary>
    /// <param name="store">The string state store holding the JSON-serialized list.</param>
    /// <param name="key">The state store key for the list.</param>
    /// <param name="value">The string value to add (typically an entity ID).</param>
    /// <param name="maxRetries">Maximum number of optimistic concurrency retry attempts.</param>
    /// <param name="logger">Logger for retry diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task AddToStringListAsync(
        this IStateStore<string> store,
        string key,
        string value,
        int maxRetries,
        ILogger logger,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (json, etag) = await store.GetWithETagAsync(key, ct);
            var list = string.IsNullOrEmpty(json)
                ? new List<string>()
                : BannouJson.Deserialize<List<string>>(json) ?? new List<string>();

            if (list.Contains(value)) return;

            list.Add(value);
            var serialized = BannouJson.Serialize(list);

            // Empty etag = "create only if not exists" semantics across all backends
            // (Redis transaction, InMemory TryAdd). Prevents concurrent first-writes
            // from losing a record (per IMPLEMENTATION TENETS).
            var newEtag = await store.TrySaveAsync(key, serialized, etag ?? string.Empty, cancellationToken: ct);
            if (newEtag is not null) return;

            logger.LogDebug("Optimistic concurrency conflict on list {Key}, retry {Attempt}", key, attempt + 1);
        }

        logger.LogWarning("Failed to add to list {Key} after {MaxRetries} retries", key, maxRetries);
    }

    /// <summary>
    /// Removes a value from a JSON-serialized string list at the given key with optimistic concurrency.
    /// Deletes the key entirely when the list becomes empty (prevents empty index accumulation).
    /// </summary>
    /// <param name="store">The string state store holding the JSON-serialized list.</param>
    /// <param name="key">The state store key for the list.</param>
    /// <param name="value">The string value to remove (typically an entity ID).</param>
    /// <param name="maxRetries">Maximum number of optimistic concurrency retry attempts.</param>
    /// <param name="logger">Logger for retry diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task RemoveFromStringListAsync(
        this IStateStore<string> store,
        string key,
        string value,
        int maxRetries,
        ILogger logger,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (json, etag) = await store.GetWithETagAsync(key, ct);
            if (string.IsNullOrEmpty(json)) return;

            var list = BannouJson.Deserialize<List<string>>(json) ?? new List<string>();
            if (!list.Remove(value)) return;

            // If list is now empty, delete the key to prevent empty index accumulation.
            // All readers handle missing keys identically to empty lists via
            // string.IsNullOrEmpty check, and AddToStringListAsync recreates from scratch.
            if (list.Count == 0)
            {
                await store.DeleteAsync(key, ct);
                return;
            }

            var serialized = BannouJson.Serialize(list);

            if (etag is null)
            {
                // Backend returned data without ETag. Unconditional save is acceptable for
                // removes — concurrent removes are idempotent (shrinking the list) and cannot
                // lose data, unlike concurrent adds (per IMPLEMENTATION TENETS).
                await store.SaveAsync(key, serialized, cancellationToken: ct);
                return;
            }

            var newEtag = await store.TrySaveAsync(key, serialized, etag, cancellationToken: ct);
            if (newEtag is not null) return;

            logger.LogDebug("Optimistic concurrency conflict on list {Key}, retry {Attempt}", key, attempt + 1);
        }

        logger.LogWarning("Failed to remove from list {Key} after {MaxRetries} retries", key, maxRetries);
    }

    /// <summary>
    /// Checks whether a JSON-serialized string list at the given key has any entries.
    /// Used by Category B clean-deprecated sweeps to check if a deprecated template
    /// still has instances referencing it via a reverse index (O(1) vs full query scan).
    /// </summary>
    /// <param name="store">The string state store holding the JSON-serialized list.</param>
    /// <param name="key">The state store key for the list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the list exists and contains at least one entry; false otherwise.</returns>
    public static async Task<bool> HasStringListEntriesAsync(
        this IStateStore<string> store,
        string key,
        CancellationToken ct)
    {
        var json = await store.GetAsync(key, ct);
        if (string.IsNullOrEmpty(json)) return false;
        var list = BannouJson.Deserialize<List<string>>(json);
        return list is { Count: > 0 };
    }

    #endregion
}
