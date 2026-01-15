// =============================================================================
// Cutscene Session
// Represents an active multiplayer cutscene session with synchronization.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Coordination;

/// <summary>
/// Implementation of a cutscene session with multi-participant coordination.
/// </summary>
/// <remarks>
/// <para>
/// A CutsceneSession manages the server-side coordination for a single
/// cutscene execution involving multiple participants. It:
/// </para>
/// <list type="bullet">
/// <item>Tracks sync point progress across all participants</item>
/// <item>Manages input windows for QTEs and choices</item>
/// <item>Handles timeouts and defaults gracefully</item>
/// <item>Provides events for state changes</item>
/// </list>
/// </remarks>
public sealed class CutsceneSession : ICutsceneSession, IDisposable
{
    private readonly SyncPointManager _syncPointManager;
    private readonly InputWindowManager _inputWindowManager;
    private readonly Func<Guid, object?>? _behaviorDefaultResolver;
    private readonly ILogger<CutsceneSession>? _logger;
    private CutsceneSessionState _state;
    private string? _abortReason;
    private bool _disposed;

    /// <summary>
    /// Creates a new cutscene session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="cinematicId">The cinematic being executed.</param>
    /// <param name="participants">All participant entity IDs.</param>
    /// <param name="options">Session options.</param>
    /// <param name="behaviorDefaultResolver">Optional resolver for behavior defaults.</param>
    /// <param name="logger">Optional logger.</param>
    public CutsceneSession(
        string sessionId,
        string cinematicId,
        IReadOnlyList<Guid> participants,
        CutsceneSessionOptions options,
        Func<Guid, object?>? behaviorDefaultResolver = null,
        ILogger<CutsceneSession>? logger = null)
    {
        SessionId = sessionId;
        CinematicId = cinematicId;
        Participants = participants;
        Options = options;
        _behaviorDefaultResolver = behaviorDefaultResolver;
        _logger = logger;

        StartedAt = DateTime.UtcNow;
        _state = CutsceneSessionState.Initializing;

        // Create managers
        var participantSet = participants.ToHashSet();
        _syncPointManager = new SyncPointManager(
            participantSet,
            options.DefaultSyncTimeout,
            logger != null ? LoggerFactory.Create(b => { }).CreateLogger<SyncPointManager>() : null);

        _inputWindowManager = new InputWindowManager(
            options.DefaultInputTimeout,
            options.UseBehaviorDefaults ? behaviorDefaultResolver : null,
            logger != null ? LoggerFactory.Create(b => { }).CreateLogger<InputWindowManager>() : null);

        // Wire up events
        _syncPointManager.SyncPointCompleted += OnSyncPointCompleted;
        _syncPointManager.SyncPointTimedOut += OnSyncPointTimedOut;
        _inputWindowManager.WindowCompleted += OnInputWindowCompleted;
        _inputWindowManager.WindowTimedOut += OnInputWindowTimedOut;

        // Transition to active
        SetState(CutsceneSessionState.Active, "Session created");
    }

    /// <inheritdoc/>
    public string SessionId { get; }

    /// <inheritdoc/>
    public string CinematicId { get; }

    /// <inheritdoc/>
    public IReadOnlyList<Guid> Participants { get; }

    /// <inheritdoc/>
    public CutsceneSessionState State => _state;

    /// <inheritdoc/>
    public DateTime StartedAt { get; }

    /// <inheritdoc/>
    public CutsceneSessionOptions Options { get; }

    /// <inheritdoc/>
    public ISyncPointManager SyncPoints => _syncPointManager;

    /// <inheritdoc/>
    public IInputWindowManager InputWindows => _inputWindowManager;

    /// <inheritdoc/>
    public event EventHandler<SyncPointReachedEventArgs>? SyncPointReached;

    /// <inheritdoc/>
    public event EventHandler<InputWindowResultEventArgs>? InputWindowResult;

    /// <inheritdoc/>
    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public async Task<SyncPointResult> ReportSyncReachedAsync(
        string syncPointId,
        Guid entityId,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state != CutsceneSessionState.Active && _state != CutsceneSessionState.WaitingForSync)
        {
            return new SyncPointResult
            {
                AllReached = false,
                ReachedParticipants = new HashSet<Guid>(),
                PendingParticipants = Participants.ToHashSet()
            };
        }

        var status = await _syncPointManager.ReportReachedAsync(syncPointId, entityId, ct);

        // Update state if waiting for sync
        if (status.State == SyncPointState.Waiting && _state == CutsceneSessionState.Active)
        {
            SetState(CutsceneSessionState.WaitingForSync, $"Waiting for sync point: {syncPointId}");
        }
        else if (status.State != SyncPointState.Waiting && _state == CutsceneSessionState.WaitingForSync)
        {
            SetState(CutsceneSessionState.Active, $"Sync point completed: {syncPointId}");
        }

        return new SyncPointResult
        {
            AllReached = status.IsComplete,
            ReachedParticipants = status.ReachedParticipants,
            PendingParticipants = status.PendingParticipants,
            TimedOut = status.TimedOut
        };
    }

    /// <inheritdoc/>
    public async Task<IInputWindow> CreateInputWindowAsync(
        InputWindowOptions options,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == CutsceneSessionState.Completed || _state == CutsceneSessionState.Aborted)
        {
            throw new InvalidOperationException($"Cannot create input window in state {_state}");
        }

        // Resolve behavior default if needed
        var effectiveOptions = options;
        if (options.DefaultSource == DefaultValueSource.Behavior &&
            options.DefaultValue == null &&
            _behaviorDefaultResolver != null)
        {
            var behaviorDefault = _behaviorDefaultResolver(options.TargetEntity);
            effectiveOptions = new InputWindowOptions
            {
                TargetEntity = options.TargetEntity,
                WindowType = options.WindowType,
                Timeout = options.Timeout,
                Options = options.Options,
                DefaultValue = behaviorDefault,
                DefaultSource = options.DefaultSource,
                EmitSyncOnComplete = options.EmitSyncOnComplete,
                PromptText = options.PromptText,
                WindowId = options.WindowId
            };
        }

        var window = await _inputWindowManager.CreateAsync(effectiveOptions, ct);

        if (_state == CutsceneSessionState.Active)
        {
            SetState(CutsceneSessionState.WaitingForInput, $"Waiting for input from {options.TargetEntity}");
        }

        return window;
    }

    /// <inheritdoc/>
    public async Task<InputSubmitResult> SubmitInputAsync(
        string windowId,
        Guid entityId,
        object input,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = await _inputWindowManager.SubmitAsync(windowId, entityId, input, ct);

        // Check if we should return to active state
        if (_state == CutsceneSessionState.WaitingForInput &&
            _inputWindowManager.GetWindowsForEntity(entityId).Count == 0)
        {
            SetState(CutsceneSessionState.Active, "Input received");
        }

        // Handle sync emission if specified
        var window = _inputWindowManager.GetWindow(windowId);
        if (window is InputWindowImpl impl && impl.EmitSyncOnComplete != null && result.Accepted)
        {
            _syncPointManager.RegisterSyncPoint(impl.EmitSyncOnComplete);
            await _syncPointManager.ReportReachedAsync(impl.EmitSyncOnComplete, entityId, ct);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == CutsceneSessionState.Completed || _state == CutsceneSessionState.Aborted)
        {
            return;
        }

        SetState(CutsceneSessionState.Completed, "Session completed successfully");

        _logger?.LogInformation(
            "Cutscene session {SessionId} completed after {Duration}ms",
            SessionId,
            (DateTime.UtcNow - StartedAt).TotalMilliseconds);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }

    /// <inheritdoc/>
    public async Task AbortAsync(string reason, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state == CutsceneSessionState.Completed || _state == CutsceneSessionState.Aborted)
        {
            return;
        }

        _abortReason = reason;
        SetState(CutsceneSessionState.Aborted, $"Session aborted: {reason}");

        _logger?.LogWarning(
            "Cutscene session {SessionId} aborted: {Reason}",
            SessionId,
            reason);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }

    private void SetState(CutsceneSessionState newState, string? reason = null)
    {
        var previousState = _state;
        if (previousState == newState)
        {
            return;
        }

        _state = newState;

        StateChanged?.Invoke(this, new SessionStateChangedEventArgs
        {
            PreviousState = previousState,
            NewState = newState,
            Reason = reason
        });
    }

    private void OnSyncPointCompleted(object? sender, SyncPointCompletedEventArgs e)
    {
        SyncPointReached?.Invoke(this, new SyncPointReachedEventArgs
        {
            SyncPointId = e.SyncPointId,
            Participants = e.Participants
        });
    }

    private void OnSyncPointTimedOut(object? sender, SyncPointTimedOutEventArgs e)
    {
        // Sync point timeout - still raise as reached with those who made it
        SyncPointReached?.Invoke(this, new SyncPointReachedEventArgs
        {
            SyncPointId = e.SyncPointId,
            Participants = e.ReachedParticipants
        });
    }

    private void OnInputWindowCompleted(object? sender, InputWindowCompletedEventArgs e)
    {
        InputWindowResult?.Invoke(this, new InputWindowResultEventArgs
        {
            WindowId = e.WindowId,
            TargetEntity = e.TargetEntity,
            Result = e.WasDefault
                ? InputSubmitResult.Timeout(e.FinalValue)
                : InputSubmitResult.Accept(e.FinalValue ?? new object())
        });
    }

    private void OnInputWindowTimedOut(object? sender, InputWindowTimedOutEventArgs e)
    {
        // Already handled by OnInputWindowCompleted
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Unwire events
        _syncPointManager.SyncPointCompleted -= OnSyncPointCompleted;
        _syncPointManager.SyncPointTimedOut -= OnSyncPointTimedOut;
        _inputWindowManager.WindowCompleted -= OnInputWindowCompleted;
        _inputWindowManager.WindowTimedOut -= OnInputWindowTimedOut;

        _syncPointManager.Dispose();
        _inputWindowManager.Dispose();
    }
}
