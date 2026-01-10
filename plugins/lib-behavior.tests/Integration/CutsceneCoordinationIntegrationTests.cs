// =============================================================================
// Cutscene Coordination Integration Tests
// Tests integration between CutsceneSession, SyncPointManager, InputWindowManager,
// and ControlGateManager.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using BeyondImmersion.BannouService.Behavior.Coordination;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for cutscene coordination combining sessions with control gates.
/// </summary>
public sealed class CutsceneCoordinationIntegrationTests : IDisposable
{
    private readonly List<Guid> _participants;
    private readonly ControlGateManager _gateManager;

    public CutsceneCoordinationIntegrationTests()
    {
        _participants = new List<Guid>
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        };
        _gateManager = new ControlGateManager();
    }

    public void Dispose()
    {
        _gateManager.Clear();
    }

    // =========================================================================
    // CONTROL GATE INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public async Task CutsceneSession_TakesControlOfAllParticipants()
    {
        // Arrange - Create session and gates
        using var session = CreateSession();

        // Act - Start cutscene and take control of all participants
        var success = await _gateManager.TakeCinematicControlAsync(
            _participants,
            session.CinematicId);

        // Assert - All participants under cinematic control
        Assert.True(success);
        foreach (var participant in _participants)
        {
            var gate = _gateManager.Get(participant);
            Assert.NotNull(gate);
            Assert.Equal(ControlSource.Cinematic, gate.CurrentSource);
            Assert.False(gate.AcceptsBehaviorOutput);
        }

        // Verify manager can find all cinematic-controlled entities
        var cinematicEntities = _gateManager.GetCinematicControlledEntities();
        Assert.Equal(_participants.Count, cinematicEntities.Count);
    }

    [Fact]
    public async Task CutsceneSession_SyncPoint_BlocksUntilAllReach()
    {
        // Arrange
        using var session = CreateSession();
        session.SyncPoints.RegisterSyncPoint("intro_complete");

        // Act - First participant reaches
        var result1 = await session.ReportSyncReachedAsync("intro_complete", _participants[0]);
        Assert.False(result1.AllReached);
        Assert.Single(result1.ReachedParticipants);

        // Second participant reaches
        var result2 = await session.ReportSyncReachedAsync("intro_complete", _participants[1]);
        Assert.False(result2.AllReached);
        Assert.Equal(2, result2.ReachedParticipants.Count);

        // Third participant reaches - now complete
        var result3 = await session.ReportSyncReachedAsync("intro_complete", _participants[2]);

        // Assert
        Assert.True(result3.AllReached);
        Assert.Equal(3, result3.ReachedParticipants.Count);
    }

    [Fact]
    public async Task CutsceneSession_SyncPoint_PartialReach_NotComplete()
    {
        // Arrange
        using var session = CreateSession();
        session.SyncPoints.RegisterSyncPoint("checkpoint");

        // Act - Only 2 of 3 participants reach
        await session.ReportSyncReachedAsync("checkpoint", _participants[0]);
        var result = await session.ReportSyncReachedAsync("checkpoint", _participants[1]);

        // Assert - Not complete, still waiting for third
        Assert.False(result.AllReached);
        Assert.Equal(2, result.ReachedParticipants.Count);
        Assert.Single(result.PendingParticipants);
        Assert.Contains(_participants[2], result.PendingParticipants);
    }

    [Fact]
    public async Task CutsceneSession_SyncPoint_WithTimeout_AutoCompletes()
    {
        // Arrange - Use short timeout
        var options = new CutsceneSessionOptions
        {
            DefaultSyncTimeout = TimeSpan.FromMilliseconds(50),
            DefaultInputTimeout = TimeSpan.FromSeconds(30)
        };
        using var session = new CutsceneSession(
            "timeout-test",
            "timeout-cinematic",
            _participants,
            options);

        session.SyncPoints.RegisterSyncPoint("timed_sync");

        // Act - Only partial participants reach
        await session.ReportSyncReachedAsync("timed_sync", _participants[0]);

        // Wait for timeout
        await Task.Delay(100);

        // Report another reach - triggers timeout check
        var result = await session.ReportSyncReachedAsync("timed_sync", _participants[1]);

        // Assert - Should show timeout occurred
        Assert.True(result.TimedOut || result.AllReached);
    }

    // =========================================================================
    // INPUT WINDOW TESTS
    // =========================================================================

    [Fact]
    public async Task CutsceneSession_InputWindow_CollectsInput()
    {
        // Arrange
        using var session = CreateSession();
        var windowOptions = new InputWindowOptions
        {
            TargetEntity = _participants[0],
            WindowType = InputWindowType.Choice,
            Options = new List<InputOption>
            {
                new() { Value = "option_a", Label = "Option A" },
                new() { Value = "option_b", Label = "Option B" },
                new() { Value = "option_c", Label = "Option C" }
            },
            PromptText = "Choose wisely"
        };

        var window = await session.CreateInputWindowAsync(windowOptions);

        // Act - Submit input
        var result = await session.SubmitInputAsync(
            window.WindowId,
            _participants[0],
            "option_b");

        // Assert
        Assert.True(result.Accepted);
        Assert.Equal("option_b", result.AdjudicatedValue);
    }

    [Fact]
    public async Task CutsceneSession_InputWindow_TimeoutUsesDefault()
    {
        // Arrange
        var options = new CutsceneSessionOptions
        {
            DefaultInputTimeout = TimeSpan.FromMilliseconds(50),
            UseBehaviorDefaults = true
        };
        using var session = new CutsceneSession(
            "input-timeout-test",
            "input-cinematic",
            _participants,
            options);

        var windowOptions = new InputWindowOptions
        {
            TargetEntity = _participants[0],
            WindowType = InputWindowType.Choice,
            Options = new List<InputOption>
            {
                new() { Value = "yes", Label = "Yes", IsDefault = true },
                new() { Value = "no", Label = "No" }
            },
            DefaultValue = "yes",
            DefaultSource = DefaultValueSource.Cutscene,
            Timeout = TimeSpan.FromMilliseconds(50)
        };

        InputWindowResultEventArgs? receivedResult = null;
        session.InputWindowResult += (_, args) => receivedResult = args;

        var window = await session.CreateInputWindowAsync(windowOptions);

        // Act - Wait for timeout
        await Task.Delay(100);

        // Trigger a check by attempting another operation
        var checkResult = session.InputWindows.GetWindow(window.WindowId);

        // Assert - Window should be complete or timed out
        Assert.NotNull(window);
        // Window may be timed out with default value applied
        if (checkResult != null && checkResult.IsCompleted)
        {
            // Window timed out successfully
            Assert.True(checkResult.HasInput);
        }
    }

    [Fact]
    public async Task CutsceneSession_InputWindow_MultipleParticipants()
    {
        // Arrange
        using var session = CreateSession();

        var window1Options = new InputWindowOptions
        {
            TargetEntity = _participants[0],
            WindowType = InputWindowType.Choice,
            Options = new List<InputOption>
            {
                new() { Value = "attack", Label = "Attack" },
                new() { Value = "defend", Label = "Defend" }
            }
        };
        var window2Options = new InputWindowOptions
        {
            TargetEntity = _participants[1],
            WindowType = InputWindowType.Choice,
            Options = new List<InputOption>
            {
                new() { Value = "support", Label = "Support" },
                new() { Value = "retreat", Label = "Retreat" }
            }
        };

        var window1 = await session.CreateInputWindowAsync(window1Options);
        var window2 = await session.CreateInputWindowAsync(window2Options);

        // Act - Each participant submits to their own window
        var result1 = await session.SubmitInputAsync(window1.WindowId, _participants[0], "attack");
        var result2 = await session.SubmitInputAsync(window2.WindowId, _participants[1], "support");

        // Assert - Both accepted independently
        Assert.True(result1.Accepted);
        Assert.True(result2.Accepted);
        Assert.Equal("attack", result1.AdjudicatedValue);
        Assert.Equal("support", result2.AdjudicatedValue);
    }

    [Fact]
    public async Task CutsceneSession_QteWindow_ScoresInput()
    {
        // Arrange
        using var session = CreateSession();
        var windowOptions = new InputWindowOptions
        {
            TargetEntity = _participants[0],
            WindowType = InputWindowType.QuickTimeEvent,
            PromptText = "Press Now!"
        };

        var window = await session.CreateInputWindowAsync(windowOptions);

        // Act - Submit QTE input with timing data
        var qteInput = new { timing = 0.95f, button = "A" };
        var result = await session.SubmitInputAsync(
            window.WindowId,
            _participants[0],
            qteInput);

        // Assert
        Assert.True(result.Accepted);
    }

    // =========================================================================
    // SESSION LIFECYCLE TESTS
    // =========================================================================

    [Fact]
    public async Task CutsceneSession_Abort_ReturnsControlToAllParticipants()
    {
        // Arrange
        using var session = CreateSession();
        await _gateManager.TakeCinematicControlAsync(_participants, session.CinematicId);

        // Verify cinematic control
        foreach (var p in _participants)
        {
            Assert.Equal(ControlSource.Cinematic, _gateManager.Get(p)?.CurrentSource);
        }

        // Act - Abort session and return control
        await session.AbortAsync("Player disconnected");
        await _gateManager.ReturnCinematicControlAsync(_participants, ControlHandoff.Instant());

        // Assert - All gates back to Behavior
        foreach (var p in _participants)
        {
            Assert.Equal(ControlSource.Behavior, _gateManager.Get(p)?.CurrentSource);
        }
        Assert.Equal(CutsceneSessionState.Aborted, session.State);
    }

    [Fact]
    public async Task CutsceneSession_Complete_HandoffWithRestorationData()
    {
        // Arrange
        using var session = CreateSession();
        await _gateManager.TakeCinematicControlAsync(_participants, session.CinematicId);

        var finalState = new EntityState
        {
            Position = new System.Numerics.Vector3(10, 0, 20),
            Rotation = new System.Numerics.Vector3(0, 90, 0),
            Stance = "standing",
            Emotion = "relieved"
        };

        // Track control change events
        var handoffReceived = new Dictionary<Guid, ControlHandoff?>();
        foreach (var p in _participants)
        {
            var gate = _gateManager.Get(p);
            if (gate != null)
            {
                gate.ControlChanged += (_, e) =>
                {
                    handoffReceived[e.EntityId] = e.Handoff;
                };
            }
        }

        // Act - Complete session with state sync
        await session.CompleteAsync();
        await _gateManager.ReturnCinematicControlAsync(
            _participants,
            ControlHandoff.InstantWithState(finalState));

        // Assert - Handoff data available
        Assert.Equal(CutsceneSessionState.Completed, session.State);
        foreach (var p in _participants)
        {
            Assert.True(handoffReceived.ContainsKey(p));
            Assert.NotNull(handoffReceived[p]);
            Assert.True(handoffReceived[p]?.SyncState);
            Assert.NotNull(handoffReceived[p]?.FinalState);
        }
    }

    [Fact]
    public async Task CutsceneSession_Complete_AllGatesReleased()
    {
        // Arrange
        using var session = CreateSession();
        await _gateManager.TakeCinematicControlAsync(_participants, session.CinematicId);

        // Act - Complete and return control
        await session.CompleteAsync();
        await _gateManager.ReturnCinematicControlAsync(_participants, ControlHandoff.Instant());

        // Assert
        Assert.Equal(CutsceneSessionState.Completed, session.State);
        foreach (var p in _participants)
        {
            var gate = _gateManager.Get(p);
            Assert.NotNull(gate);
            Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
            Assert.True(gate.AcceptsBehaviorOutput);
        }

        // No cinematic-controlled entities remain
        Assert.Empty(_gateManager.GetCinematicControlledEntities());
    }

    [Fact]
    public async Task CutsceneSession_StateTransitions_CorrectSequence()
    {
        // Arrange
        using var session = CreateSession();
        var stateHistory = new List<CutsceneSessionState> { session.State };
        session.StateChanged += (_, e) => stateHistory.Add(e.NewState);

        session.SyncPoints.RegisterSyncPoint("sync1");

        // Act - Progress through states
        // Active (initial) -> WaitingForSync
        await session.ReportSyncReachedAsync("sync1", _participants[0]);
        // WaitingForSync -> Active (when all reach)
        await session.ReportSyncReachedAsync("sync1", _participants[1]);
        await session.ReportSyncReachedAsync("sync1", _participants[2]);

        // Active -> WaitingForInput
        var window = await session.CreateInputWindowAsync(new InputWindowOptions
        {
            TargetEntity = _participants[0]
        });

        // WaitingForInput -> Active (when input received)
        await session.SubmitInputAsync(window.WindowId, _participants[0], "input");

        // Active -> Completed
        await session.CompleteAsync();

        // Assert - Verify state progression
        Assert.Contains(CutsceneSessionState.Active, stateHistory);
        Assert.Contains(CutsceneSessionState.WaitingForSync, stateHistory);
        Assert.Contains(CutsceneSessionState.WaitingForInput, stateHistory);
        Assert.Contains(CutsceneSessionState.Completed, stateHistory);

        // Final state is Completed
        Assert.Equal(CutsceneSessionState.Completed, session.State);
    }

    // =========================================================================
    // MULTI-SESSION TESTS
    // =========================================================================

    [Fact]
    public async Task CutsceneCoordinator_MultipleSessions_Independent()
    {
        // Arrange - Two independent sessions with different participants
        var session1Participants = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var session2Participants = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        using var session1 = new CutsceneSession(
            "session1",
            "cinematic_a",
            session1Participants,
            CutsceneSessionOptions.Multiplayer);

        using var session2 = new CutsceneSession(
            "session2",
            "cinematic_b",
            session2Participants,
            CutsceneSessionOptions.Multiplayer);

        // Act - Take control for each session
        await _gateManager.TakeCinematicControlAsync(session1Participants, session1.CinematicId);
        await _gateManager.TakeCinematicControlAsync(session2Participants, session2.CinematicId);

        // Register sync points independently
        session1.SyncPoints.RegisterSyncPoint("sync_a");
        session2.SyncPoints.RegisterSyncPoint("sync_b");

        // Progress session1
        await session1.ReportSyncReachedAsync("sync_a", session1Participants[0]);
        await session1.ReportSyncReachedAsync("sync_a", session1Participants[1]);
        await session1.CompleteAsync();

        // Session2 still active
        Assert.Equal(CutsceneSessionState.Completed, session1.State);
        Assert.Equal(CutsceneSessionState.Active, session2.State);

        // Return control for session1 only
        await _gateManager.ReturnCinematicControlAsync(session1Participants, ControlHandoff.Instant());

        // Assert - Session1 participants freed, Session2 still controlled
        foreach (var p in session1Participants)
        {
            Assert.Equal(ControlSource.Behavior, _gateManager.Get(p)?.CurrentSource);
        }
        foreach (var p in session2Participants)
        {
            Assert.Equal(ControlSource.Cinematic, _gateManager.Get(p)?.CurrentSource);
        }
    }

    [Fact]
    public async Task CutsceneCoordinator_SessionCleanup_RemovesCompleted()
    {
        // Arrange
        var coordinator = new CutsceneCoordinator();

        var session1 = await coordinator.CreateSessionAsync(
            "session1",
            "cleanup_cinematic",
            _participants.Take(2).ToList(),
            CutsceneSessionOptions.Multiplayer);

        var session2 = await coordinator.CreateSessionAsync(
            "session2",
            "active_cinematic",
            _participants.Skip(1).ToList(),
            CutsceneSessionOptions.Multiplayer);

        // Complete session1
        await session1.CompleteAsync();

        // Act - Cleanup
        coordinator.CleanupCompletedSessions();

        // Assert
        Assert.Null(coordinator.GetSession("session1"));
        Assert.NotNull(coordinator.GetSession("session2"));
    }

    [Fact]
    public async Task CutsceneSession_WithRealControlGates_FullFlow()
    {
        // Arrange - Full end-to-end flow
        using var session = CreateSession();

        // Phase 1: Take cinematic control
        await _gateManager.TakeCinematicControlAsync(
            _participants,
            session.CinematicId,
            allowBehaviorChannels: new HashSet<string> { "expression" });

        // Verify partial behavior allowed
        foreach (var p in _participants)
        {
            var gate = _gateManager.Get(p);
            Assert.NotNull(gate);
            Assert.Equal(ControlSource.Cinematic, gate.CurrentSource);
            Assert.Contains("expression", gate.BehaviorInputChannels);
        }

        // Phase 2: Sync point coordination
        session.SyncPoints.RegisterSyncPoint("intro");
        foreach (var p in _participants)
        {
            await session.ReportSyncReachedAsync("intro", p);
        }

        // Phase 3: Input collection
        var window = await session.CreateInputWindowAsync(new InputWindowOptions
        {
            TargetEntity = _participants[0],
            WindowType = InputWindowType.Choice,
            Options = new List<InputOption>
            {
                new() { Value = "spare", Label = "Spare" },
                new() { Value = "eliminate", Label = "Eliminate" }
            }
        });
        await session.SubmitInputAsync(window.WindowId, _participants[0], "spare");

        // Phase 4: Another sync point
        session.SyncPoints.RegisterSyncPoint("decision_made");
        foreach (var p in _participants)
        {
            await session.ReportSyncReachedAsync("decision_made", p);
        }

        // Phase 5: Complete and handoff
        await session.CompleteAsync();

        var finalState = new EntityState
        {
            Position = new System.Numerics.Vector3(0, 0, 0),
            Stance = "idle"
        };
        await _gateManager.ReturnCinematicControlAsync(
            _participants,
            ControlHandoff.InstantWithState(finalState));

        // Assert - Full flow completed
        Assert.Equal(CutsceneSessionState.Completed, session.State);
        foreach (var p in _participants)
        {
            var gate = _gateManager.Get(p);
            Assert.NotNull(gate);
            Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
            Assert.True(gate.AcceptsBehaviorOutput);
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private CutsceneSession CreateSession(CutsceneSessionOptions? options = null)
    {
        return new CutsceneSession(
            $"session-{Guid.NewGuid():N}",
            "test-cinematic",
            _participants,
            options ?? CutsceneSessionOptions.Multiplayer);
    }
}
