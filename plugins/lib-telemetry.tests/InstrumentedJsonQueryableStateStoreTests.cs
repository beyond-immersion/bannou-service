using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Telemetry.Instrumentation;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Telemetry.Tests;

/// <summary>
/// Unit tests for InstrumentedJsonQueryableStateStore.
/// Verifies that JSON query operations are properly delegated and instrumented.
/// </summary>
public class InstrumentedJsonQueryableStateStoreTests
{
    private readonly Mock<IJsonQueryableStateStore<TestEntity>> _innerStoreMock;
    private readonly Mock<ITelemetryProvider> _telemetryMock;
    private readonly InstrumentedJsonQueryableStateStore<TestEntity> _sut;

    public InstrumentedJsonQueryableStateStoreTests()
    {
        _innerStoreMock = new Mock<IJsonQueryableStateStore<TestEntity>>();
        _telemetryMock = new Mock<ITelemetryProvider>();

        _telemetryMock
            .Setup(x => x.StartActivity(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<System.Diagnostics.ActivityKind>(),
                It.IsAny<System.Diagnostics.ActivityContext?>()))
            .Returns((System.Diagnostics.Activity?)null);

        _sut = new InstrumentedJsonQueryableStateStore<TestEntity>(
            _innerStoreMock.Object,
            _telemetryMock.Object,
            "test-json-store",
            "mysql");
    }

    #region JsonQueryAsync Tests

    /// <summary>
    /// Verifies that JsonQueryAsync delegates to the inner store and returns its result.
    /// </summary>
    [Fact]
    public async Task JsonQueryAsync_DelegatesToInnerStore()
    {
        // Arrange
        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.Name", Operator = QueryOperator.Equals, Value = "test" }
        };
        var expected = new List<JsonQueryResult<TestEntity>>
        {
            new("key1", new TestEntity { Id = "1", Name = "test" })
        };
        _innerStoreMock
            .Setup(x => x.JsonQueryAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.JsonQueryAsync(conditions);

        // Assert
        Assert.Single(result);
        Assert.Equal("key1", result[0].Key);
        _innerStoreMock.Verify(
            x => x.JsonQueryAsync(conditions, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that JsonQueryAsync records telemetry activity and metrics.
    /// </summary>
    [Fact]
    public async Task JsonQueryAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.JsonQueryAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JsonQueryResult<TestEntity>>());

        // Act
        await _sut.JsonQueryAsync(new List<QueryCondition>());

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.json_query",
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

    /// <summary>
    /// Verifies that JsonQueryAsync propagates exceptions and records failure metrics.
    /// </summary>
    [Fact]
    public async Task JsonQueryAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("JSON query failed");
        _innerStoreMock
            .Setup(x => x.JsonQueryAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.JsonQueryAsync(new List<QueryCondition>()));
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

    #region JsonQueryPagedAsync Tests

    /// <summary>
    /// Verifies that JsonQueryPagedAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task JsonQueryPagedAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new JsonPagedResult<TestEntity>(
            new List<JsonQueryResult<TestEntity>>(),
            TotalCount: 0,
            Offset: 0,
            Limit: 10);
        _innerStoreMock
            .Setup(x => x.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                0, 10,
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.JsonQueryPagedAsync(null, 0, 10);

        // Assert
        Assert.Equal(0, result.TotalCount);
    }

    #endregion

    #region JsonCountAsync Tests

    /// <summary>
    /// Verifies that JsonCountAsync delegates to the inner store and returns its result.
    /// </summary>
    [Fact]
    public async Task JsonCountAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.JsonCountAsync(It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(15);

        // Act
        var result = await _sut.JsonCountAsync(null);

        // Assert
        Assert.Equal(15, result);
        _innerStoreMock.Verify(
            x => x.JsonCountAsync(null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that JsonCountAsync records telemetry with "json_count" operation name.
    /// </summary>
    [Fact]
    public async Task JsonCountAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.JsonCountAsync(It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        await _sut.JsonCountAsync(null);

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.json_count",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    #endregion

    #region JsonDistinctAsync Tests

    /// <summary>
    /// Verifies that JsonDistinctAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task JsonDistinctAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<object?> { "value1", "value2" };
        _innerStoreMock
            .Setup(x => x.JsonDistinctAsync("$.Name", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.JsonDistinctAsync("$.Name");

        // Assert
        Assert.Equal(2, result.Count);
        _innerStoreMock.Verify(
            x => x.JsonDistinctAsync("$.Name", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region JsonAggregateAsync Tests

    /// <summary>
    /// Verifies that JsonAggregateAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task JsonAggregateAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.JsonAggregateAsync(
                "$.Score", JsonAggregation.Sum, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)100.0);

        // Act
        var result = await _sut.JsonAggregateAsync("$.Score", JsonAggregation.Sum);

        // Assert
        Assert.Equal(100.0, result);
    }

    #endregion

    /// <summary>
    /// Test entity for JSON queryable state store tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
