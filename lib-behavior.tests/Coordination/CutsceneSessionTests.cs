// =============================================================================
// Cutscene Session Tests
// Tests for multi-participant cutscene sessions.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Coordination;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Coordination;

/// <summary>
/// Tests for <see cref="CutsceneSession"/>.
/// </summary>
public sealed class CutsceneSessionTests : IDisposable
{
    private readonly List<Guid> _participants;
    private readonly CutsceneSession _session;

    public CutsceneSessionTests()
    {
        _participants = new List<Guid>
        {
            Guid.NewGuid(),
            Guid.NewGuid()
        };
        _session = new CutsceneSession(
            "test-session",
            "test-cinematic",
            _participants,
            CutsceneSessionOptions.Default);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    // =========================================================================
    // CONSTRUCTION TESTS
    // =========================================================================

    [Fact]
    public void Constructor_SetsProperties()
    {
        // Assert
        Assert.Equal("test-session", _session.SessionId);
        Assert.Equal("test-cinematic", _session.CinematicId);
        Assert.Equal(_participants.Count, _session.Participants.Count);
        Assert.Equal(CutsceneSessionState.Active, _session.State);
    }

    [Fact]
    public void Constructor_CreatesSyncPointManager()
    {
        // Assert
        Assert.NotNull(_session.SyncPoints);
    }

    [Fact]
    public void Constructor_CreatesInputWindowManager()
    {
        // Assert
        Assert.NotNull(_session.InputWindows);
    }

    // =========================================================================
    // SYNC POINT TESTS
    // =========================================================================

    [Fact]
    public async Task ReportSyncReachedAsync_SingleParticipant_NotComplete()
    {
        // Arrange
        _session.SyncPoints.RegisterSyncPoint("sync1");

        // Act
        var result = await _session.ReportSyncReachedAsync("sync1", _participants[0]);

        // Assert
        Assert.False(result.AllReached);
        Assert.Contains(_participants[0], result.ReachedParticipants);
    }

    [Fact]
    public async Task ReportSyncReachedAsync_AllParticipants_Complete()
    {
        // Arrange
        _session.SyncPoints.RegisterSyncPoint("sync1");

        // Act
        SyncPointResult? lastResult = null;
        foreach (var participant in _participants)
        {
            lastResult = await _session.ReportSyncReachedAsync("sync1", participant);
        }

        // Assert
        Assert.NotNull(lastResult);
        Assert.True(lastResult.AllReached);
    }

    [Fact]
    public async Task ReportSyncReachedAsync_RaisesSyncPointReachedEvent()
    {
        // Arrange
        _session.SyncPoints.RegisterSyncPoint("sync1");
        SyncPointReachedEventArgs? receivedArgs = null;
        _session.SyncPointReached += (_, args) => receivedArgs = args;

        // Act
        foreach (var participant in _participants)
        {
            await _session.ReportSyncReachedAsync("sync1", participant);
        }

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal("sync1", receivedArgs.SyncPointId);
    }

    // =========================================================================
    // INPUT WINDOW TESTS
    // =========================================================================

    [Fact]
    public async Task CreateInputWindowAsync_CreatesWindow()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _participants[0]
        };

        // Act
        var window = await _session.CreateInputWindowAsync(options);

        // Assert
        Assert.NotNull(window);
        Assert.Equal(_participants[0], window.TargetEntity);
    }

    [Fact]
    public async Task SubmitInputAsync_AcceptsValidInput()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _participants[0]
        };
        var window = await _session.CreateInputWindowAsync(options);

        // Act
        var result = await _session.SubmitInputAsync(
            window.WindowId,
            _participants[0],
            "test-input");

        // Assert
        Assert.True(result.Accepted);
    }

    [Fact]
    public async Task SubmitInputAsync_RaisesInputWindowResultEvent()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _participants[0]
        };
        var window = await _session.CreateInputWindowAsync(options);

        InputWindowResultEventArgs? receivedArgs = null;
        _session.InputWindowResult += (_, args) => receivedArgs = args;

        // Act
        await _session.SubmitInputAsync(window.WindowId, _participants[0], "test-input");

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal(window.WindowId, receivedArgs.WindowId);
    }

    // =========================================================================
    // STATE TRANSITION TESTS
    // =========================================================================

    [Fact]
    public async Task CreateInputWindowAsync_TransitionsToWaitingForInput()
    {
        // Arrange
        SessionStateChangedEventArgs? stateChange = null;
        _session.StateChanged += (_, args) => stateChange = args;

        // Act
        await _session.CreateInputWindowAsync(new InputWindowOptions
        {
            TargetEntity = _participants[0]
        });

        // Assert
        Assert.NotNull(stateChange);
        Assert.Equal(CutsceneSessionState.WaitingForInput, stateChange.NewState);
    }

    [Fact]
    public async Task CompleteAsync_TransitionsToCompleted()
    {
        // Arrange
        SessionStateChangedEventArgs? stateChange = null;
        _session.StateChanged += (_, args) => stateChange = args;

        // Act
        await _session.CompleteAsync();

        // Assert
        Assert.NotNull(stateChange);
        Assert.Equal(CutsceneSessionState.Completed, stateChange.NewState);
    }

    [Fact]
    public async Task AbortAsync_TransitionsToAborted()
    {
        // Arrange
        SessionStateChangedEventArgs? stateChange = null;
        _session.StateChanged += (_, args) => stateChange = args;

        // Act
        await _session.AbortAsync("test reason");

        // Assert
        Assert.NotNull(stateChange);
        Assert.Equal(CutsceneSessionState.Aborted, stateChange.NewState);
    }

    [Fact]
    public async Task CompleteAsync_WhenAlreadyCompleted_IsIdempotent()
    {
        // Arrange
        await _session.CompleteAsync();
        var stateChangeCount = 0;
        _session.StateChanged += (_, _) => stateChangeCount++;

        // Act
        await _session.CompleteAsync();

        // Assert
        Assert.Equal(0, stateChangeCount); // No additional state change
    }

    // =========================================================================
    // OPTIONS TESTS
    // =========================================================================

    [Fact]
    public void SinglePlayerOptions_HasNoSyncTimeout()
    {
        // Arrange
        var options = CutsceneSessionOptions.SinglePlayer;

        // Assert
        Assert.Null(options.DefaultSyncTimeout);
        Assert.Equal(SkippableMode.Easily, options.Skippable);
    }

    [Fact]
    public void MultiplayerOptions_HasSyncTimeout()
    {
        // Arrange
        var options = CutsceneSessionOptions.Multiplayer;

        // Assert
        Assert.NotNull(options.DefaultSyncTimeout);
        Assert.Equal(SkippableMode.NotSkippable, options.Skippable);
    }
}
