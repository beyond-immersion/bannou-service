// =============================================================================
// Sync Point Manager
// Manages cross-entity synchronization points in cutscenes.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behavior.Coordination;

/// <summary>
/// Default implementation of sync point management.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe implementation that tracks multiple sync points
/// and their participant status. Supports:
/// </para>
/// <list type="bullet">
/// <item>Per-sync-point participant sets</item>
/// <item>Optional timeouts with auto-completion</item>
/// <item>Async waiting for all participants</item>
/// <item>Reset for reusable sync points</item>
/// </list>
/// </remarks>
public sealed class SyncPointManager : ISyncPointManager, IDisposable
{
    private readonly ConcurrentDictionary<string, SyncPointTracker> _syncPoints;
    private readonly IReadOnlySet<Guid> _defaultParticipants;
    private readonly TimeSpan? _defaultTimeout;
    private readonly ILogger<SyncPointManager>? _logger;
    private readonly CancellationTokenSource _disposeCts;
    private bool _disposed;

    /// <summary>
    /// Creates a new sync point manager.
    /// </summary>
    /// <param name="defaultParticipants">Default participant set for unspecified sync points.</param>
    /// <param name="defaultTimeout">Default timeout (null = no timeout).</param>
    /// <param name="logger">Optional logger.</param>
    public SyncPointManager(
        IReadOnlySet<Guid> defaultParticipants,
        TimeSpan? defaultTimeout = null,
        ILogger<SyncPointManager>? logger = null)
    {
        _defaultParticipants = defaultParticipants ?? throw new ArgumentNullException(nameof(defaultParticipants));
        _defaultTimeout = defaultTimeout;
        _logger = logger;
        _syncPoints = new ConcurrentDictionary<string, SyncPointTracker>(StringComparer.OrdinalIgnoreCase);
        _disposeCts = new CancellationTokenSource();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> RegisteredSyncPoints =>
        _syncPoints.Keys.ToList().AsReadOnly();

    /// <inheritdoc/>
    public IReadOnlyCollection<string> WaitingSyncPoints =>
        _syncPoints
            .Where(kv => kv.Value.State == SyncPointState.Waiting)
            .Select(kv => kv.Key)
            .ToList()
            .AsReadOnly();

    /// <inheritdoc/>
    public event EventHandler<SyncPointCompletedEventArgs>? SyncPointCompleted;

    /// <inheritdoc/>
    public event EventHandler<SyncPointTimedOutEventArgs>? SyncPointTimedOut;

    /// <inheritdoc/>
    public void RegisterSyncPoint(
        string syncPointId,
        IReadOnlySet<Guid>? requiredParticipants = null,
        TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(syncPointId);

        var tracker = new SyncPointTracker(
            syncPointId,
            requiredParticipants ?? _defaultParticipants,
            timeout ?? _defaultTimeout);

        if (!_syncPoints.TryAdd(syncPointId, tracker))
        {
            // Already registered - update if needed
            _syncPoints[syncPointId] = tracker;
        }

        _logger?.LogDebug(
            "Registered sync point {SyncPointId} with {ParticipantCount} participants, timeout: {Timeout}",
            syncPointId,
            tracker.RequiredParticipants.Count,
            timeout ?? _defaultTimeout);
    }

    /// <inheritdoc/>
    public async Task<SyncPointStatus> ReportReachedAsync(
        string syncPointId,
        Guid entityId,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(syncPointId);

        // Auto-register if not registered
        if (!_syncPoints.TryGetValue(syncPointId, out var tracker))
        {
            RegisterSyncPoint(syncPointId);
            tracker = _syncPoints[syncPointId];
        }

        var wasFirstReach = tracker.ReachedParticipants.Count == 0;
        var added = tracker.ReportReached(entityId);

        if (added)
        {
            _logger?.LogDebug(
                "Entity {EntityId} reached sync point {SyncPointId} ({ReachedCount}/{TotalCount})",
                entityId,
                syncPointId,
                tracker.ReachedParticipants.Count,
                tracker.RequiredParticipants.Count);

            // Start timeout timer on first reach
            if (wasFirstReach && tracker.Timeout.HasValue)
            {
                _ = StartTimeoutTimerAsync(syncPointId, tracker.Timeout.Value, ct);
            }

            // Check if all reached
            if (tracker.IsAllReached)
            {
                tracker.MarkCompleted();
                RaiseSyncPointCompleted(tracker);
            }
        }

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
        return tracker.GetStatus();
    }

    /// <inheritdoc/>
    public async Task<SyncPointStatus> WaitForAllAsync(
        string syncPointId,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(syncPointId);

        if (!_syncPoints.TryGetValue(syncPointId, out var tracker))
        {
            throw new InvalidOperationException($"Sync point '{syncPointId}' not registered");
        }

        // Already complete?
        if (tracker.IsComplete)
        {
            return tracker.GetStatus();
        }

        // Wait for completion signal
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);

        try
        {
            await tracker.CompletionTask.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(SyncPointManager));
        }

        return tracker.GetStatus();
    }

    /// <inheritdoc/>
    public SyncPointStatus? GetStatus(string syncPointId)
    {
        if (string.IsNullOrEmpty(syncPointId))
        {
            return null;
        }

        return _syncPoints.TryGetValue(syncPointId, out var tracker)
            ? tracker.GetStatus()
            : null;
    }

    /// <inheritdoc/>
    public void Reset(string syncPointId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_syncPoints.TryGetValue(syncPointId, out var tracker))
        {
            tracker.Reset();
            _logger?.LogDebug("Reset sync point {SyncPointId}", syncPointId);
        }
    }

    /// <inheritdoc/>
    public void ResetAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var tracker in _syncPoints.Values)
        {
            tracker.Reset();
        }

        _logger?.LogDebug("Reset all sync points");
    }

    private async Task StartTimeoutTimerAsync(
        string syncPointId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            await Task.Delay(timeout, linkedCts.Token);

            if (_syncPoints.TryGetValue(syncPointId, out var tracker) &&
                tracker.State == SyncPointState.Waiting)
            {
                tracker.MarkTimedOut();
                RaiseSyncPointTimedOut(tracker, timeout);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled or disposed
        }
    }

    private void RaiseSyncPointCompleted(SyncPointTracker tracker)
    {
        var duration = tracker.CompletedAt.HasValue && tracker.FirstReachedAt.HasValue
            ? tracker.CompletedAt.Value - tracker.FirstReachedAt.Value
            : TimeSpan.Zero;

        _logger?.LogInformation(
            "Sync point {SyncPointId} completed with {ParticipantCount} participants in {Duration}ms",
            tracker.SyncPointId,
            tracker.ReachedParticipants.Count,
            duration.TotalMilliseconds);

        SyncPointCompleted?.Invoke(this, new SyncPointCompletedEventArgs
        {
            SyncPointId = tracker.SyncPointId,
            Participants = tracker.ReachedParticipants.ToHashSet(),
            Duration = duration
        });
    }

    private void RaiseSyncPointTimedOut(SyncPointTracker tracker, TimeSpan timeout)
    {
        var missing = tracker.RequiredParticipants.Except(tracker.ReachedParticipants).ToHashSet();

        _logger?.LogWarning(
            "Sync point {SyncPointId} timed out after {Timeout}ms, missing {MissingCount} participants",
            tracker.SyncPointId,
            timeout.TotalMilliseconds,
            missing.Count);

        SyncPointTimedOut?.Invoke(this, new SyncPointTimedOutEventArgs
        {
            SyncPointId = tracker.SyncPointId,
            ReachedParticipants = tracker.ReachedParticipants.ToHashSet(),
            MissingParticipants = missing,
            Timeout = timeout
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();

        foreach (var tracker in _syncPoints.Values)
        {
            tracker.Dispose();
        }

        _syncPoints.Clear();
    }
}

/// <summary>
/// Internal tracker for a single sync point.
/// </summary>
internal sealed class SyncPointTracker : IDisposable
{
    private readonly HashSet<Guid> _reachedParticipants;
    private readonly object _lock = new();
    private readonly TaskCompletionSource _completionTcs;
    private bool _disposed;

    public SyncPointTracker(
        string syncPointId,
        IReadOnlySet<Guid> requiredParticipants,
        TimeSpan? timeout)
    {
        SyncPointId = syncPointId;
        RequiredParticipants = requiredParticipants.ToHashSet();
        Timeout = timeout;
        _reachedParticipants = new HashSet<Guid>();
        _completionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        State = SyncPointState.Waiting;
    }

    public string SyncPointId { get; }
    public IReadOnlySet<Guid> RequiredParticipants { get; }
    public TimeSpan? Timeout { get; }
    public SyncPointState State { get; private set; }
    public DateTime? FirstReachedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public bool TimedOut { get; private set; }

    public IReadOnlySet<Guid> ReachedParticipants
    {
        get
        {
            lock (_lock)
            {
                return _reachedParticipants.ToHashSet();
            }
        }
    }

    public bool IsAllReached
    {
        get
        {
            lock (_lock)
            {
                return RequiredParticipants.All(p => _reachedParticipants.Contains(p));
            }
        }
    }

    public bool IsComplete => State != SyncPointState.Waiting;

    public Task CompletionTask => _completionTcs.Task;

    public bool ReportReached(Guid entityId)
    {
        lock (_lock)
        {
            if (State != SyncPointState.Waiting)
            {
                return false;
            }

            if (!RequiredParticipants.Contains(entityId))
            {
                return false;
            }

            if (!_reachedParticipants.Add(entityId))
            {
                return false; // Already reached
            }

            if (FirstReachedAt == null)
            {
                FirstReachedAt = DateTime.UtcNow;
            }

            return true;
        }
    }

    public void MarkCompleted()
    {
        lock (_lock)
        {
            if (State != SyncPointState.Waiting)
            {
                return;
            }

            State = SyncPointState.Completed;
            CompletedAt = DateTime.UtcNow;
            _completionTcs.TrySetResult();
        }
    }

    public void MarkTimedOut()
    {
        lock (_lock)
        {
            if (State != SyncPointState.Waiting)
            {
                return;
            }

            State = SyncPointState.TimedOut;
            TimedOut = true;
            CompletedAt = DateTime.UtcNow;
            _completionTcs.TrySetResult();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _reachedParticipants.Clear();
            State = SyncPointState.Waiting;
            FirstReachedAt = null;
            CompletedAt = null;
            TimedOut = false;
            // Note: Can't reset TaskCompletionSource - need new instance for next cycle
        }
    }

    public SyncPointStatus GetStatus()
    {
        lock (_lock)
        {
            return new SyncPointStatus
            {
                SyncPointId = SyncPointId,
                State = State,
                RequiredParticipants = RequiredParticipants,
                ReachedParticipants = _reachedParticipants.ToHashSet(),
                FirstReachedAt = FirstReachedAt,
                CompletedAt = CompletedAt,
                Timeout = Timeout,
                TimedOut = TimedOut
            };
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _completionTcs.TrySetCanceled();
    }
}
