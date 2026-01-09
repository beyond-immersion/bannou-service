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
/// Default implementation of state sync that updates the entity state registry.
/// </summary>
/// <remarks>
/// <para>
/// This implementation synchronizes entity state from cinematics back to the
/// behavior system via the <see cref="IEntityStateRegistry"/>. When a cinematic
/// completes and returns control, the entity's final state (position, health,
/// stance, etc.) is written to the registry.
/// </para>
/// <para>
/// Consumers of behavior evaluation can then query the registry to include
/// current entity state in the <see cref="BehaviorEvaluationContext.Data"/>
/// dictionary, ensuring behaviors make decisions based on accurate world knowledge.
/// </para>
/// </remarks>
public sealed class StateSync : IStateSync
{
    private readonly IEntityStateRegistry _stateRegistry;
    private readonly ILogger<StateSync>? _logger;

    /// <summary>
    /// Creates a new state sync service.
    /// </summary>
    /// <param name="stateRegistry">The entity state registry to update.</param>
    /// <param name="logger">Optional logger.</param>
    public StateSync(IEntityStateRegistry stateRegistry, ILogger<StateSync>? logger = null)
    {
        _stateRegistry = stateRegistry ?? throw new ArgumentNullException(nameof(stateRegistry));
        _logger = logger;
    }

    /// <summary>
    /// Creates a new state sync service with a default registry.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <remarks>
    /// This constructor creates an internal EntityStateRegistry instance.
    /// For production use, prefer the constructor that takes IEntityStateRegistry
    /// to enable sharing state across components.
    /// </remarks>
    public StateSync(ILogger<StateSync>? logger = null)
        : this(new EntityStateRegistry(), logger)
    {
    }

    /// <inheritdoc/>
    public event EventHandler<StateSyncCompletedEventArgs>? StateSyncCompleted;

    /// <summary>
    /// Gets the state registry used by this sync service.
    /// </summary>
    /// <remarks>
    /// This allows consumers to access the registry directly for querying state
    /// or subscribing to state change events.
    /// </remarks>
    public IEntityStateRegistry StateRegistry => _stateRegistry;

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

        switch (handoff.Style)
        {
            case HandoffStyle.Instant:
                // Immediate state sync
                await SyncInstantAsync(entityId, finalCinematicState, ct);
                break;

            case HandoffStyle.Blend:
                // Blend interpolation not yet implemented - sync final state immediately
                // The blend animation would happen on the game client side
                _logger?.LogDebug(
                    "Blend handoff for entity {EntityId} using instant sync (blend interpolation deferred to client)",
                    entityId);
                await SyncInstantAsync(entityId, finalCinematicState, ct);
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

    private async Task SyncInstantAsync(
        Guid entityId,
        EntityState state,
        CancellationToken ct)
    {
        // Check for cancellation before proceeding
        ct.ThrowIfCancellationRequested();

        // Update the entity state registry with the cinematic's final state
        // This makes the state available to subsequent behavior evaluations
        _stateRegistry.UpdateState(entityId, state, source: "cinematic");

        _logger?.LogDebug(
            "Synced entity {EntityId} state to registry: Position={Position}, Health={Health}, Stance={Stance}",
            entityId,
            state.Position,
            state.Health,
            state.Stance);

        // Yield to allow other async operations to proceed
        // This ensures we don't block if called in a tight loop
        await Task.Yield();
    }
}
