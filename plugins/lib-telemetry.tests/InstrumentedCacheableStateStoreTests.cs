using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Telemetry.Instrumentation;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Telemetry.Tests;

/// <summary>
/// Unit tests for InstrumentedCacheableStateStore.
/// Verifies that Set and Sorted Set operations are properly delegated and instrumented.
/// </summary>
public class InstrumentedCacheableStateStoreTests
{
    private readonly Mock<ICacheableStateStore<TestEntity>> _innerStoreMock;
    private readonly Mock<ITelemetryProvider> _telemetryMock;
    private readonly InstrumentedCacheableStateStore<TestEntity> _sut;

    public InstrumentedCacheableStateStoreTests()
    {
        _innerStoreMock = new Mock<ICacheableStateStore<TestEntity>>();
        _telemetryMock = new Mock<ITelemetryProvider>();

        // Setup telemetry to return null activities (simulates no listener)
        _telemetryMock
            .Setup(x => x.StartActivity(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<System.Diagnostics.ActivityKind>(),
                It.IsAny<System.Diagnostics.ActivityContext?>()))
            .Returns((System.Diagnostics.Activity?)null);

        _sut = new InstrumentedCacheableStateStore<TestEntity>(
            _innerStoreMock.Object,
            _telemetryMock.Object,
            "test-cacheable-store",
            "redis");
    }

    #region Set Operations Tests

    [Fact]
    public async Task AddToSetAsync_Single_DelegatesToInnerStore()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Test" };
        _innerStoreMock
            .Setup(x => x.AddToSetAsync("test-set", item, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.AddToSetAsync("test-set", item);

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.AddToSetAsync("test-set", item, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddToSetAsync_Single_RecordsTelemetry()
    {
        // Arrange
        var item = new TestEntity { Id = "1" };
        _innerStoreMock
            .Setup(x => x.AddToSetAsync(It.IsAny<string>(), It.IsAny<TestEntity>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.AddToSetAsync("test-set", item);

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.add_to_set",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);

        _telemetryMock.Verify(
            x => x.RecordCounter(
                TelemetryComponents.State,
                TelemetryMetrics.StateOperations,
                1,
                It.IsAny<KeyValuePair<string, object?>[]>()),
            Times.Once);
    }

    [Fact]
    public async Task AddToSetAsync_Bulk_DelegatesToInnerStore()
    {
        // Arrange
        IEnumerable<TestEntity> items = new[] { new TestEntity { Id = "1" }, new TestEntity { Id = "2" } };
        _innerStoreMock
            .Setup(x => x.AddToSetAsync<TestEntity>("test-set", It.IsAny<IEnumerable<TestEntity>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _sut.AddToSetAsync("test-set", items);

        // Assert
        Assert.Equal(2L, result);
    }

    [Fact]
    public async Task RemoveFromSetAsync_DelegatesToInnerStore()
    {
        // Arrange
        var item = new TestEntity { Id = "1" };
        _innerStoreMock
            .Setup(x => x.RemoveFromSetAsync("test-set", item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.RemoveFromSetAsync("test-set", item);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetSetAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<TestEntity> { new() { Id = "1" }, new() { Id = "2" } };
        _innerStoreMock
            .Setup(x => x.GetSetAsync<TestEntity>("test-set", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.GetSetAsync<TestEntity>("test-set");

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SetContainsAsync_DelegatesToInnerStore()
    {
        // Arrange
        var item = new TestEntity { Id = "1" };
        _innerStoreMock
            .Setup(x => x.SetContainsAsync("test-set", item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SetContainsAsync("test-set", item);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SetCountAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SetCountAsync("test-set", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        var result = await _sut.SetCountAsync("test-set");

        // Assert
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task DeleteSetAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteSetAsync("test-set", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteSetAsync("test-set");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RefreshSetTtlAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.RefreshSetTtlAsync("test-set", 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.RefreshSetTtlAsync("test-set", 60);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Sorted Set Operations Tests

    [Fact]
    public async Task SortedSetAddAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetAddAsync("leaderboard", "player1", 100.0, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.SortedSetAddAsync("leaderboard", "player1", 100.0, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SortedSetAddAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetAddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.sorted_set_add",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task SortedSetAddBatchAsync_DelegatesToInnerStore()
    {
        // Arrange
        var entries = new[] { ("player1", 100.0), ("player2", 200.0) };
        _innerStoreMock
            .Setup(x => x.SortedSetAddBatchAsync("leaderboard", It.IsAny<IEnumerable<(string, double)>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _sut.SortedSetAddBatchAsync("leaderboard", entries);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task SortedSetRemoveAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetRemoveAsync("leaderboard", "player1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SortedSetRemoveAsync("leaderboard", "player1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SortedSetScoreAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetScoreAsync("leaderboard", "player1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(100.5);

        // Act
        var result = await _sut.SortedSetScoreAsync("leaderboard", "player1");

        // Assert
        Assert.Equal(100.5, result);
    }

    [Fact]
    public async Task SortedSetRankAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetRankAsync("leaderboard", "player1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);

        // Act
        var result = await _sut.SortedSetRankAsync("leaderboard", "player1", descending: true);

        // Assert
        Assert.Equal(5L, result);
    }

    [Fact]
    public async Task SortedSetRangeByRankAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<(string, double)> { ("player1", 100.0), ("player2", 90.0) };
        _innerStoreMock
            .Setup(x => x.SortedSetRangeByRankAsync("leaderboard", 0, 9, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.SortedSetRangeByRankAsync("leaderboard", 0, 9, descending: true);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SortedSetRangeByScoreAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<(string, double)> { ("player1", 100.0), ("player2", 150.0) };
        _innerStoreMock
            .Setup(x => x.SortedSetRangeByScoreAsync("leaderboard", 100.0, 200.0, 0, -1, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.SortedSetRangeByScoreAsync("leaderboard", 100.0, 200.0);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SortedSetCountAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetCountAsync("leaderboard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var result = await _sut.SortedSetCountAsync("leaderboard");

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public async Task SortedSetIncrementAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetIncrementAsync("leaderboard", "player1", 10.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(110.0);

        // Act
        var result = await _sut.SortedSetIncrementAsync("leaderboard", "player1", 10.0);

        // Assert
        Assert.Equal(110.0, result);
    }

    [Fact]
    public async Task SortedSetDeleteAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetDeleteAsync("leaderboard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SortedSetDeleteAsync("leaderboard");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task SortedSetAddAsync_WhenInnerThrows_RecordsError()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Test error");
        _innerStoreMock
            .Setup(x => x.SortedSetAddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SortedSetAddAsync("leaderboard", "player1", 100.0));

        Assert.Same(expectedException, exception);

        // Verify metrics still recorded
        _telemetryMock.Verify(
            x => x.RecordCounter(
                TelemetryComponents.State,
                TelemetryMetrics.StateOperations,
                1,
                It.IsAny<KeyValuePair<string, object?>[]>()),
            Times.Once);
    }

    [Fact]
    public async Task AddToSetAsync_WhenInnerThrows_RecordsError()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Test error");
        _innerStoreMock
            .Setup(x => x.AddToSetAsync<TestEntity>(It.IsAny<string>(), It.IsAny<TestEntity>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddToSetAsync("test-set", new TestEntity()));

        Assert.Same(expectedException, exception);
    }

    #endregion

    #region Base IStateStore Operations Still Work

    [Fact]
    public async Task GetAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new TestEntity { Id = "1", Name = "Test" };
        _innerStoreMock
            .Setup(x => x.GetAsync("key1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.GetAsync("key1");

        // Assert
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task SaveAsync_DelegatesToInnerStore()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test" };
        _innerStoreMock
            .Setup(x => x.SaveAsync("key1", entity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var result = await _sut.SaveAsync("key1", entity);

        // Assert
        Assert.Equal("etag-1", result);
    }

    #endregion

    /// <summary>
    /// Test entity for cacheable state store tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
