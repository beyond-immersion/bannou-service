// =============================================================================
// Behavior Stack Registry
// Manages behavior stacks for all entities.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behavior.Stack;

/// <summary>
/// Thread-safe registry for entity behavior stacks.
/// </summary>
public sealed class BehaviorStackRegistry : IBehaviorStackRegistry
{
    private readonly ConcurrentDictionary<Guid, IBehaviorStack> _stacks;
    private readonly IIntentStackMerger _merger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<BehaviorStackRegistry>? _logger;

    /// <summary>
    /// Creates a new behavior stack registry.
    /// </summary>
    /// <param name="merger">The merger to use for all stacks.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public BehaviorStackRegistry(
        IIntentStackMerger merger,
        ILoggerFactory? loggerFactory = null)
    {
        _merger = merger;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<BehaviorStackRegistry>();
        _stacks = new ConcurrentDictionary<Guid, IBehaviorStack>();
    }

    /// <inheritdoc/>
    public IBehaviorStack GetOrCreate(Guid entityId, IArchetypeDefinition archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);

        return _stacks.GetOrAdd(entityId, id =>
        {
            var stackLogger = _loggerFactory?.CreateLogger<BehaviorStack>();
            var stack = new BehaviorStack(id, archetype, _merger, stackLogger);

            _logger?.LogDebug(
                "Created behavior stack for entity {EntityId} with archetype {ArchetypeId}",
                id,
                archetype.Id);

            return stack;
        });
    }

    /// <inheritdoc/>
    public IBehaviorStack? Get(Guid entityId)
    {
        return _stacks.TryGetValue(entityId, out var stack) ? stack : null;
    }

    /// <inheritdoc/>
    public bool Remove(Guid entityId)
    {
        var removed = _stacks.TryRemove(entityId, out var stack);

        if (removed && stack != null)
        {
            stack.Clear();
            _logger?.LogDebug("Removed behavior stack for entity {EntityId}", entityId);
        }

        return removed;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<Guid> GetEntityIds()
    {
        return _stacks.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public int Count => _stacks.Count;

    /// <inheritdoc/>
    public void Clear()
    {
        var ids = _stacks.Keys.ToList();
        foreach (var id in ids)
        {
            Remove(id);
        }

        _logger?.LogInformation("Cleared all behavior stacks");
    }
}
