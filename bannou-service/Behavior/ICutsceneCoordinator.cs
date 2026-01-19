// =============================================================================
// Cutscene Coordinator Interface
// Server-side coordination for multi-participant cutscenes.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Coordinates multi-participant cutscenes across distributed clients.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator manages the server-side master timeline for cutscenes
/// involving multiple players/entities across different clients.
/// </para>
/// <para>
/// Key responsibilities:
/// </para>
/// <list type="bullet">
/// <item>Track sync point progress across all participants</item>
/// <item>Create and manage input windows for QTEs and choices</item>
/// <item>Handle disconnection gracefully with defaults</item>
/// <item>Distribute cutscene state updates to all participants</item>
/// </list>
/// </remarks>
public interface ICutsceneCoordinator
{
    /// <summary>
    /// Creates a new cutscene session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="cinematicId">The cinematic being executed.</param>
    /// <param name="participants">Entity IDs of all participants.</param>
    /// <param name="options">Session options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created session.</returns>
    Task<ICutsceneSession> CreateSessionAsync(
        string sessionId,
        string cinematicId,
        IReadOnlyList<Guid> participants,
        CutsceneSessionOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Gets an existing session by ID.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>The session, or null if not found.</returns>
    ICutsceneSession? GetSession(string sessionId);

    /// <summary>
    /// Ends a session and cleans up resources.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EndSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    IReadOnlyCollection<ICutsceneSession> ActiveSessions { get; }
}

/// <summary>
/// Represents an active cutscene session with multiple participants.
/// </summary>
public interface ICutsceneSession : IDisposable
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// The cinematic being executed.
    /// </summary>
    string CinematicId { get; }

    /// <summary>
    /// All participant entity IDs.
    /// </summary>
    IReadOnlyList<Guid> Participants { get; }

    /// <summary>
    /// Current session state.
    /// </summary>
    CutsceneSessionState State { get; }

    /// <summary>
    /// When the session started.
    /// </summary>
    DateTime StartedAt { get; }

    /// <summary>
    /// Session options.
    /// </summary>
    CutsceneSessionOptions Options { get; }

    /// <summary>
    /// Gets the sync point manager for this session.
    /// </summary>
    ISyncPointManager SyncPoints { get; }

    /// <summary>
    /// Gets the input window manager for this session.
    /// </summary>
    IInputWindowManager InputWindows { get; }

    /// <summary>
    /// Reports that an entity has reached a sync point.
    /// </summary>
    /// <param name="syncPointId">The sync point identifier.</param>
    /// <param name="entityId">The entity that reached the point.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when all participants have reached the sync point.</returns>
    Task<SyncPointResult> ReportSyncReachedAsync(
        string syncPointId,
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates an input window for a participant.
    /// </summary>
    /// <param name="options">Input window options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created input window.</returns>
    Task<IInputWindow> CreateInputWindowAsync(
        InputWindowOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Submits input for an active input window.
    /// </summary>
    /// <param name="windowId">The input window ID.</param>
    /// <param name="entityId">The entity submitting.</param>
    /// <param name="input">The input value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The input result.</returns>
    Task<InputSubmitResult> SubmitInputAsync(
        string windowId,
        Guid entityId,
        object input,
        CancellationToken ct = default);

    /// <summary>
    /// Marks the session as completed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task CompleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Aborts the session.
    /// </summary>
    /// <param name="reason">Abort reason.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AbortAsync(string reason, CancellationToken ct = default);

    /// <summary>
    /// Event raised when all participants reach a sync point.
    /// </summary>
    event EventHandler<SyncPointReachedEventArgs>? SyncPointReached;

    /// <summary>
    /// Event raised when an input window result is available.
    /// </summary>
    event EventHandler<InputWindowResultEventArgs>? InputWindowResult;

    /// <summary>
    /// Event raised when the session state changes.
    /// </summary>
    event EventHandler<SessionStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// State of a cutscene session.
/// </summary>
public enum CutsceneSessionState
{
    /// <summary>Session is being set up.</summary>
    Initializing,

    /// <summary>Session is active and running.</summary>
    Active,

    /// <summary>Waiting for participants at a sync point.</summary>
    WaitingForSync,

    /// <summary>Waiting for input from a participant.</summary>
    WaitingForInput,

    /// <summary>Session completed successfully.</summary>
    Completed,

    /// <summary>Session was aborted.</summary>
    Aborted
}

/// <summary>
/// Options for creating a cutscene session.
/// </summary>
public sealed class CutsceneSessionOptions
{
    /// <summary>
    /// Default timeout for sync points (null = wait indefinitely in single-player).
    /// </summary>
    public TimeSpan? DefaultSyncTimeout { get; init; }

    /// <summary>
    /// Default timeout for input windows.
    /// </summary>
    public TimeSpan DefaultInputTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to use behavior defaults when input times out.
    /// </summary>
    public bool UseBehaviorDefaults { get; init; } = true;

    /// <summary>
    /// Whether the cutscene is skippable.
    /// </summary>
    public SkippableMode Skippable { get; init; } = SkippableMode.NotSkippable;

    /// <summary>
    /// Skip destinations per branch (for when cutscene is skipped).
    /// </summary>
    public IReadOnlyDictionary<string, SkipDestination>? SkipDestinations { get; init; }

    /// <summary>
    /// Creates default options.
    /// </summary>
    public static CutsceneSessionOptions Default => new();

    /// <summary>
    /// Creates options for single-player (no timeouts).
    /// </summary>
    public static CutsceneSessionOptions SinglePlayer => new()
    {
        DefaultSyncTimeout = null,
        DefaultInputTimeout = TimeSpan.MaxValue,
        Skippable = SkippableMode.Easily
    };

    /// <summary>
    /// Creates options for multiplayer.
    /// </summary>
    public static CutsceneSessionOptions Multiplayer => new()
    {
        DefaultSyncTimeout = TimeSpan.FromSeconds(30),
        DefaultInputTimeout = TimeSpan.FromSeconds(10),
        Skippable = SkippableMode.NotSkippable
    };
}

/// <summary>
/// Skippable mode for cutscenes.
/// </summary>
public enum SkippableMode
{
    /// <summary>Cannot be skipped.</summary>
    NotSkippable,

    /// <summary>Can be skipped easily.</summary>
    Easily,

    /// <summary>Can be skipped with penalty.</summary>
    WithPenalty
}

/// <summary>
/// Skip destination for an entity.
/// </summary>
public sealed class SkipDestination
{
    /// <summary>
    /// Entity positions after skip.
    /// </summary>
    public required IReadOnlyDictionary<Guid, EntityState> EntityStates { get; init; }
}

/// <summary>
/// Result of a sync point check.
/// </summary>
public sealed class SyncPointResult
{
    /// <summary>
    /// Whether all participants have reached the sync point.
    /// </summary>
    public bool AllReached { get; init; }

    /// <summary>
    /// Participants who have reached the sync point.
    /// </summary>
    public required IReadOnlySet<Guid> ReachedParticipants { get; init; }

    /// <summary>
    /// Participants still pending.
    /// </summary>
    public required IReadOnlySet<Guid> PendingParticipants { get; init; }

    /// <summary>
    /// Whether the sync point timed out.
    /// </summary>
    public bool TimedOut { get; init; }
}

/// <summary>
/// Result of submitting input.
/// </summary>
public sealed class InputSubmitResult
{
    /// <summary>
    /// Whether the input was accepted.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// The final adjudicated value (may differ from submitted).
    /// </summary>
    public object? AdjudicatedValue { get; init; }

    /// <summary>
    /// Reason if input was rejected.
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// Whether this was the timeout default.
    /// </summary>
    public bool WasDefault { get; init; }

    /// <summary>
    /// Creates an accepted result.
    /// </summary>
    public static InputSubmitResult Accept(object value) => new()
    {
        Accepted = true,
        AdjudicatedValue = value
    };

    /// <summary>
    /// Creates a rejected result.
    /// </summary>
    public static InputSubmitResult Reject(string reason) => new()
    {
        Accepted = false,
        RejectionReason = reason
    };

    /// <summary>
    /// Creates a timeout default result.
    /// </summary>
    public static InputSubmitResult Timeout(object? defaultValue) => new()
    {
        Accepted = true,
        AdjudicatedValue = defaultValue,
        WasDefault = true
    };
}

/// <summary>
/// Event args for sync point reached.
/// </summary>
public sealed class SyncPointReachedEventArgs : EventArgs
{
    /// <summary>The sync point ID.</summary>
    public required string SyncPointId { get; init; }

    /// <summary>All participants who reached it.</summary>
    public required IReadOnlySet<Guid> Participants { get; init; }

    /// <summary>When all reached.</summary>
    public DateTime ReachedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for input window result.
/// </summary>
public sealed class InputWindowResultEventArgs : EventArgs
{
    /// <summary>The window ID.</summary>
    public required string WindowId { get; init; }

    /// <summary>The target entity.</summary>
    public Guid TargetEntity { get; init; }

    /// <summary>The result.</summary>
    public required InputSubmitResult Result { get; init; }
}

/// <summary>
/// Event args for session state change.
/// </summary>
public sealed class SessionStateChangedEventArgs : EventArgs
{
    /// <summary>Previous state.</summary>
    public CutsceneSessionState PreviousState { get; init; }

    /// <summary>New state.</summary>
    public CutsceneSessionState NewState { get; init; }

    /// <summary>Optional reason for the change.</summary>
    public string? Reason { get; init; }
}
