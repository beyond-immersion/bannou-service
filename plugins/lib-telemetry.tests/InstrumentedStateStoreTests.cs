using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Telemetry.Instrumentation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Telemetry.Tests;

/// <summary>
/// Unit tests for InstrumentedStateStore.
/// Verifies that operations are properly delegated and instrumented.
/// </summary>
public class InstrumentedStateStoreTests
{
    private readonly Mock<IStateStore<TestEntity>> _innerStoreMock;
    private readonly Mock<ITelemetryProvider> _telemetryMock;
    private readonly InstrumentedStateStore<TestEntity> _sut;

    public InstrumentedStateStoreTests()
    {
        _innerStoreMock = new Mock<IStateStore<TestEntity>>();
        _telemetryMock = new Mock<ITelemetryProvider>();

        // Setup telemetry to return null activities (simulates no listener)
        _telemetryMock
            .Setup(x => x.StartActivity(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<System.Diagnostics.ActivityKind>(),
                It.IsAny<System.Diagnostics.ActivityContext?>()))
            .Returns((System.Diagnostics.Activity?)null);

        _sut = new InstrumentedStateStore<TestEntity>(
            _innerStoreMock.Object,
            _telemetryMock.Object,
            "test-store",
            "redis");
    }

    #region GetAsync

    [Fact]
    public async Task GetAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new TestEntity { Id = "test-1", Name = "Test" };
        _innerStoreMock
            .Setup(x => x.GetAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.GetAsync("test-key");

        // Assert
        Assert.Same(expected, result);
        _innerStoreMock.Verify(x => x.GetAsync("test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestEntity());

        // Act
        await _sut.GetAsync("test-key");

        // Assert - verify telemetry was recorded
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.get",
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

        _telemetryMock.Verify(
            x => x.RecordHistogram(
                TelemetryComponents.State,
                TelemetryMetrics.StateDuration,
                It.IsAny<double>(),
                It.IsAny<KeyValuePair<string, object?>[]>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenInnerThrows_RecordsError()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Test error");
        _innerStoreMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetAsync("test-key"));

        Assert.Same(expectedException, exception);

        // Verify metrics still recorded (with success=false in tags)
        _telemetryMock.Verify(
            x => x.RecordCounter(
                TelemetryComponents.State,
                TelemetryMetrics.StateOperations,
                1,
                It.IsAny<KeyValuePair<string, object?>[]>()),
            Times.Once);
    }

    #endregion

    #region SaveAsync

    [Fact]
    public async Task SaveAsync_DelegatesToInnerStore()
    {
        // Arrange
        var entity = new TestEntity { Id = "test-1", Name = "Test" };
        var expectedEtag = "etag-123";
        _innerStoreMock
            .Setup(x => x.SaveAsync("test-key", entity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEtag);

        // Act
        var result = await _sut.SaveAsync("test-key", entity);

        // Assert
        Assert.Equal(expectedEtag, result);
        _innerStoreMock.Verify(
            x => x.SaveAsync("test-key", entity, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveAsync_RecordsTelemetry()
    {
        // Arrange
        var entity = new TestEntity { Id = "test-1" };
        _innerStoreMock
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<TestEntity>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await _sut.SaveAsync("test-key", entity);

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.save",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteAsync("test-key");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.DeleteAsync("test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.ExistsAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.ExistsAsync("test-key");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(x => x.ExistsAsync("test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetBulkAsync

    [Fact]
    public async Task GetBulkAsync_DelegatesToInnerStore()
    {
        // Arrange
        var keys = new[] { "key1", "key2" };
        var expected = new Dictionary<string, TestEntity>
        {
            ["key1"] = new TestEntity { Id = "1" },
            ["key2"] = new TestEntity { Id = "2" }
        };
        _innerStoreMock
            .Setup(x => x.GetBulkAsync(keys, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.GetBulkAsync(keys);

        // Assert
        Assert.Equal(2, result.Count);
        _innerStoreMock.Verify(x => x.GetBulkAsync(keys, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Set Operations

    [Fact]
    public async Task AddToSetAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.AddToSetAsync<string>("set-key", "item", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.AddToSetAsync("set-key", "item");

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(
            x => x.AddToSetAsync<string>("set-key", "item", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSetAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<string> { "a", "b", "c" };
        _innerStoreMock
            .Setup(x => x.GetSetAsync<string>("set-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.GetSetAsync<string>("set-key");

        // Assert
        Assert.Equal(3, result.Count);
    }

    #endregion

    #region Sorted Set Operations

    [Fact]
    public async Task SortedSetAddAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetAddAsync("zset", "member", 1.0, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SortedSetAddAsync("zset", "member", 1.0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SortedSetRankAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SortedSetRankAsync("zset", "member", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);

        // Act
        var result = await _sut.SortedSetRankAsync("zset", "member", true);

        // Assert
        Assert.Equal(5L, result);
    }

    #endregion

    /// <summary>
    /// Test entity for state store tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
