// =============================================================================
// Sync Point Manager Interface
// Manages cross-entity synchronization points in cutscenes.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Manages synchronization points for multi-participant cutscenes.
/// </summary>
/// <remarks>
/// <para>
/// Sync points allow multiple ABML channels (each potentially on different clients)
/// to coordinate their execution. When any channel reaches a sync point:
/// </para>
/// <list type="number">
/// <item>That channel pauses execution</item>
/// <item>Server tracks which participants have reached</item>
/// <item>Once all participants reach, server releases all to continue</item>
/// </list>
/// <para>
/// This enables cross-entity coordination like:
/// </para>
/// <list type="bullet">
/// <item>Hero attacks → villain reacts (villain waits for hero)</item>
/// <item>Camera shake → after impact (camera waits for attack to land)</item>
/// <item>Dialogue choice → other player responds (both synchronized)</item>
/// </list>
/// </remarks>
public interface ISyncPointManager
{
    /// <summary>
    /// Registers a sync point that will be used in this session.
    /// </summary>
    /// <param name="syncPointId">The sync point identifier.</param>
    /// <param name="requiredParticipants">Participants that must reach this point (null = all).</param>
    /// <param name="timeout">Optional timeout (null = use session default).</param>
    void RegisterSyncPoint(
        string syncPointId,
        IReadOnlySet<Guid>? requiredParticipants = null,
        TimeSpan? timeout = null);

    /// <summary>
    /// Reports that a participant has reached a sync point.
    /// </summary>
    /// <param name="syncPointId">The sync point identifier.</param>
    /// <param name="entityId">The entity that reached the point.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sync point status after this report.</returns>
    Task<SyncPointStatus> ReportReachedAsync(
        string syncPointId,
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Waits for all participants to reach a sync point.
    /// </summary>
    /// <param name="syncPointId">The sync point identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final sync point status.</returns>
    Task<SyncPointStatus> WaitForAllAsync(
        string syncPointId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current status of a sync point.
    /// </summary>
    /// <param name="syncPointId">The sync point identifier.</param>
    /// <returns>The sync point status, or null if not registered.</returns>
    SyncPointStatus? GetStatus(string syncPointId);

    /// <summary>
    /// Gets all registered sync points.
    /// </summary>
    IReadOnlyCollection<string> RegisteredSyncPoints { get; }

    /// <summary>
    /// Gets sync points that are currently waiting for participants.
    /// </summary>
    IReadOnlyCollection<string> WaitingSyncPoints { get; }

    /// <summary>
    /// Resets a sync point for reuse (e.g., in a loop).
    /// </summary>
    /// <param name="syncPointId">The sync point identifier.</param>
    void Reset(string syncPointId);

    /// <summary>
    /// Resets all sync points.
    /// </summary>
    void ResetAll();

    /// <summary>
    /// Event raised when all participants reach a sync point.
    /// </summary>
    event EventHandler<SyncPointCompletedEventArgs>? SyncPointCompleted;

    /// <summary>
    /// Event raised when a sync point times out.
    /// </summary>
    event EventHandler<SyncPointTimedOutEventArgs>? SyncPointTimedOut;
}

/// <summary>
/// Status of a sync point.
/// </summary>
public sealed class SyncPointStatus
{
    /// <summary>
    /// The sync point identifier.
    /// </summary>
    public required string SyncPointId { get; init; }

    /// <summary>
    /// Current state of the sync point.
    /// </summary>
    public SyncPointState State { get; init; }

    /// <summary>
    /// Participants required to reach this point.
    /// </summary>
    public required IReadOnlySet<Guid> RequiredParticipants { get; init; }

    /// <summary>
    /// Participants who have reached.
    /// </summary>
    public required IReadOnlySet<Guid> ReachedParticipants { get; init; }

    /// <summary>
    /// Participants still pending.
    /// </summary>
    public IReadOnlySet<Guid> PendingParticipants =>
        RequiredParticipants.Except(ReachedParticipants).ToHashSet();

    /// <summary>
    /// Whether all required participants have reached.
    /// </summary>
    public bool IsComplete => State == SyncPointState.Completed;

    /// <summary>
    /// When the first participant reached.
    /// </summary>
    public DateTime? FirstReachedAt { get; init; }

    /// <summary>
    /// When all participants reached (null if not yet).
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Timeout for this sync point.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Whether the sync point timed out.
    /// </summary>
    public bool TimedOut { get; init; }
}

/// <summary>
/// State of a sync point.
/// </summary>
public enum SyncPointState
{
    /// <summary>Waiting for participants.</summary>
    Waiting,

    /// <summary>All participants have reached.</summary>
    Completed,

    /// <summary>Timed out before all participants reached.</summary>
    TimedOut
}

/// <summary>
/// Event args for sync point completion.
/// </summary>
public sealed class SyncPointCompletedEventArgs : EventArgs
{
    /// <summary>The sync point ID.</summary>
    public required string SyncPointId { get; init; }

    /// <summary>Participants who reached.</summary>
    public required IReadOnlySet<Guid> Participants { get; init; }

    /// <summary>Duration from first reach to completion.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Event args for sync point timeout.
/// </summary>
public sealed class SyncPointTimedOutEventArgs : EventArgs
{
    /// <summary>The sync point ID.</summary>
    public required string SyncPointId { get; init; }

    /// <summary>Participants who reached before timeout.</summary>
    public required IReadOnlySet<Guid> ReachedParticipants { get; init; }

    /// <summary>Participants who did not reach.</summary>
    public required IReadOnlySet<Guid> MissingParticipants { get; init; }

    /// <summary>The timeout duration.</summary>
    public TimeSpan Timeout { get; init; }
}
