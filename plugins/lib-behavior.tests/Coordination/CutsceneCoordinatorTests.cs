// =============================================================================
// Cutscene Coordinator Tests
// Tests for multi-session coordination.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Coordination;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Coordination;

/// <summary>
/// Tests for <see cref="CutsceneCoordinator"/>.
/// </summary>
public sealed class CutsceneCoordinatorTests : IDisposable
{
    private readonly CutsceneCoordinator _coordinator;
    private readonly List<Guid> _participants;

    public CutsceneCoordinatorTests()
    {
        _coordinator = new CutsceneCoordinator();
        _participants = new List<Guid>
        {
            Guid.NewGuid(),
            Guid.NewGuid()
        };
    }

    public void Dispose()
    {
        _coordinator.Dispose();
    }

    // =========================================================================
    // SESSION CREATION TESTS
    // =========================================================================

    [Fact]
    public async Task CreateSessionAsync_CreatesNewSession()
    {
        // Act
        var session = await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Assert
        Assert.NotNull(session);
        Assert.Equal("session1", session.SessionId);
        Assert.Equal("cinematic1", session.CinematicId);
    }

    [Fact]
    public async Task CreateSessionAsync_AddsToActiveSessions()
    {
        // Act
        await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Assert
        Assert.Single(_coordinator.ActiveSessions);
    }

    [Fact]
    public async Task CreateSessionAsync_DuplicateId_ThrowsInvalidOperation()
    {
        // Arrange
        await _coordinator.CreateSessionAsync(
            "duplicate",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _coordinator.CreateSessionAsync(
                "duplicate",
                "cinematic2",
                _participants,
                CutsceneSessionOptions.Default));
    }

    [Fact]
    public async Task CreateSessionAsync_EmptyParticipants_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _coordinator.CreateSessionAsync(
                "session1",
                "cinematic1",
                new List<Guid>(),
                CutsceneSessionOptions.Default));
    }

    [Fact]
    public async Task CreateSessionAsync_NullSessionId_ThrowsArgumentException()
    {
        // Act & Assert - ArgumentNullException inherits from ArgumentException
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _coordinator.CreateSessionAsync(
                null!,
                "cinematic1",
                _participants,
                CutsceneSessionOptions.Default));
    }

    // =========================================================================
    // GET SESSION TESTS
    // =========================================================================

    [Fact]
    public async Task GetSession_ExistingSession_ReturnsSession()
    {
        // Arrange
        await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Act
        var session = _coordinator.GetSession("session1");

        // Assert
        Assert.NotNull(session);
        Assert.Equal("session1", session.SessionId);
    }

    [Fact]
    public void GetSession_NonexistentSession_ReturnsNull()
    {
        // Act
        var session = _coordinator.GetSession("nonexistent");

        // Assert
        Assert.Null(session);
    }

    [Fact]
    public void GetSession_EmptyString_ReturnsNull()
    {
        // Act
        var session = _coordinator.GetSession("");

        // Assert
        Assert.Null(session);
    }

    // =========================================================================
    // END SESSION TESTS
    // =========================================================================

    [Fact]
    public async Task EndSessionAsync_RemovesFromActiveSessions()
    {
        // Arrange
        await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Act
        await _coordinator.EndSessionAsync("session1");

        // Assert
        Assert.Empty(_coordinator.ActiveSessions);
        Assert.Null(_coordinator.GetSession("session1"));
    }

    [Fact]
    public async Task EndSessionAsync_NonexistentSession_DoesNotThrow()
    {
        // Act & Assert - should not throw
        await _coordinator.EndSessionAsync("nonexistent");
    }

    [Fact]
    public async Task EndSessionAsync_CompletesActiveSession()
    {
        // Arrange
        var session = await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Act
        await _coordinator.EndSessionAsync("session1");

        // Assert
        Assert.True(
            session.State == CutsceneSessionState.Completed ||
            session.State == CutsceneSessionState.Aborted);
    }

    // =========================================================================
    // ACTIVE SESSIONS TESTS
    // =========================================================================

    [Fact]
    public async Task ActiveSessions_ExcludesCompletedSessions()
    {
        // Arrange
        var session1 = await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        await _coordinator.CreateSessionAsync(
            "session2",
            "cinematic2",
            _participants,
            CutsceneSessionOptions.Default);

        // Complete session1
        await session1.CompleteAsync();

        // Act
        var active = _coordinator.ActiveSessions;

        // Assert
        Assert.Single(active);
        Assert.Equal("session2", active.First().SessionId);
    }

    [Fact]
    public async Task ActiveSessions_ExcludesAbortedSessions()
    {
        // Arrange
        var session = await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Abort session
        await session.AbortAsync("test");

        // Act
        var active = _coordinator.ActiveSessions;

        // Assert
        Assert.Empty(active);
    }

    // =========================================================================
    // GET SESSIONS FOR ENTITY TESTS
    // =========================================================================

    [Fact]
    public async Task GetSessionsForEntity_ReturnsMatchingSessions()
    {
        // Arrange
        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();

        await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            new List<Guid> { entity1 },
            CutsceneSessionOptions.Default);

        await _coordinator.CreateSessionAsync(
            "session2",
            "cinematic2",
            new List<Guid> { entity1, entity2 },
            CutsceneSessionOptions.Default);

        await _coordinator.CreateSessionAsync(
            "session3",
            "cinematic3",
            new List<Guid> { entity2 },
            CutsceneSessionOptions.Default);

        // Act
        var sessions = _coordinator.GetSessionsForEntity(entity1);

        // Assert
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Contains(entity1, s.Participants));
    }

    // =========================================================================
    // CLEANUP TESTS
    // =========================================================================

    [Fact]
    public async Task CleanupCompletedSessions_RemovesCompletedAndAborted()
    {
        // Arrange
        var session1 = await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        var session2 = await _coordinator.CreateSessionAsync(
            "session2",
            "cinematic2",
            _participants,
            CutsceneSessionOptions.Default);

        await _coordinator.CreateSessionAsync(
            "session3",
            "cinematic3",
            _participants,
            CutsceneSessionOptions.Default);

        await session1.CompleteAsync();
        await session2.AbortAsync("test");

        // Act
        _coordinator.CleanupCompletedSessions();

        // Assert
        Assert.Null(_coordinator.GetSession("session1"));
        Assert.Null(_coordinator.GetSession("session2"));
        Assert.NotNull(_coordinator.GetSession("session3"));
    }

    // =========================================================================
    // DISPOSAL TESTS
    // =========================================================================

    [Fact]
    public async Task Dispose_DisposesAllSessions()
    {
        // Arrange
        await _coordinator.CreateSessionAsync(
            "session1",
            "cinematic1",
            _participants,
            CutsceneSessionOptions.Default);

        // Act
        _coordinator.Dispose();

        // Assert
        Assert.Empty(_coordinator.ActiveSessions);
    }

    [Fact]
    public async Task CreateSessionAsync_AfterDispose_ThrowsObjectDisposed()
    {
        // Arrange
        _coordinator.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _coordinator.CreateSessionAsync(
                "session1",
                "cinematic1",
                _participants,
                CutsceneSessionOptions.Default));
    }
}
