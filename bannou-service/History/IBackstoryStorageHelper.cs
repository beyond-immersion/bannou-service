using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Defines how to match and update elements in backstory storage.
/// </summary>
/// <typeparam name="TElement">Type of element in the backstory.</typeparam>
public interface IBackstoryElementMatcher<TElement> where TElement : class
{
    /// <summary>
    /// Gets the type component of the composite key.
    /// </summary>
    /// <param name="element">The element to extract type from.</param>
    /// <returns>The type string.</returns>
    string GetElementType(TElement element);

    /// <summary>
    /// Gets the key component of the composite key.
    /// </summary>
    /// <param name="element">The element to extract key from.</param>
    /// <returns>The key string.</returns>
    string GetElementKey(TElement element);

    /// <summary>
    /// Copies values from source element to target element during merge.
    /// Should not copy type or key (those define identity).
    /// </summary>
    /// <param name="source">Source element with new values.</param>
    /// <param name="target">Target element to update.</param>
    void CopyValues(TElement source, TElement target);

    /// <summary>
    /// Creates a copy of an element for storage.
    /// </summary>
    /// <param name="element">Element to clone.</param>
    /// <returns>A new instance with same values.</returns>
    TElement Clone(TElement element);
}

/// <summary>
/// Delegate-based implementation of IBackstoryElementMatcher.
/// </summary>
/// <typeparam name="TElement">Type of element.</typeparam>
public class BackstoryElementMatcher<TElement> : IBackstoryElementMatcher<TElement> where TElement : class
{
    private readonly Func<TElement, string> _getType;
    private readonly Func<TElement, string> _getKey;
    private readonly Action<TElement, TElement> _copyValues;
    private readonly Func<TElement, TElement> _clone;

    /// <summary>
    /// Creates a new matcher with the specified delegates.
    /// </summary>
    /// <param name="getType">Function to get element type.</param>
    /// <param name="getKey">Function to get element key.</param>
    /// <param name="copyValues">Action to copy values from source to target.</param>
    /// <param name="clone">Function to create a copy of an element.</param>
    public BackstoryElementMatcher(
        Func<TElement, string> getType,
        Func<TElement, string> getKey,
        Action<TElement, TElement> copyValues,
        Func<TElement, TElement> clone)
    {
        _getType = getType ?? throw new ArgumentNullException(nameof(getType));
        _getKey = getKey ?? throw new ArgumentNullException(nameof(getKey));
        _copyValues = copyValues ?? throw new ArgumentNullException(nameof(copyValues));
        _clone = clone ?? throw new ArgumentNullException(nameof(clone));
    }

    /// <inheritdoc />
    public string GetElementType(TElement element) => _getType(element);

    /// <inheritdoc />
    public string GetElementKey(TElement element) => _getKey(element);

    /// <inheritdoc />
    public void CopyValues(TElement source, TElement target) => _copyValues(source, target);

    /// <inheritdoc />
    public TElement Clone(TElement element) => _clone(element);
}

/// <summary>
/// Configuration for creating a BackstoryStorageHelper instance.
/// </summary>
/// <typeparam name="TBackstory">Type of the backstory container.</typeparam>
/// <typeparam name="TElement">Type of elements in the backstory.</typeparam>
public record BackstoryStorageConfiguration<TBackstory, TElement>
    where TBackstory : class, new()
    where TElement : class
{
    /// <summary>
    /// Factory for creating state stores.
    /// </summary>
    public required IStateStoreFactory StateStoreFactory { get; init; }

    /// <summary>
    /// Name of the state store to use.
    /// </summary>
    public required string StateStoreName { get; init; }

    /// <summary>
    /// Prefix for backstory keys (e.g., "backstory-").
    /// </summary>
    public required string KeyPrefix { get; init; }

    /// <summary>
    /// Matcher for element operations.
    /// </summary>
    public required IBackstoryElementMatcher<TElement> ElementMatcher { get; init; }

    /// <summary>
    /// Gets the entity ID from a backstory container.
    /// </summary>
    public required Func<TBackstory, string> GetEntityId { get; init; }

    /// <summary>
    /// Sets the entity ID on a backstory container.
    /// </summary>
    public required Action<TBackstory, string> SetEntityId { get; init; }

    /// <summary>
    /// Gets the elements list from a backstory container.
    /// </summary>
    public required Func<TBackstory, List<TElement>> GetElements { get; init; }

    /// <summary>
    /// Sets the elements list on a backstory container.
    /// </summary>
    public required Action<TBackstory, List<TElement>> SetElements { get; init; }

    /// <summary>
    /// Gets the created timestamp from a backstory container.
    /// </summary>
    public required Func<TBackstory, long> GetCreatedAtUnix { get; init; }

    /// <summary>
    /// Sets the created timestamp on a backstory container.
    /// </summary>
    public required Action<TBackstory, long> SetCreatedAtUnix { get; init; }

    /// <summary>
    /// Gets the updated timestamp from a backstory container.
    /// </summary>
    public required Func<TBackstory, long> GetUpdatedAtUnix { get; init; }

    /// <summary>
    /// Sets the updated timestamp on a backstory container.
    /// </summary>
    public required Action<TBackstory, long> SetUpdatedAtUnix { get; init; }

    /// <summary>
    /// Provider for distributed locking during write operations.
    /// </summary>
    public required IDistributedLockProvider LockProvider { get; init; }

    /// <summary>
    /// Timeout in seconds for distributed lock acquisition.
    /// </summary>
    public required int LockTimeoutSeconds { get; init; }
}

/// <summary>
/// Result of a backstory operation including whether it was a new creation.
/// </summary>
/// <typeparam name="TBackstory">Type of the backstory container.</typeparam>
/// <param name="Backstory">The backstory data.</param>
/// <param name="IsNew">True if this was a new creation, false if an update.</param>
public record BackstoryOperationResult<TBackstory>(TBackstory Backstory, bool IsNew);

/// <summary>
/// Interface for managing backstory/lore element storage with merge/replace semantics.
/// Write operations acquire a distributed lock on the entity ID per IMPLEMENTATION TENETS.
/// </summary>
/// <typeparam name="TBackstory">Type of the backstory container.</typeparam>
/// <typeparam name="TElement">Type of elements in the backstory.</typeparam>
public interface IBackstoryStorageHelper<TBackstory, TElement>
    where TBackstory : class, new()
    where TElement : class
{
    /// <summary>
    /// Gets a backstory by entity ID.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The backstory, or null if not found.</returns>
    Task<TBackstory?> GetAsync(
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets backstory elements with merge or replace semantics.
    /// Acquires a distributed lock on the entity ID before modifying data.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="elements">Elements to set.</param>
    /// <param name="replaceExisting">If true, replaces all elements. If false, merges by type+key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lockable result containing the backstory operation result.</returns>
    Task<LockableResult<BackstoryOperationResult<TBackstory>>> SetAsync(
        string entityId,
        IReadOnlyList<TElement> elements,
        bool replaceExisting,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a single element.
    /// Acquires a distributed lock on the entity ID before modifying data.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="element">Element to add or update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lockable result containing the backstory operation result.</returns>
    Task<LockableResult<BackstoryOperationResult<TBackstory>>> AddElementAsync(
        string entityId,
        TElement element,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backstory.
    /// Acquires a distributed lock on the entity ID before deleting.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lockable result containing whether backstory existed and was deleted.</returns>
    Task<LockableResult<bool>> DeleteAsync(
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a backstory exists.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if backstory exists.</returns>
    Task<bool> ExistsAsync(
        string entityId,
        CancellationToken cancellationToken = default);
}
