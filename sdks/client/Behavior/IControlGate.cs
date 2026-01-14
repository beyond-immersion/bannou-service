// =============================================================================
// Control Gate
// Manages which system controls entity intent channels.
// =============================================================================

using BeyondImmersion.Bannou.Client.Behavior.Intent;

namespace BeyondImmersion.Bannou.Client.Behavior;

/// <summary>
/// Controls which system writes to entity intent channels.
/// </summary>
/// <remarks>
/// <para>
/// The control gate determines the priority of different input sources:
/// </para>
/// <list type="number">
/// <item><b>Cinematic</b> (highest) - Forced, takes full control</item>
/// <item><b>Player</b> - Direct player commands</item>
/// <item><b>Opportunity</b> - Optional, becomes cinematic if accepted</item>
/// <item><b>Behavior</b> (lowest) - Autonomous AI behavior stack</item>
/// </list>
/// <para>
/// During cinematics, the behavior stack continues evaluating (for QTE defaults)
/// but its outputs are masked and not applied to entities.
/// </para>
/// </remarks>
public interface IControlGate
{
    /// <summary>
    /// Gets the current control mode for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The current control mode.</returns>
    ControlMode GetControlMode(Guid entityId);

    /// <summary>
    /// Takes cinematic control of entities.
    /// </summary>
    /// <remarks>
    /// While under cinematic control:
    /// <list type="bullet">
    /// <item>Behavior stack continues evaluating (for QTE defaults)</item>
    /// <item>Behavior outputs are masked (not sent to entities)</item>
    /// <item>Player input is disabled (except for QTEs)</item>
    /// <item>Cinematic commands are sent directly to entities</item>
    /// </list>
    /// </remarks>
    /// <param name="entities">Entities to take control of.</param>
    /// <param name="sessionId">The cutscene session ID.</param>
    void TakeCinematicControl(IReadOnlyList<Guid> entities, string sessionId);

    /// <summary>
    /// Returns control to normal behavior for entities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When control is returned:
    /// </para>
    /// <list type="bullet">
    /// <item>Behavior stack outputs are unmasked</item>
    /// <item>Player input is re-enabled</item>
    /// <item>Entity state may need sync (see handoff options)</item>
    /// </list>
    /// </remarks>
    /// <param name="entities">Entities to return control for.</param>
    void ReturnControl(IReadOnlyList<Guid> entities);

    /// <summary>
    /// Gets the session ID for an entity under cinematic control.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The session ID, or null if not under cinematic control.</returns>
    string? GetSessionId(Guid entityId);

    /// <summary>
    /// Gets all entities currently in a cutscene session.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>Entities in the session.</returns>
    IReadOnlyList<Guid> GetEntitiesInSession(string sessionId);

    /// <summary>
    /// Gets the behavior stack's computed output for an entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This returns what the behavior stack WOULD output if it had control.
    /// Useful for QTE defaults where the character agent computes "what I would do."
    /// </para>
    /// <para>
    /// Returns null if the entity has no active behavior stack.
    /// </para>
    /// </remarks>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The behavior stack's computed intent, or null.</returns>
    MergedIntent? GetBehaviorDefault(Guid entityId);

    /// <summary>
    /// Checks if an entity is currently accepting player input.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if player input is accepted.</returns>
    bool IsPlayerInputAllowed(Guid entityId);

    /// <summary>
    /// Temporarily allows player input during cinematic (for QTE).
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="windowId">The input window ID.</param>
    void AllowPlayerInputForWindow(Guid entityId, string windowId);

    /// <summary>
    /// Revokes temporary player input allowance.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="windowId">The input window ID.</param>
    void RevokePlayerInputForWindow(Guid entityId, string windowId);

    /// <summary>
    /// Event raised when control mode changes for an entity.
    /// </summary>
    event EventHandler<ControlModeChangedEventArgs>? ControlModeChanged;
}

/// <summary>
/// Control mode for an entity.
/// </summary>
public enum ControlMode
{
    /// <summary>Normal behavior stack control.</summary>
    Behavior,

    /// <summary>Cinematic has full control, behavior masked.</summary>
    Cinematic,

    /// <summary>Player has direct control.</summary>
    Player,

    /// <summary>Opportunity is being presented (may become cinematic).</summary>
    Opportunity
}

/// <summary>
/// Event args for control mode changes.
/// </summary>
public sealed class ControlModeChangedEventArgs : EventArgs
{
    /// <summary>
    /// The entity whose control mode changed.
    /// </summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// The previous control mode.
    /// </summary>
    public ControlMode PreviousMode { get; init; }

    /// <summary>
    /// The new control mode.
    /// </summary>
    public ControlMode NewMode { get; init; }

    /// <summary>
    /// The session ID (if entering/exiting cinematic).
    /// </summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// Default implementation of control gate.
/// </summary>
public sealed class DefaultControlGate : IControlGate
{
    private readonly Dictionary<Guid, ControlState> _entityStates = new();
    private readonly Dictionary<string, HashSet<Guid>> _sessionEntities = new();
    private readonly object _lock = new();

    /// <inheritdoc/>
    public event EventHandler<ControlModeChangedEventArgs>? ControlModeChanged;

    /// <summary>
    /// Registers an entity's behavior stack for QTE defaults.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="behaviorProvider">Function that returns the current merged intent.</param>
    public void RegisterBehaviorProvider(Guid entityId, Func<MergedIntent?> behaviorProvider)
    {
        lock (_lock)
        {
            if (!_entityStates.TryGetValue(entityId, out var state))
            {
                state = new ControlState();
                _entityStates[entityId] = state;
            }
            state.BehaviorProvider = behaviorProvider;
        }
    }

    /// <inheritdoc/>
    public ControlMode GetControlMode(Guid entityId)
    {
        lock (_lock)
        {
            return _entityStates.TryGetValue(entityId, out var state) ? state.Mode : ControlMode.Behavior;
        }
    }

    /// <inheritdoc/>
    public void TakeCinematicControl(IReadOnlyList<Guid> entities, string sessionId)
    {
        lock (_lock)
        {
            if (!_sessionEntities.ContainsKey(sessionId))
            {
                _sessionEntities[sessionId] = new HashSet<Guid>();
            }

            foreach (var entityId in entities)
            {
                var previousMode = GetControlMode(entityId);

                if (!_entityStates.TryGetValue(entityId, out var state))
                {
                    state = new ControlState();
                    _entityStates[entityId] = state;
                }

                state.Mode = ControlMode.Cinematic;
                state.SessionId = sessionId;
                _sessionEntities[sessionId].Add(entityId);

                ControlModeChanged?.Invoke(this, new ControlModeChangedEventArgs
                {
                    EntityId = entityId,
                    PreviousMode = previousMode,
                    NewMode = ControlMode.Cinematic,
                    SessionId = sessionId
                });
            }
        }
    }

    /// <inheritdoc/>
    public void ReturnControl(IReadOnlyList<Guid> entities)
    {
        lock (_lock)
        {
            foreach (var entityId in entities)
            {
                if (!_entityStates.TryGetValue(entityId, out var state))
                {
                    continue;
                }

                var previousMode = state.Mode;
                var sessionId = state.SessionId;

                state.Mode = ControlMode.Behavior;
                state.SessionId = null;
                state.AllowedInputWindows.Clear();

                if (sessionId != null && _sessionEntities.TryGetValue(sessionId, out var sessionSet))
                {
                    sessionSet.Remove(entityId);
                    if (sessionSet.Count == 0)
                    {
                        _sessionEntities.Remove(sessionId);
                    }
                }

                ControlModeChanged?.Invoke(this, new ControlModeChangedEventArgs
                {
                    EntityId = entityId,
                    PreviousMode = previousMode,
                    NewMode = ControlMode.Behavior,
                    SessionId = sessionId
                });
            }
        }
    }

    /// <inheritdoc/>
    public string? GetSessionId(Guid entityId)
    {
        lock (_lock)
        {
            return _entityStates.TryGetValue(entityId, out var state) ? state.SessionId : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Guid> GetEntitiesInSession(string sessionId)
    {
        lock (_lock)
        {
            return _sessionEntities.TryGetValue(sessionId, out var entities)
                ? entities.ToList()
                : Array.Empty<Guid>();
        }
    }

    /// <inheritdoc/>
    public MergedIntent? GetBehaviorDefault(Guid entityId)
    {
        lock (_lock)
        {
            if (_entityStates.TryGetValue(entityId, out var state) && state.BehaviorProvider != null)
            {
                return state.BehaviorProvider();
            }
            return null;
        }
    }

    /// <inheritdoc/>
    public bool IsPlayerInputAllowed(Guid entityId)
    {
        lock (_lock)
        {
            if (!_entityStates.TryGetValue(entityId, out var state))
            {
                return true; // Default: allow input
            }

            // Player mode or Behavior mode always allow input
            if (state.Mode is ControlMode.Player or ControlMode.Behavior)
            {
                return true;
            }

            // Cinematic mode only allows input during active input windows
            return state.AllowedInputWindows.Count > 0;
        }
    }

    /// <inheritdoc/>
    public void AllowPlayerInputForWindow(Guid entityId, string windowId)
    {
        lock (_lock)
        {
            if (!_entityStates.TryGetValue(entityId, out var state))
            {
                state = new ControlState();
                _entityStates[entityId] = state;
            }
            state.AllowedInputWindows.Add(windowId);
        }
    }

    /// <inheritdoc/>
    public void RevokePlayerInputForWindow(Guid entityId, string windowId)
    {
        lock (_lock)
        {
            if (_entityStates.TryGetValue(entityId, out var state))
            {
                state.AllowedInputWindows.Remove(windowId);
            }
        }
    }

    private sealed class ControlState
    {
        public ControlMode Mode { get; set; } = ControlMode.Behavior;
        public string? SessionId { get; set; }
        public Func<MergedIntent?>? BehaviorProvider { get; set; }
        public HashSet<string> AllowedInputWindows { get; } = new();
    }
}
