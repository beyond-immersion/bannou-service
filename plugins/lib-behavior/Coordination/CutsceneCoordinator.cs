// =============================================================================
// Cutscene Coordinator
// Server-side coordination for multi-participant cutscenes.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Runtime;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Coordination;

/// <summary>
/// Default implementation of cutscene coordination.
/// </summary>
/// <remarks>
/// <para>
/// The CutsceneCoordinator manages the server-side master timeline for
/// cutscenes involving multiple participants. It:
/// </para>
/// <list type="bullet">
/// <item>Creates and tracks cutscene sessions</item>
/// <item>Provides session lookup by ID</item>
/// <item>Handles session cleanup</item>
/// <item>Integrates with behavior stack for QTE defaults</item>
/// </list>
/// </remarks>
public sealed class CutsceneCoordinator : ICutsceneCoordinator, IDisposable
{
    private readonly ConcurrentDictionary<string, CutsceneSession> _sessions;
    private readonly Func<Guid, object?>? _behaviorDefaultResolver;
    private readonly ILogger<CutsceneCoordinator>? _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ITelemetryProvider? _telemetryProvider;
    private bool _disposed;

    /// <summary>
    /// Creates a new cutscene coordinator.
    /// </summary>
    /// <param name="behaviorDefaultResolver">Optional resolver for behavior defaults.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for span instrumentation.</param>
    public CutsceneCoordinator(
        Func<Guid, object?>? behaviorDefaultResolver = null,
        ILoggerFactory? loggerFactory = null,
        ITelemetryProvider? telemetryProvider = null)
    {
        _behaviorDefaultResolver = behaviorDefaultResolver;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<CutsceneCoordinator>();
        _telemetryProvider = telemetryProvider;
        _sessions = new ConcurrentDictionary<string, CutsceneSession>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ICutsceneSession> ActiveSessions =>
        _sessions.Values
            .Where(s => s.State != CutsceneSessionState.Completed && s.State != CutsceneSessionState.Aborted)
            .Cast<ICutsceneSession>()
            .ToList()
            .AsReadOnly();

    /// <inheritdoc/>
    public async Task<ICutsceneSession> CreateSessionAsync(
        string sessionId,
        string cinematicId,
        IReadOnlyList<Guid> participants,
        CutsceneSessionOptions options,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "CutsceneCoordinator.CreateSessionAsync");
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(cinematicId);

        if (participants.Count == 0)
        {
            throw new ArgumentException("At least one participant is required", nameof(participants));
        }

        var session = new CutsceneSession(
            sessionId,
            cinematicId,
            participants,
            options,
            _behaviorDefaultResolver,
            _loggerFactory?.CreateLogger<CutsceneSession>(),
            _telemetryProvider);

        if (!_sessions.TryAdd(sessionId, session))
        {
            session.Dispose();
            throw new InvalidOperationException($"Session '{sessionId}' already exists");
        }

        _logger?.LogInformation(
            "Created cutscene session {SessionId} for cinematic {CinematicId} with {ParticipantCount} participants",
            sessionId,
            cinematicId,
            participants.Count);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
        return session;
    }

    /// <inheritdoc/>
    public ICutsceneSession? GetSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    /// <inheritdoc/>
    public async Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "CutsceneCoordinator.EndSessionAsync");
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessions.TryRemove(sessionId, out var session))
        {
            return;
        }

        try
        {
            // Complete if still active
            if (session.State != CutsceneSessionState.Completed &&
                session.State != CutsceneSessionState.Aborted)
            {
                await session.CompleteAsync(ct);
            }

            _logger?.LogInformation(
                "Ended cutscene session {SessionId}, duration: {Duration}ms",
                sessionId,
                (DateTime.UtcNow - session.StartedAt).TotalMilliseconds);
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Gets sessions that a specific entity is participating in.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>Sessions containing this participant.</returns>
    public IReadOnlyCollection<ICutsceneSession> GetSessionsForEntity(Guid entityId)
    {
        return _sessions.Values
            .Where(s => s.Participants.Contains(entityId))
            .Cast<ICutsceneSession>()
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Cleans up completed or aborted sessions.
    /// </summary>
    public void CleanupCompletedSessions()
    {
        var sessionsToRemove = _sessions
            .Where(kv => kv.Value.State == CutsceneSessionState.Completed ||
                        kv.Value.State == CutsceneSessionState.Aborted)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var sessionId in sessionsToRemove)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Dispose();
            }
        }

        if (sessionsToRemove.Count > 0)
        {
            _logger?.LogDebug("Cleaned up {Count} completed/aborted sessions", sessionsToRemove.Count);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }
}

/// <summary>
/// Extension methods for integrating CutsceneCoordinator with CinematicRunner.
/// </summary>
public static class CutsceneCoordinatorExtensions
{
    /// <summary>
    /// Creates a coordinated session that integrates with a CinematicRunner.
    /// </summary>
    /// <param name="coordinator">The coordinator.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="controller">The cinematic controller managing execution.</param>
    /// <param name="options">Session options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The coordinated session.</returns>
    public static async Task<ICutsceneSession> CreateCoordinatedSessionAsync(
        this ICutsceneCoordinator coordinator,
        string sessionId,
        CinematicRunner controller,
        CutsceneSessionOptions? options = null,
        CancellationToken ct = default)
    {

        var effectiveOptions = options ?? CutsceneSessionOptions.Default;

        var session = await coordinator.CreateSessionAsync(
            sessionId,
            controller.CinematicId,
            controller.ControlledEntities.ToList(),
            effectiveOptions,
            ct);

        // Wire up controller completion to session completion
        controller.CinematicCompleted += async (_, e) =>
        {
            if (!e.WasAborted)
            {
                await session.CompleteAsync(ct);
            }
            else
            {
                await session.AbortAsync("Cinematic aborted", ct);
            }
        };

        return session;
    }
}
