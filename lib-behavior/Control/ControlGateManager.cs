// =============================================================================
// Control Gate Manager
// Registry and management of per-entity control gates.
// =============================================================================

using System.Collections.Concurrent;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Thread-safe manager for entity control gates.
/// </summary>
public sealed class ControlGateManager : IControlGateRegistry
{
    private readonly ConcurrentDictionary<Guid, IControlGate> _gates;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<ControlGateManager>? _logger;

    /// <summary>
    /// Creates a new control gate manager.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for creating gate loggers.</param>
    public ControlGateManager(ILoggerFactory? loggerFactory = null)
    {
        _gates = new ConcurrentDictionary<Guid, IControlGate>();
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<ControlGateManager>();
    }

    /// <inheritdoc/>
    public IControlGate GetOrCreate(Guid entityId)
    {
        return _gates.GetOrAdd(entityId, id =>
        {
            var gateLogger = _loggerFactory?.CreateLogger<ControlGate>();
            var gate = new ControlGate(id, gateLogger);

            _logger?.LogDebug("Created control gate for entity {EntityId}", id);

            return gate;
        });
    }

    /// <inheritdoc/>
    public IControlGate? Get(Guid entityId)
    {
        return _gates.TryGetValue(entityId, out var gate) ? gate : null;
    }

    /// <inheritdoc/>
    public bool Remove(Guid entityId)
    {
        var removed = _gates.TryRemove(entityId, out _);

        if (removed)
        {
            _logger?.LogDebug("Removed control gate for entity {EntityId}", entityId);
        }

        return removed;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<Guid> GetCinematicControlledEntities()
    {
        var result = new List<Guid>();

        foreach (var kvp in _gates)
        {
            if (kvp.Value.CurrentSource == ControlSource.Cinematic)
            {
                result.Add(kvp.Key);
            }
        }

        return result.AsReadOnly();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<Guid> GetPlayerControlledEntities()
    {
        var result = new List<Guid>();

        foreach (var kvp in _gates)
        {
            if (kvp.Value.CurrentSource == ControlSource.Player)
            {
                result.Add(kvp.Key);
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Takes cinematic control of multiple entities at once.
    /// </summary>
    /// <param name="entityIds">The entities to control.</param>
    /// <param name="cinematicId">The cinematic ID.</param>
    /// <param name="allowBehaviorChannels">Channels where behavior can contribute.</param>
    /// <param name="duration">Expected duration.</param>
    /// <returns>True if all entities were controlled, false if any failed.</returns>
    public async Task<bool> TakeCinematicControlAsync(
        IEnumerable<Guid> entityIds,
        string cinematicId,
        IReadOnlySet<string>? allowBehaviorChannels = null,
        TimeSpan? duration = null)
    {
        var options = ControlOptions.ForCinematic(cinematicId, allowBehaviorChannels, duration);
        var allSuccess = true;

        foreach (var entityId in entityIds)
        {
            var gate = GetOrCreate(entityId);
            var success = await gate.TakeControlAsync(options);

            if (!success)
            {
                _logger?.LogWarning(
                    "Failed to take cinematic control of entity {EntityId} for cinematic {CinematicId}",
                    entityId,
                    cinematicId);
                allSuccess = false;
            }
        }

        return allSuccess;
    }

    /// <summary>
    /// Returns control from cinematic for multiple entities.
    /// </summary>
    /// <param name="entityIds">The entities to release.</param>
    /// <param name="handoff">The handoff protocol.</param>
    public async Task ReturnCinematicControlAsync(
        IEnumerable<Guid> entityIds,
        ControlHandoff handoff)
    {
        foreach (var entityId in entityIds)
        {
            var gate = Get(entityId);
            if (gate != null && gate.CurrentSource == ControlSource.Cinematic)
            {
                await gate.ReturnControlAsync(handoff);
            }
        }
    }

    /// <summary>
    /// Gets the count of active control gates.
    /// </summary>
    public int Count => _gates.Count;

    /// <summary>
    /// Clears all control gates.
    /// </summary>
    public void Clear()
    {
        _gates.Clear();
        _logger?.LogInformation("Cleared all control gates");
    }
}
