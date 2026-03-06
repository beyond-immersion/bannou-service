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

    #region Counter Operations Tests

    [Fact]
    public async Task IncrementAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.IncrementAsync("counter:test", 5, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(15);

        // Act
        var result = await _sut.IncrementAsync("counter:test", 5);

        // Assert
        Assert.Equal(15, result);
        _innerStoreMock.Verify(x => x.IncrementAsync("counter:test", 5, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncrementAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _sut.IncrementAsync("counter:test");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.counter_increment",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task IncrementAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Counter failed");
        _innerStoreMock
            .Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.IncrementAsync("counter:test"));
        Assert.Same(expectedException, exception);
    }

    [Fact]
    public async Task DecrementAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DecrementAsync("counter:test", 3, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        // Act
        var result = await _sut.DecrementAsync("counter:test", 3);

        // Assert
        Assert.Equal(7, result);
        _innerStoreMock.Verify(x => x.DecrementAsync("counter:test", 3, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecrementAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DecrementAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        await _sut.DecrementAsync("counter:test");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.counter_decrement",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task GetCounterAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.GetCounterAsync("counter:test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(42L);

        // Act
        var result = await _sut.GetCounterAsync("counter:test");

        // Assert
        Assert.Equal(42L, result);
        _innerStoreMock.Verify(x => x.GetCounterAsync("counter:test", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCounterAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.GetCounterAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        // Act
        var result = await _sut.GetCounterAsync("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCounterAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.GetCounterAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Act
        await _sut.GetCounterAsync("counter:test");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.counter_get",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task SetCounterAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SetCounterAsync("counter:test", 100, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.SetCounterAsync("counter:test", 100);

        // Assert
        _innerStoreMock.Verify(x => x.SetCounterAsync("counter:test", 100, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetCounterAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SetCounterAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.SetCounterAsync("counter:test", 50);

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.counter_set",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task DeleteCounterAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteCounterAsync("counter:test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteCounterAsync("counter:test");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.DeleteCounterAsync("counter:test", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCounterAsync_WhenNotFound_ReturnsFalse()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteCounterAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.DeleteCounterAsync("missing");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteCounterAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteCounterAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.DeleteCounterAsync("counter:test");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.counter_delete",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    #endregion

    #region Hash Operations Tests

    [Fact]
    public async Task HashGetAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashGetAsync<string>("hash:test", "field1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("value1");

        // Act
        var result = await _sut.HashGetAsync<string>("hash:test", "field1");

        // Assert
        Assert.Equal("value1", result);
        _innerStoreMock.Verify(x => x.HashGetAsync<string>("hash:test", "field1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HashGetAsync_WhenFieldNotFound_ReturnsNull()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashGetAsync<string>("hash:test", "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _sut.HashGetAsync<string>("hash:test", "missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task HashGetAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashGetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("val");

        // Act
        await _sut.HashGetAsync<string>("hash:test", "field1");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_get",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task HashSetAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashSetAsync("hash:test", "field1", "value1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HashSetAsync("hash:test", "field1", "value1");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.HashSetAsync("hash:test", "field1", "value1", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HashSetAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashSetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.HashSetAsync("hash:test", "field1", "value1");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_set",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task HashSetManyAsync_DelegatesToInnerStore()
    {
        // Arrange
        var fields = new[]
        {
            new KeyValuePair<string, string>("field1", "value1"),
            new KeyValuePair<string, string>("field2", "value2")
        };
        _innerStoreMock
            .Setup(x => x.HashSetManyAsync<string>("hash:test", It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HashSetManyAsync("hash:test", fields);

        // Assert
        _innerStoreMock.Verify(
            x => x.HashSetManyAsync<string>("hash:test", It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HashSetManyAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashSetManyAsync<string>(It.IsAny<string>(), It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HashSetManyAsync("hash:test", new[] { new KeyValuePair<string, string>("f", "v") });

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_set_many",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task HashDeleteAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashDeleteAsync("hash:test", "field1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HashDeleteAsync("hash:test", "field1");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.HashDeleteAsync("hash:test", "field1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HashDeleteAsync_WhenFieldNotFound_ReturnsFalse()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashDeleteAsync("hash:test", "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.HashDeleteAsync("hash:test", "missing");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HashExistsAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashExistsAsync("hash:test", "field1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HashExistsAsync("hash:test", "field1");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.HashExistsAsync("hash:test", "field1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HashExistsAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.HashExistsAsync("hash:test", "field1");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_exists",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task HashIncrementAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashIncrementAsync("hash:test", "counter", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(15);

        // Act
        var result = await _sut.HashIncrementAsync("hash:test", "counter", 5);

        // Assert
        Assert.Equal(15, result);
        _innerStoreMock.Verify(x => x.HashIncrementAsync("hash:test", "counter", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HashIncrementAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashIncrementAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _sut.HashIncrementAsync("hash:test", "counter");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_increment",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task HashGetAllAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new Dictionary<string, string>
        {
            ["field1"] = "value1",
            ["field2"] = "value2"
        };
        _innerStoreMock
            .Setup(x => x.HashGetAllAsync<string>("hash:test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.HashGetAllAsync<string>("hash:test");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["field1"]);
        Assert.Equal("value2", result["field2"]);
        _innerStoreMock.Verify(x => x.HashGetAllAsync<string>("hash:test", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HashGetAllAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashGetAllAsync<string>(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Act
        await _sut.HashGetAllAsync<string>("hash:test");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_get_all",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task HashCountAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashCountAsync("hash:test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        var result = await _sut.HashCountAsync("hash:test");

        // Assert
        Assert.Equal(5, result);
        _innerStoreMock.Verify(x => x.HashCountAsync("hash:test", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HashCountAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.HashCountAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        await _sut.HashCountAsync("hash:test");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_count",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task DeleteHashAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteHashAsync("hash:test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteHashAsync("hash:test");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.DeleteHashAsync("hash:test", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteHashAsync_WhenNotFound_ReturnsFalse()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteHashAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.DeleteHashAsync("missing");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteHashAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteHashAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.DeleteHashAsync("hash:test");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_delete_all",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task RefreshHashTtlAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.RefreshHashTtlAsync("hash:test", 300, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.RefreshHashTtlAsync("hash:test", 300);

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.RefreshHashTtlAsync("hash:test", 300, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshHashTtlAsync_WhenHashNotFound_ReturnsFalse()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.RefreshHashTtlAsync("missing", 300, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.RefreshHashTtlAsync("missing", 300);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshHashTtlAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.RefreshHashTtlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.RefreshHashTtlAsync("hash:test", 60);

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.hash_refresh_ttl",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task HashGetAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Hash get failed");
        _innerStoreMock
            .Setup(x => x.HashGetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HashGetAsync<string>("hash:test", "field1"));
        Assert.Same(expectedException, exception);

        _telemetryMock.Verify(
            x => x.RecordCounter(
                TelemetryComponents.State,
                TelemetryMetrics.StateOperations,
                1,
                It.IsAny<KeyValuePair<string, object?>[]>()),
            Times.Once);
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
        public string Id { get; set; } = "test-id";
        public string Name { get; set; } = "test-name";
    }
}
