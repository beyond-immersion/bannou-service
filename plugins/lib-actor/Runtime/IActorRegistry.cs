namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Thread-safe registry for tracking active actor instances.
/// Uses ConcurrentDictionary internally per IMPLEMENTATION TENETS (Multi-Instance Safety).
/// </summary>
public interface IActorRegistry
{
    /// <summary>
    /// Gets the count of currently registered actors.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Attempts to register a new actor runner.
    /// </summary>
    /// <param name="actorId">The actor's unique identifier.</param>
    /// <param name="runner">The actor runner instance.</param>
    /// <returns>True if registered successfully, false if an actor with this ID already exists.</returns>
    bool TryRegister(string actorId, IActorRunner runner);

    /// <summary>
    /// Attempts to get an actor runner by ID.
    /// </summary>
    /// <param name="actorId">The actor's unique identifier.</param>
    /// <param name="runner">The actor runner if found.</param>
    /// <returns>True if found, false otherwise.</returns>
    bool TryGet(string actorId, out IActorRunner? runner);

    /// <summary>
    /// Attempts to remove an actor runner by ID.
    /// </summary>
    /// <param name="actorId">The actor's unique identifier.</param>
    /// <param name="runner">The removed actor runner if found.</param>
    /// <returns>True if removed, false if not found.</returns>
    bool TryRemove(string actorId, out IActorRunner? runner);

    /// <summary>
    /// Gets all active actor IDs.
    /// </summary>
    /// <returns>An enumerable of all registered actor IDs.</returns>
    IEnumerable<string> GetActiveActorIds();

    /// <summary>
    /// Gets all active actor runners.
    /// </summary>
    /// <returns>An enumerable of all registered actor runners.</returns>
    IEnumerable<IActorRunner> GetAllRunners();

    /// <summary>
    /// Gets actors filtered by category.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    /// <returns>Actors matching the category.</returns>
    IEnumerable<IActorRunner> GetByCategory(string category);

    /// <summary>
    /// Gets actors filtered by template ID.
    /// </summary>
    /// <param name="templateId">The template ID to filter by.</param>
    /// <returns>Actors matching the template ID.</returns>
    IEnumerable<IActorRunner> GetByTemplateId(Guid templateId);

    /// <summary>
    /// Gets actors filtered by character ID.
    /// </summary>
    /// <param name="characterId">The character ID to filter by.</param>
    /// <returns>Actors associated with the character.</returns>
    IEnumerable<IActorRunner> GetByCharacterId(Guid characterId);

    /// <summary>
    /// Gets actors filtered by status.
    /// </summary>
    /// <param name="status">The status to filter by.</param>
    /// <returns>Actors with the specified status.</returns>
    IEnumerable<IActorRunner> GetByStatus(ActorStatus status);
}
