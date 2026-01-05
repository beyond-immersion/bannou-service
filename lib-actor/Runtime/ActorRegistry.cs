using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Thread-safe registry for tracking active actor instances.
/// Uses ConcurrentDictionary internally per T9 (Multi-Instance Safety).
/// </summary>
public class ActorRegistry : IActorRegistry
{
    private readonly ConcurrentDictionary<string, IActorRunner> _actors = new();

    /// <inheritdoc/>
    public int Count => _actors.Count;

    /// <inheritdoc/>
    public bool TryRegister(string actorId, IActorRunner runner)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(runner);
        return _actors.TryAdd(actorId, runner);
    }

    /// <inheritdoc/>
    public bool TryGet(string actorId, out IActorRunner? runner)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        return _actors.TryGetValue(actorId, out runner);
    }

    /// <inheritdoc/>
    public bool TryRemove(string actorId, out IActorRunner? runner)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        return _actors.TryRemove(actorId, out runner);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetActiveActorIds()
    {
        return _actors.Keys.ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<IActorRunner> GetAllRunners()
    {
        return _actors.Values.ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<IActorRunner> GetByCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return _actors.Values
            .Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<IActorRunner> GetByTemplateId(Guid templateId)
    {
        return _actors.Values
            .Where(r => r.TemplateId == templateId)
            .ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<IActorRunner> GetByCharacterId(Guid characterId)
    {
        return _actors.Values
            .Where(r => r.CharacterId == characterId)
            .ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<IActorRunner> GetByStatus(ActorStatus status)
    {
        return _actors.Values
            .Where(r => r.Status == status)
            .ToList();
    }
}
