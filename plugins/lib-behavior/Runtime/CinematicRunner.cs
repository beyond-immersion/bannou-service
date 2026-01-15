// =============================================================================
// Cinematic Controller
// High-level controller that integrates CinematicInterpreter with control gating.
// Manages entity control acquisition/release and state synchronization.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Runtime;

/// <summary>
/// High-level controller for cinematic playback with entity control management.
/// </summary>
/// <remarks>
/// <para>
/// The CinematicRunner wraps a CinematicInterpreter and manages the
/// control gating lifecycle for participating entities:
/// </para>
/// <list type="number">
/// <item>Takes control of entities when the cinematic starts</item>
/// <item>Manages the interpreter's evaluation loop</item>
/// <item>Returns control when the cinematic completes</item>
/// <item>Synchronizes entity state on completion</item>
/// </list>
/// </remarks>
public sealed class CinematicRunner : IDisposable
{
    private readonly CinematicInterpreter _interpreter;
    private readonly ControlGateManager _controlGates;
    private readonly IStateSync _stateSync;
    private readonly ILogger<CinematicRunner>? _logger;

    private string _cinematicId = string.Empty;
    private HashSet<Guid> _controlledEntities = new();
    private IReadOnlySet<string>? _allowBehaviorChannels;
    private CinematicRunnerState _state;
    private Dictionary<Guid, EntityState> _entityFinalStates = new();
    private ControlHandoff _defaultHandoff = ControlHandoff.Instant();
    private bool _disposed;

    /// <summary>
    /// Creates a new cinematic controller.
    /// </summary>
    /// <param name="interpreter">The cinematic interpreter to control.</param>
    /// <param name="controlGates">The control gate manager.</param>
    /// <param name="stateSync">The state sync service.</param>
    /// <param name="logger">Optional logger.</param>
    public CinematicRunner(
        CinematicInterpreter interpreter,
        ControlGateManager controlGates,
        IStateSync stateSync,
        ILogger<CinematicRunner>? logger = null)
    {
        _interpreter = interpreter;
        _controlGates = controlGates;
        _stateSync = stateSync;
        _logger = logger;
        _state = CinematicRunnerState.Idle;
    }

    /// <summary>
    /// Gets the current controller state.
    /// </summary>
    public CinematicRunnerState State => _state;

    /// <summary>
    /// Gets the cinematic ID.
    /// </summary>
    public string CinematicId => _cinematicId;

    /// <summary>
    /// Gets the entities currently under cinematic control.
    /// </summary>
    public IReadOnlySet<Guid> ControlledEntities => _controlledEntities;

    /// <summary>
    /// Gets whether the cinematic is currently running.
    /// </summary>
    public bool IsRunning => _state == CinematicRunnerState.Running ||
                            _state == CinematicRunnerState.WaitingForExtension;

    /// <summary>
    /// Gets the underlying interpreter.
    /// </summary>
    public CinematicInterpreter Interpreter => _interpreter;

    /// <summary>
    /// Event raised when the cinematic starts.
    /// </summary>
    public event EventHandler<CinematicStartedEventArgs>? CinematicStarted;

    /// <summary>
    /// Event raised when the cinematic completes.
    /// </summary>
    public event EventHandler<CinematicCompletedEventArgs>? CinematicCompleted;

    /// <summary>
    /// Event raised when control is returned to an entity.
    /// </summary>
    public event EventHandler<ControlReturnedEventArgs>? ControlReturned;

    /// <summary>
    /// Starts the cinematic, taking control of the specified entities.
    /// </summary>
    /// <param name="cinematicId">The cinematic identifier.</param>
    /// <param name="entities">The entities to control.</param>
    /// <param name="allowBehaviorChannels">Optional channels where behavior can still contribute.</param>
    /// <param name="defaultHandoff">Default handoff style when cinematic completes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if control was acquired and cinematic started.</returns>
    public async Task<bool> StartAsync(
        string cinematicId,
        IEnumerable<Guid> entities,
        IReadOnlySet<string>? allowBehaviorChannels = null,
        ControlHandoff? defaultHandoff = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(cinematicId))
        {
            throw new ArgumentException("Cinematic ID cannot be null or empty", nameof(cinematicId));
        }


        if (_state != CinematicRunnerState.Idle)
        {
            _logger?.LogWarning(
                "Cannot start cinematic {CinematicId}: controller is in state {State}",
                cinematicId,
                _state);
            return false;
        }

        _cinematicId = cinematicId;
        _controlledEntities = new HashSet<Guid>(entities);
        _allowBehaviorChannels = allowBehaviorChannels;
        _defaultHandoff = defaultHandoff ?? ControlHandoff.Instant();
        _entityFinalStates.Clear();

        if (_controlledEntities.Count == 0)
        {
            _logger?.LogWarning(
                "Cinematic {CinematicId} has no entities to control",
                cinematicId);
        }

        // Take control of all entities
        var success = await _controlGates.TakeCinematicControlAsync(
            _controlledEntities,
            cinematicId,
            allowBehaviorChannels);

        if (!success)
        {
            _logger?.LogWarning(
                "Failed to acquire control of some entities for cinematic {CinematicId}",
                cinematicId);
            // Continue anyway - partial control is acceptable
        }

        _state = CinematicRunnerState.Running;

        _logger?.LogInformation(
            "Started cinematic {CinematicId} with {EntityCount} entities",
            cinematicId,
            _controlledEntities.Count);

        // Raise started event
        CinematicStarted?.Invoke(this, new CinematicStartedEventArgs(
            cinematicId,
            _controlledEntities.ToList().AsReadOnly(),
            allowBehaviorChannels));

        return true;
    }

    /// <summary>
    /// Evaluates one frame of the cinematic.
    /// </summary>
    /// <returns>The evaluation result.</returns>
    public CinematicEvaluationResult Evaluate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == CinematicRunnerState.Idle)
        {
            return new CinematicEvaluationResult(
                CinematicStatus.Completed,
                null,
                "Cinematic not started");
        }

        if (_state == CinematicRunnerState.Completed)
        {
            return new CinematicEvaluationResult(
                CinematicStatus.Completed,
                null,
                "Cinematic already completed");
        }

        var result = _interpreter.Evaluate();

        // Update state based on result
        if (result.IsWaiting)
        {
            _state = CinematicRunnerState.WaitingForExtension;
        }
        else if (result.IsCompleted)
        {
            _state = CinematicRunnerState.Completed;
        }
        else
        {
            _state = CinematicRunnerState.Running;
        }

        return result;
    }

    /// <summary>
    /// Sets the final state for an entity (to be used during control return).
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="state">The entity's final state.</param>
    public void SetEntityFinalState(Guid entityId, EntityState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _entityFinalStates[entityId] = state;
    }

    /// <summary>
    /// Completes the cinematic and returns control to entities.
    /// </summary>
    /// <param name="handoff">Optional handoff override (uses default if not specified).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CompleteAsync(
        ControlHandoff? handoff = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == CinematicRunnerState.Idle)
        {
            _logger?.LogDebug("Cannot complete cinematic: not started");
            return;
        }

        var effectiveHandoff = handoff ?? _defaultHandoff;

        _logger?.LogInformation(
            "Completing cinematic {CinematicId} with handoff style {Style}",
            _cinematicId,
            effectiveHandoff.Style);

        // Sync state and return control for each entity
        foreach (var entityId in _controlledEntities)
        {
            await ReturnEntityControlAsync(entityId, effectiveHandoff, ct);
        }

        // Return control via manager
        await _controlGates.ReturnCinematicControlAsync(_controlledEntities, effectiveHandoff);

        _state = CinematicRunnerState.Completed;

        // Raise completed event
        CinematicCompleted?.Invoke(this, new CinematicCompletedEventArgs(
            _cinematicId,
            _controlledEntities.ToList().AsReadOnly(),
            effectiveHandoff));

        _logger?.LogInformation(
            "Cinematic {CinematicId} completed, control returned to {EntityCount} entities",
            _cinematicId,
            _controlledEntities.Count);
    }

    /// <summary>
    /// Aborts the cinematic immediately.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task AbortAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == CinematicRunnerState.Idle)
        {
            return;
        }

        _logger?.LogWarning("Aborting cinematic {CinematicId}", _cinematicId);

        // Use instant handoff for abort
        var handoff = ControlHandoff.Instant();

        // Return control immediately without state sync
        foreach (var entityId in _controlledEntities)
        {
            var gate = _controlGates.Get(entityId);
            if (gate != null && gate.CurrentSource == ControlSource.Cinematic)
            {
                await gate.ReturnControlAsync(handoff);
            }
        }

        _state = CinematicRunnerState.Completed;

        // Raise completed event with abort indication
        CinematicCompleted?.Invoke(this, new CinematicCompletedEventArgs(
            _cinematicId,
            _controlledEntities.ToList().AsReadOnly(),
            handoff,
            wasAborted: true));
    }

    /// <summary>
    /// Resets the controller to idle state for reuse.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _interpreter.Reset();
        _cinematicId = string.Empty;
        _controlledEntities.Clear();
        _allowBehaviorChannels = null;
        _entityFinalStates.Clear();
        _state = CinematicRunnerState.Idle;

        _logger?.LogDebug("Cinematic controller reset");
    }

    private async Task ReturnEntityControlAsync(
        Guid entityId,
        ControlHandoff handoff,
        CancellationToken ct)
    {
        // Get final state for this entity
        if (!_entityFinalStates.TryGetValue(entityId, out var finalState))
        {
            finalState = EntityState.Empty;
        }

        // Create handoff with entity's final state
        var entityHandoff = handoff with
        {
            FinalState = finalState,
            SyncState = handoff.SyncState && finalState != EntityState.Empty
        };

        // Sync state if requested
        if (entityHandoff.SyncState && entityHandoff.FinalState != null)
        {
            await _stateSync.SyncStateAsync(entityId, entityHandoff.FinalState, entityHandoff, ct);
        }

        // Raise control returned event
        ControlReturned?.Invoke(this, new ControlReturnedEventArgs(
            entityId,
            _cinematicId,
            entityHandoff));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // If still running, abort
        if (IsRunning)
        {
            AbortAsync().GetAwaiter().GetResult();
        }

        _disposed = true;
    }
}

/// <summary>
/// State of a cinematic controller.
/// </summary>
public enum CinematicRunnerState
{
    /// <summary>Not started or reset.</summary>
    Idle,

    /// <summary>Cinematic is actively running.</summary>
    Running,

    /// <summary>Waiting at a continuation point for an extension.</summary>
    WaitingForExtension,

    /// <summary>Cinematic has completed.</summary>
    Completed
}

/// <summary>
/// Event args for cinematic started event.
/// </summary>
public sealed class CinematicStartedEventArgs : EventArgs
{
    /// <summary>
    /// Creates new event args.
    /// </summary>
    /// <param name="cinematicId">The cinematic ID.</param>
    /// <param name="entities">The controlled entities.</param>
    /// <param name="allowBehaviorChannels">Channels where behavior can contribute.</param>
    public CinematicStartedEventArgs(
        string cinematicId,
        IReadOnlyList<Guid> entities,
        IReadOnlySet<string>? allowBehaviorChannels)
    {
        CinematicId = cinematicId;
        Entities = entities;
        AllowBehaviorChannels = allowBehaviorChannels;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>Gets the cinematic ID.</summary>
    public string CinematicId { get; }

    /// <summary>Gets the controlled entities.</summary>
    public IReadOnlyList<Guid> Entities { get; }

    /// <summary>Gets channels where behavior can contribute.</summary>
    public IReadOnlySet<string>? AllowBehaviorChannels { get; }

    /// <summary>Gets when the cinematic started.</summary>
    public DateTime StartedAt { get; }
}

/// <summary>
/// Event args for cinematic completed event.
/// </summary>
public sealed class CinematicCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Creates new event args.
    /// </summary>
    /// <param name="cinematicId">The cinematic ID.</param>
    /// <param name="entities">The controlled entities.</param>
    /// <param name="handoff">The handoff protocol used.</param>
    /// <param name="wasAborted">Whether the cinematic was aborted.</param>
    public CinematicCompletedEventArgs(
        string cinematicId,
        IReadOnlyList<Guid> entities,
        ControlHandoff handoff,
        bool wasAborted = false)
    {
        CinematicId = cinematicId;
        Entities = entities;
        Handoff = handoff;
        WasAborted = wasAborted;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Gets the cinematic ID.</summary>
    public string CinematicId { get; }

    /// <summary>Gets the controlled entities.</summary>
    public IReadOnlyList<Guid> Entities { get; }

    /// <summary>Gets the handoff protocol used.</summary>
    public ControlHandoff Handoff { get; }

    /// <summary>Gets whether the cinematic was aborted.</summary>
    public bool WasAborted { get; }

    /// <summary>Gets when the cinematic completed.</summary>
    public DateTime CompletedAt { get; }
}

/// <summary>
/// Event args for control returned event.
/// </summary>
public sealed class ControlReturnedEventArgs : EventArgs
{
    /// <summary>
    /// Creates new event args.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="cinematicId">The cinematic ID.</param>
    /// <param name="handoff">The handoff protocol used.</param>
    public ControlReturnedEventArgs(
        Guid entityId,
        string cinematicId,
        ControlHandoff handoff)
    {
        EntityId = entityId;
        CinematicId = cinematicId;
        Handoff = handoff;
        ReturnedAt = DateTime.UtcNow;
    }

    /// <summary>Gets the entity ID.</summary>
    public Guid EntityId { get; }

    /// <summary>Gets the cinematic ID.</summary>
    public string CinematicId { get; }

    /// <summary>Gets the handoff protocol used.</summary>
    public ControlHandoff Handoff { get; }

    /// <summary>Gets when control was returned.</summary>
    public DateTime ReturnedAt { get; }
}
