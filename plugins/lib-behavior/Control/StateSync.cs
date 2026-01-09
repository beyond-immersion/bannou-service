// =============================================================================
// State Sync
// Synchronizes entity state from cinematic back to behavior when control returns.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Synchronizes entity state when control returns from cinematic to behavior.
/// </summary>
public interface IStateSync
{
    /// <summary>
    /// Synchronizes entity state from cinematic back to behavior.
    /// Called when cinematic returns control.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="finalCinematicState">The final state from the cinematic.</param>
    /// <param name="handoff">The control handoff parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the sync operation.</returns>
    Task SyncStateAsync(
        Guid entityId,
        EntityState finalCinematicState,
        ControlHandoff handoff,
        CancellationToken ct);

    /// <summary>
    /// Event raised when state sync completes.
    /// </summary>
    event EventHandler<StateSyncCompletedEventArgs>? StateSyncCompleted;
}

/// <summary>
/// Event args for state sync completion.
/// </summary>
public sealed class StateSyncCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Creates new event args.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="syncedState">The synced state.</param>
    /// <param name="handoffStyle">The handoff style used.</param>
    public StateSyncCompletedEventArgs(
        Guid entityId,
        EntityState syncedState,
        HandoffStyle handoffStyle)
    {
        EntityId = entityId;
        SyncedState = syncedState ?? throw new ArgumentNullException(nameof(syncedState));
        HandoffStyle = handoffStyle;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the entity ID.
    /// </summary>
    public Guid EntityId { get; }

    /// <summary>
    /// Gets the synced state.
    /// </summary>
    public EntityState SyncedState { get; }

    /// <summary>
    /// Gets the handoff style.
    /// </summary>
    public HandoffStyle HandoffStyle { get; }

    /// <summary>
    /// Gets when the sync completed.
    /// </summary>
    public DateTime CompletedAt { get; }
}

/// <summary>
/// Default implementation of state sync.
/// </summary>
public sealed class StateSync : IStateSync
{
    private readonly ILogger<StateSync>? _logger;

    /// <summary>
    /// Creates a new state sync service.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public StateSync(ILogger<StateSync>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public event EventHandler<StateSyncCompletedEventArgs>? StateSyncCompleted;

    /// <inheritdoc/>
    public async Task SyncStateAsync(
        Guid entityId,
        EntityState finalCinematicState,
        ControlHandoff handoff,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(finalCinematicState);
        ArgumentNullException.ThrowIfNull(handoff);

        // Skip sync if not requested
        if (!handoff.SyncState)
        {
            _logger?.LogDebug(
                "Skipping state sync for entity {EntityId} - SyncState is false",
                entityId);
            return;
        }

        _logger?.LogDebug(
            "Syncing state for entity {EntityId} with handoff style {Style}",
            entityId,
            handoff.Style);

        // For blend handoff, we'd need to interpolate state over time
        // For now, treat all styles as instant sync
        switch (handoff.Style)
        {
            case HandoffStyle.Instant:
                // Immediate state sync
                await SyncInstantAsync(entityId, finalCinematicState, ct);
                break;

            case HandoffStyle.Blend:
                // TODO: Implement blend interpolation over handoff.BlendDuration
                // For MVP, fall through to instant
                await SyncInstantAsync(entityId, finalCinematicState, ct);
                _logger?.LogDebug(
                    "Blend handoff not yet implemented, using instant for entity {EntityId}",
                    entityId);
                break;

            case HandoffStyle.Explicit:
                // Explicit means the handoff was already handled - just sync final state
                await SyncInstantAsync(entityId, finalCinematicState, ct);
                break;

            default:
                _logger?.LogWarning(
                    "Unknown handoff style {Style} for entity {EntityId}, using instant",
                    handoff.Style,
                    entityId);
                await SyncInstantAsync(entityId, finalCinematicState, ct);
                break;
        }

        // Raise completion event
        StateSyncCompleted?.Invoke(this, new StateSyncCompletedEventArgs(
            entityId,
            finalCinematicState,
            handoff.Style));

        _logger?.LogInformation(
            "State sync completed for entity {EntityId}",
            entityId);
    }

    private Task SyncInstantAsync(
        Guid entityId,
        EntityState state,
        CancellationToken ct)
    {
        // In a full implementation, this would:
        // 1. Update the entity's position/rotation/etc in the world state
        // 2. Update the behavior stack's knowledge of the entity's current state
        // 3. Trigger any necessary re-evaluations

        // For now, this is a placeholder that completes immediately
        _logger?.LogDebug(
            "Instant state sync for entity {EntityId}: Position={Position}, Health={Health}",
            entityId,
            state.Position,
            state.Health);

        return Task.CompletedTask;
    }
}
