using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Telemetry.Instrumentation;
using Moq;
using System.Linq.Expressions;
using Xunit;

namespace BeyondImmersion.BannouService.Telemetry.Tests;

/// <summary>
/// Unit tests for InstrumentedQueryableStateStore.
/// Verifies that LINQ query operations are properly delegated and instrumented.
/// </summary>
public class InstrumentedQueryableStateStoreTests
{
    private readonly Mock<IQueryableStateStore<TestEntity>> _innerStoreMock;
    private readonly Mock<ITelemetryProvider> _telemetryMock;
    private readonly InstrumentedQueryableStateStore<TestEntity> _sut;

    public InstrumentedQueryableStateStoreTests()
    {
        _innerStoreMock = new Mock<IQueryableStateStore<TestEntity>>();
        _telemetryMock = new Mock<ITelemetryProvider>();

        _telemetryMock
            .Setup(x => x.StartActivity(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<System.Diagnostics.ActivityKind>(),
                It.IsAny<System.Diagnostics.ActivityContext?>()))
            .Returns((System.Diagnostics.Activity?)null);

        _sut = new InstrumentedQueryableStateStore<TestEntity>(
            _innerStoreMock.Object,
            _telemetryMock.Object,
            "test-queryable-store",
            "mysql");
    }

    #region QueryAsync Tests

    /// <summary>
    /// Verifies that QueryAsync delegates to the inner store and returns its result.
    /// </summary>
    [Fact]
    public async Task QueryAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<TestEntity> { new() { Id = "1", Name = "Test" } };
        Expression<Func<TestEntity, bool>> predicate = x => x.Id == "1";
        _innerStoreMock
            .Setup(x => x.QueryAsync(It.IsAny<Expression<Func<TestEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.QueryAsync(predicate);

        // Assert
        Assert.Single(result);
        Assert.Equal("1", result[0].Id);
        _innerStoreMock.Verify(
            x => x.QueryAsync(It.IsAny<Expression<Func<TestEntity, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that QueryAsync records telemetry activity and metrics.
    /// </summary>
    [Fact]
    public async Task QueryAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.QueryAsync(It.IsAny<Expression<Func<TestEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestEntity>());

        // Act
        await _sut.QueryAsync(x => x.Id == "1");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.query",
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
    /// Verifies that QueryAsync propagates exceptions and still records failure metrics.
    /// </summary>
    [Fact]
    public async Task QueryAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Query failed");
        _innerStoreMock
            .Setup(x => x.QueryAsync(It.IsAny<Expression<Func<TestEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.QueryAsync(x => x.Id == "1"));
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

    #region QueryPagedAsync Tests

    /// <summary>
    /// Verifies that QueryPagedAsync delegates to the inner store and returns its result.
    /// </summary>
    [Fact]
    public async Task QueryPagedAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new PagedResult<TestEntity>(
            new List<TestEntity> { new() { Id = "1" } },
            TotalCount: 1,
            Page: 1,
            PageSize: 10);
        _innerStoreMock
            .Setup(x => x.QueryPagedAsync(
                It.IsAny<Expression<Func<TestEntity, bool>>?>(),
                1, 10,
                It.IsAny<Expression<Func<TestEntity, object>>?>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.QueryPagedAsync(null, 1, 10);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
    }

    #endregion

    #region CountAsync Tests

    /// <summary>
    /// Verifies that CountAsync delegates to the inner store and returns its result.
    /// </summary>
    [Fact]
    public async Task CountAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.CountAsync(It.IsAny<Expression<Func<TestEntity, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        // Act
        var result = await _sut.CountAsync();

        // Assert
        Assert.Equal(42, result);
        _innerStoreMock.Verify(
            x => x.CountAsync(It.IsAny<Expression<Func<TestEntity, bool>>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that CountAsync records telemetry with "count" operation name.
    /// </summary>
    [Fact]
    public async Task CountAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.CountAsync(It.IsAny<Expression<Func<TestEntity, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        await _sut.CountAsync();

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.count",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    #endregion

    #region Base IStateStore Operations Still Work

    /// <summary>
    /// Verifies that base GetAsync still delegates through the inheritance chain.
    /// </summary>
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

    #endregion

    /// <summary>
    /// Test entity for queryable state store tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
