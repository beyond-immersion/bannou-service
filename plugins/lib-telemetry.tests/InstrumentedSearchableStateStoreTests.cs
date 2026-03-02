using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Telemetry.Instrumentation;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Telemetry.Tests;

/// <summary>
/// Unit tests for InstrumentedSearchableStateStore.
/// Verifies that full-text search operations are properly delegated and instrumented.
/// </summary>
public class InstrumentedSearchableStateStoreTests
{
    private readonly Mock<ISearchableStateStore<TestEntity>> _innerStoreMock;
    private readonly Mock<ITelemetryProvider> _telemetryMock;
    private readonly InstrumentedSearchableStateStore<TestEntity> _sut;

    public InstrumentedSearchableStateStoreTests()
    {
        _innerStoreMock = new Mock<ISearchableStateStore<TestEntity>>();
        _telemetryMock = new Mock<ITelemetryProvider>();

        _telemetryMock
            .Setup(x => x.StartActivity(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<System.Diagnostics.ActivityKind>(),
                It.IsAny<System.Diagnostics.ActivityContext?>()))
            .Returns((System.Diagnostics.Activity?)null);

        _sut = new InstrumentedSearchableStateStore<TestEntity>(
            _innerStoreMock.Object,
            _telemetryMock.Object,
            "test-search-store",
            "redis");
    }

    #region CreateIndexAsync Tests

    /// <summary>
    /// Verifies that CreateIndexAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task CreateIndexAsync_DelegatesToInnerStore()
    {
        // Arrange
        var schema = new List<SearchSchemaField>
        {
            new() { Path = "$.Name", Type = SearchFieldType.Text }
        };
        _innerStoreMock
            .Setup(x => x.CreateIndexAsync("idx:test", It.IsAny<IReadOnlyList<SearchSchemaField>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.CreateIndexAsync("idx:test", schema);

        // Assert
        Assert.True(result);
        _innerStoreMock.Verify(
            x => x.CreateIndexAsync("idx:test", schema, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that CreateIndexAsync records telemetry with "create_index" operation.
    /// </summary>
    [Fact]
    public async Task CreateIndexAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.CreateIndexAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<SearchSchemaField>>(), It.IsAny<SearchIndexOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.CreateIndexAsync("idx:test", new List<SearchSchemaField>());

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.create_index",
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

    #endregion

    #region SearchAsync Tests

    /// <summary>
    /// Verifies that SearchAsync delegates to the inner store and returns its result.
    /// </summary>
    [Fact]
    public async Task SearchAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new SearchPagedResult<TestEntity>(
            Items: new List<SearchResult<TestEntity>>
            {
                new("key1", new TestEntity { Id = "1", Name = "test" }, 1.0)
            },
            TotalCount: 1,
            Offset: 0,
            Limit: 10);
        _innerStoreMock
            .Setup(x => x.SearchAsync("idx:test", "test query", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.SearchAsync("idx:test", "test query");

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        _innerStoreMock.Verify(
            x => x.SearchAsync("idx:test", "test query", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that SearchAsync records telemetry with "search" operation.
    /// </summary>
    [Fact]
    public async Task SearchAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchQueryOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchPagedResult<TestEntity>(new List<SearchResult<TestEntity>>(), 0, 0, 10));

        // Act
        await _sut.SearchAsync("idx:test", "query");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.search",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    /// <summary>
    /// Verifies that SearchAsync propagates exceptions and records failure metrics.
    /// </summary>
    [Fact]
    public async Task SearchAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Search failed");
        _innerStoreMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchQueryOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SearchAsync("idx:test", "query"));
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

    #region SuggestAsync Tests

    /// <summary>
    /// Verifies that SuggestAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task SuggestAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<(string, double)> { ("suggestion1", 0.9), ("suggestion2", 0.8) };
        _innerStoreMock
            .Setup(x => x.SuggestAsync("idx:test", "sug", 5, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.SuggestAsync("idx:test", "sug");

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region DropIndexAsync Tests

    /// <summary>
    /// Verifies that DropIndexAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task DropIndexAsync_DelegatesToInnerStore()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DropIndexAsync("idx:test", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DropIndexAsync("idx:test");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetIndexInfoAsync Tests

    /// <summary>
    /// Verifies that GetIndexInfoAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task GetIndexInfoAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new SearchIndexInfo
        {
            Name = "idx:test",
            DocumentCount = 100
        };
        _innerStoreMock
            .Setup(x => x.GetIndexInfoAsync("idx:test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.GetIndexInfoAsync("idx:test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("idx:test", result.Name);
    }

    #endregion

    #region ListIndexesAsync Tests

    /// <summary>
    /// Verifies that ListIndexesAsync delegates to the inner store.
    /// </summary>
    [Fact]
    public async Task ListIndexesAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new List<string> { "idx:test1", "idx:test2" };
        _innerStoreMock
            .Setup(x => x.ListIndexesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.ListIndexesAsync();

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region Base Operations Still Work

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
    /// Test entity for searchable state store tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
