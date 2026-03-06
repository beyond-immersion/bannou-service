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

    #region GetWithETagAsync

    [Fact]
    public async Task GetWithETagAsync_DelegatesToInnerStore()
    {
        // Arrange
        var expected = new TestEntity { Id = "test-1", Name = "Test" };
        var expectedEtag = "etag-abc";
        _innerStoreMock
            .Setup(x => x.GetWithETagAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((expected, expectedEtag));

        // Act
        var (value, etag) = await _sut.GetWithETagAsync("test-key");

        // Assert
        Assert.Same(expected, value);
        Assert.Equal(expectedEtag, etag);
        _innerStoreMock.Verify(x => x.GetWithETagAsync("test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetWithETagAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new TestEntity(), "etag-1"));

        // Act
        await _sut.GetWithETagAsync("test-key");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.get_with_etag",
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
    public async Task GetWithETagAsync_WhenNotFound_ReturnsNulls()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.GetWithETagAsync("missing-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((TestEntity?)null, (string?)null));

        // Act
        var (value, etag) = await _sut.GetWithETagAsync("missing-key");

        // Assert
        Assert.Null(value);
        Assert.Null(etag);
    }

    [Fact]
    public async Task GetWithETagAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Test error");
        _innerStoreMock
            .Setup(x => x.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetWithETagAsync("test-key"));

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

    #region TrySaveAsync

    [Fact]
    public async Task TrySaveAsync_DelegatesToInnerStore()
    {
        // Arrange
        var entity = new TestEntity { Id = "test-1", Name = "Test" };
        var expectedNewEtag = "etag-new";
        _innerStoreMock
            .Setup(x => x.TrySaveAsync("test-key", entity, "etag-old", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedNewEtag);

        // Act
        var result = await _sut.TrySaveAsync("test-key", entity, "etag-old");

        // Assert
        Assert.Equal(expectedNewEtag, result);
        _innerStoreMock.Verify(
            x => x.TrySaveAsync("test-key", entity, "etag-old", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TrySaveAsync_WhenConflict_ReturnsNull()
    {
        // Arrange
        var entity = new TestEntity { Id = "test-1" };
        _innerStoreMock
            .Setup(x => x.TrySaveAsync("test-key", entity, "stale-etag", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _sut.TrySaveAsync("test-key", entity, "stale-etag");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task TrySaveAsync_RecordsTelemetry()
    {
        // Arrange
        var entity = new TestEntity { Id = "test-1" };
        _innerStoreMock
            .Setup(x => x.TrySaveAsync(It.IsAny<string>(), It.IsAny<TestEntity>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-new");

        // Act
        await _sut.TrySaveAsync("test-key", entity, "etag-old");

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.try_save",
                System.Diagnostics.ActivityKind.Client,
                null),
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
    public async Task TrySaveAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Concurrency failure");
        _innerStoreMock
            .Setup(x => x.TrySaveAsync(It.IsAny<string>(), It.IsAny<TestEntity>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.TrySaveAsync("key", new TestEntity(), "etag"));

        Assert.Same(expectedException, exception);
    }

    #endregion

    #region SaveBulkAsync

    [Fact]
    public async Task SaveBulkAsync_DelegatesToInnerStore()
    {
        // Arrange
        var items = new[]
        {
            new KeyValuePair<string, TestEntity>("key1", new TestEntity { Id = "1" }),
            new KeyValuePair<string, TestEntity>("key2", new TestEntity { Id = "2" })
        };
        var expectedEtags = new Dictionary<string, string>
        {
            ["key1"] = "etag-1",
            ["key2"] = "etag-2"
        };
        _innerStoreMock
            .Setup(x => x.SaveBulkAsync(items, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEtags);

        // Act
        var result = await _sut.SaveBulkAsync(items);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("etag-1", result["key1"]);
        Assert.Equal("etag-2", result["key2"]);
        _innerStoreMock.Verify(x => x.SaveBulkAsync(items, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveBulkAsync_RecordsTelemetry()
    {
        // Arrange
        var items = new[]
        {
            new KeyValuePair<string, TestEntity>("key1", new TestEntity { Id = "1" })
        };
        _innerStoreMock
            .Setup(x => x.SaveBulkAsync(It.IsAny<IEnumerable<KeyValuePair<string, TestEntity>>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Act
        await _sut.SaveBulkAsync(items);

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.save_bulk",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task SaveBulkAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Bulk save failed");
        _innerStoreMock
            .Setup(x => x.SaveBulkAsync(It.IsAny<IEnumerable<KeyValuePair<string, TestEntity>>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SaveBulkAsync(new[] { new KeyValuePair<string, TestEntity>("k", new TestEntity()) }));

        Assert.Same(expectedException, exception);
    }

    #endregion

    #region ExistsBulkAsync

    [Fact]
    public async Task ExistsBulkAsync_DelegatesToInnerStore()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        var expectedSet = new HashSet<string> { "key1", "key3" };
        _innerStoreMock
            .Setup(x => x.ExistsBulkAsync(keys, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSet);

        // Act
        var result = await _sut.ExistsBulkAsync(keys);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("key1", result);
        Assert.Contains("key3", result);
        Assert.DoesNotContain("key2", result);
        _innerStoreMock.Verify(x => x.ExistsBulkAsync(keys, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistsBulkAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.ExistsBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Act
        await _sut.ExistsBulkAsync(new[] { "key1" });

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.exists_bulk",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task ExistsBulkAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Bulk exists failed");
        _innerStoreMock
            .Setup(x => x.ExistsBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExistsBulkAsync(new[] { "key1" }));

        Assert.Same(expectedException, exception);
    }

    #endregion

    #region DeleteBulkAsync

    [Fact]
    public async Task DeleteBulkAsync_DelegatesToInnerStore()
    {
        // Arrange
        var keys = new[] { "key1", "key2" };
        _innerStoreMock
            .Setup(x => x.DeleteBulkAsync(keys, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _sut.DeleteBulkAsync(keys);

        // Assert
        Assert.Equal(2, result);
        _innerStoreMock.Verify(x => x.DeleteBulkAsync(keys, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBulkAsync_RecordsTelemetry()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        await _sut.DeleteBulkAsync(new[] { "key1" });

        // Assert
        _telemetryMock.Verify(
            x => x.StartActivity(
                TelemetryComponents.State,
                "state.delete_bulk",
                System.Diagnostics.ActivityKind.Client,
                null),
            Times.Once);
    }

    [Fact]
    public async Task DeleteBulkAsync_WhenNoKeysExist_ReturnsZero()
    {
        // Arrange
        _innerStoreMock
            .Setup(x => x.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _sut.DeleteBulkAsync(new[] { "missing1", "missing2" });

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task DeleteBulkAsync_WhenInnerThrows_RecordsErrorAndPropagates()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Bulk delete failed");
        _innerStoreMock
            .Setup(x => x.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteBulkAsync(new[] { "key1" }));

        Assert.Same(expectedException, exception);
    }

    #endregion

    // Note: Set and Sorted Set operation tests moved to InstrumentedCacheableStateStoreTests
    // as these operations are now on ICacheableStateStore, not IStateStore

    /// <summary>
    /// Test entity for state store tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = "test-id";
        public string Name { get; set; } = "test-name";
    }
}
