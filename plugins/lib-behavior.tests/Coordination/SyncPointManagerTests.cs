// =============================================================================
// Sync Point Manager Tests
// Tests for cross-entity synchronization points.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Coordination;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Coordination;

/// <summary>
/// Tests for <see cref="SyncPointManager"/>.
/// </summary>
public sealed class SyncPointManagerTests : IDisposable
{
    private readonly HashSet<Guid> _defaultParticipants;
    private readonly SyncPointManager _manager;

    public SyncPointManagerTests()
    {
        _defaultParticipants = new HashSet<Guid>
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        };
        _manager = new SyncPointManager(_defaultParticipants);
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    // =========================================================================
    // REGISTRATION TESTS
    // =========================================================================

    [Fact]
    public void RegisterSyncPoint_AddsToRegisteredSyncPoints()
    {
        // Act
        _manager.RegisterSyncPoint("sync1");

        // Assert
        Assert.Contains("sync1", _manager.RegisteredSyncPoints);
    }

    [Fact]
    public void RegisterSyncPoint_WithCustomParticipants_UsesProvidedSet()
    {
        // Arrange
        var customParticipants = new HashSet<Guid> { Guid.NewGuid() };

        // Act
        _manager.RegisterSyncPoint("sync1", customParticipants);
        var status = _manager.GetStatus("sync1");

        // Assert
        Assert.NotNull(status);
        Assert.Single(status.RequiredParticipants);
    }

    [Fact]
    public void RegisterSyncPoint_WithTimeout_SetsTimeoutValue()
    {
        // Act
        _manager.RegisterSyncPoint("sync1", timeout: TimeSpan.FromSeconds(5));
        var status = _manager.GetStatus("sync1");

        // Assert
        Assert.NotNull(status);
        Assert.Equal(TimeSpan.FromSeconds(5), status.Timeout);
    }

    // =========================================================================
    // REPORT REACHED TESTS
    // =========================================================================

    [Fact]
    public async Task ReportReachedAsync_FirstParticipant_RecordsReached()
    {
        // Arrange
        var participant = _defaultParticipants.First();
        _manager.RegisterSyncPoint("sync1");

        // Act
        var status = await _manager.ReportReachedAsync("sync1", participant);

        // Assert
        Assert.Contains(participant, status.ReachedParticipants);
        Assert.Equal(SyncPointState.Waiting, status.State);
    }

    [Fact]
    public async Task ReportReachedAsync_AllParticipants_MarksComplete()
    {
        // Arrange
        _manager.RegisterSyncPoint("sync1");

        // Act - report all participants
        SyncPointStatus? lastStatus = null;
        foreach (var participant in _defaultParticipants)
        {
            lastStatus = await _manager.ReportReachedAsync("sync1", participant);
        }

        // Assert
        Assert.NotNull(lastStatus);
        Assert.True(lastStatus.IsComplete);
        Assert.Equal(SyncPointState.Completed, lastStatus.State);
    }

    [Fact]
    public async Task ReportReachedAsync_DuplicateReport_IsIdempotent()
    {
        // Arrange
        var participant = _defaultParticipants.First();
        _manager.RegisterSyncPoint("sync1");

        // Act
        await _manager.ReportReachedAsync("sync1", participant);
        var status = await _manager.ReportReachedAsync("sync1", participant);

        // Assert - should still have only one reached
        Assert.Single(status.ReachedParticipants);
    }

    [Fact]
    public async Task ReportReachedAsync_NonParticipant_IsIgnored()
    {
        // Arrange
        var nonParticipant = Guid.NewGuid();
        _manager.RegisterSyncPoint("sync1");

        // Act
        var status = await _manager.ReportReachedAsync("sync1", nonParticipant);

        // Assert
        Assert.Empty(status.ReachedParticipants);
    }

    [Fact]
    public async Task ReportReachedAsync_UnregisteredSyncPoint_AutoRegisters()
    {
        // Arrange
        var participant = _defaultParticipants.First();

        // Act
        var status = await _manager.ReportReachedAsync("unregistered", participant);

        // Assert
        Assert.Contains("unregistered", _manager.RegisteredSyncPoints);
        Assert.Contains(participant, status.ReachedParticipants);
    }

    // =========================================================================
    // WAIT FOR ALL TESTS
    // =========================================================================

    [Fact]
    public async Task WaitForAllAsync_AlreadyComplete_ReturnsImmediately()
    {
        // Arrange
        _manager.RegisterSyncPoint("sync1");
        foreach (var participant in _defaultParticipants)
        {
            await _manager.ReportReachedAsync("sync1", participant);
        }

        // Act
        var status = await _manager.WaitForAllAsync("sync1");

        // Assert
        Assert.True(status.IsComplete);
    }

    [Fact]
    public async Task WaitForAllAsync_CancellationToken_ThrowsOnCancellation()
    {
        // Arrange
        _manager.RegisterSyncPoint("sync1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _manager.WaitForAllAsync("sync1", cts.Token));
    }

    [Fact]
    public async Task WaitForAllAsync_UnregisteredSyncPoint_ThrowsInvalidOperation()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.WaitForAllAsync("nonexistent"));
    }

    // =========================================================================
    // EVENTS TESTS
    // =========================================================================

    [Fact]
    public async Task SyncPointCompleted_RaisesEventWhenAllReach()
    {
        // Arrange
        _manager.RegisterSyncPoint("sync1");
        SyncPointCompletedEventArgs? receivedArgs = null;
        _manager.SyncPointCompleted += (_, args) => receivedArgs = args;

        // Act
        foreach (var participant in _defaultParticipants)
        {
            await _manager.ReportReachedAsync("sync1", participant);
        }

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal("sync1", receivedArgs.SyncPointId);
        Assert.Equal(_defaultParticipants.Count, receivedArgs.Participants.Count);
    }

    // =========================================================================
    // RESET TESTS
    // =========================================================================

    [Fact]
    public async Task Reset_ClearsReachedParticipants()
    {
        // Arrange
        _manager.RegisterSyncPoint("sync1");
        foreach (var participant in _defaultParticipants)
        {
            await _manager.ReportReachedAsync("sync1", participant);
        }

        // Act
        _manager.Reset("sync1");
        var status = _manager.GetStatus("sync1");

        // Assert
        Assert.NotNull(status);
        Assert.Empty(status.ReachedParticipants);
        Assert.Equal(SyncPointState.Waiting, status.State);
    }

    [Fact]
    public async Task ResetAll_ClearsAllSyncPoints()
    {
        // Arrange
        _manager.RegisterSyncPoint("sync1");
        _manager.RegisterSyncPoint("sync2");
        var participant = _defaultParticipants.First();
        await _manager.ReportReachedAsync("sync1", participant);
        await _manager.ReportReachedAsync("sync2", participant);

        // Act
        _manager.ResetAll();

        // Assert
        Assert.Empty(_manager.GetStatus("sync1")!.ReachedParticipants);
        Assert.Empty(_manager.GetStatus("sync2")!.ReachedParticipants);
    }

    // =========================================================================
    // STATUS TESTS
    // =========================================================================

    [Fact]
    public void GetStatus_NonexistentSyncPoint_ReturnsNull()
    {
        // Act
        var status = _manager.GetStatus("nonexistent");

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public async Task WaitingSyncPoints_ReturnsOnlyWaiting()
    {
        // Arrange
        _manager.RegisterSyncPoint("waiting");
        _manager.RegisterSyncPoint("complete");

        await _manager.ReportReachedAsync("waiting", _defaultParticipants.First());
        foreach (var p in _defaultParticipants)
        {
            await _manager.ReportReachedAsync("complete", p);
        }

        // Act
        var waiting = _manager.WaitingSyncPoints;

        // Assert
        Assert.Single(waiting);
        Assert.Contains("waiting", waiting);
    }

    // =========================================================================
    // PENDING PARTICIPANTS TESTS
    // =========================================================================

    [Fact]
    public async Task PendingParticipants_ReturnsCorrectSet()
    {
        // Arrange
        _manager.RegisterSyncPoint("sync1");
        var first = _defaultParticipants.First();
        await _manager.ReportReachedAsync("sync1", first);

        // Act
        var status = _manager.GetStatus("sync1");

        // Assert
        Assert.NotNull(status);
        Assert.Equal(_defaultParticipants.Count - 1, status.PendingParticipants.Count);
        Assert.DoesNotContain(first, status.PendingParticipants);
    }
}
