using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace BeyondImmersion.BannouService.Telemetry.Tests;

/// <summary>
/// Unit tests for TelemetryProvider.
/// </summary>
public class TelemetryProviderTests
{
    private readonly Mock<ILogger<TelemetryProvider>> _loggerMock;

    public TelemetryProviderTests()
    {
        _loggerMock = new Mock<ILogger<TelemetryProvider>>();
    }

    private TelemetryProvider CreateProvider(
        bool tracingEnabled = true,
        bool metricsEnabled = true)
    {
        var config = new TelemetryServiceConfiguration
        {
            TracingEnabled = tracingEnabled,
            MetricsEnabled = metricsEnabled,
            OtlpEndpoint = "http://localhost:4317",
            OtlpProtocol = OtlpProtocol.Grpc,
            ServiceNamespace = "bannou",
            DeploymentEnvironment = "test"
        };

        return new TelemetryProvider(config, "test-service", _loggerMock.Object);
    }

    #region TracingEnabled/MetricsEnabled Properties

    [Fact]
    public void TracingEnabled_ReturnsConfigValue()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true);

        // Act & Assert
        Assert.True(provider.TracingEnabled);
    }

    [Fact]
    public void TracingEnabled_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false);

        // Act & Assert
        Assert.False(provider.TracingEnabled);
    }

    [Fact]
    public void MetricsEnabled_ReturnsConfigValue()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: true);

        // Act & Assert
        Assert.True(provider.MetricsEnabled);
    }

    [Fact]
    public void MetricsEnabled_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: false);

        // Act & Assert
        Assert.False(provider.MetricsEnabled);
    }

    #endregion

    #region GetActivitySource

    [Fact]
    public void GetActivitySource_WhenTracingEnabled_ReturnsActivitySource()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true);

        // Act
        var source = provider.GetActivitySource("test.component");

        // Assert
        Assert.NotNull(source);
        Assert.Equal("test.component", source.Name);
    }

    [Fact]
    public void GetActivitySource_WhenTracingDisabled_ReturnsNull()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false);

        // Act
        var source = provider.GetActivitySource("test.component");

        // Assert
        Assert.Null(source);
    }

    [Fact]
    public void GetActivitySource_ReturnsSameInstanceForSameComponent()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true);

        // Act
        var source1 = provider.GetActivitySource("test.component");
        var source2 = provider.GetActivitySource("test.component");

        // Assert
        Assert.Same(source1, source2);
    }

    [Fact]
    public void GetActivitySource_ReturnsDifferentInstancesForDifferentComponents()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true);

        // Act
        var source1 = provider.GetActivitySource("component1");
        var source2 = provider.GetActivitySource("component2");

        // Assert
        Assert.NotSame(source1, source2);
    }

    #endregion

    #region GetMeter

    [Fact]
    public void GetMeter_WhenMetricsEnabled_ReturnsMeter()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: true);

        // Act
        var meter = provider.GetMeter("test.component");

        // Assert
        Assert.NotNull(meter);
        Assert.Equal("test.component", meter.Name);
    }

    [Fact]
    public void GetMeter_WhenMetricsDisabled_ReturnsNull()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: false);

        // Act
        var meter = provider.GetMeter("test.component");

        // Assert
        Assert.Null(meter);
    }

    [Fact]
    public void GetMeter_ReturnsSameInstanceForSameComponent()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: true);

        // Act
        var meter1 = provider.GetMeter("test.component");
        var meter2 = provider.GetMeter("test.component");

        // Assert
        Assert.Same(meter1, meter2);
    }

    #endregion

    #region StartActivity

    [Fact]
    public void StartActivity_WhenTracingEnabled_StartsActivity()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true);

        // Act - note: Activity may be null if no listener is registered
        // In unit tests without OpenTelemetry SDK, we verify the method doesn't throw
        var activity = provider.StartActivity(
            TelemetryComponents.State,
            "test.operation",
            ActivityKind.Internal);

        // Assert - activity may be null without listener, but call should succeed
        // The important thing is it doesn't throw
        activity?.Dispose();
    }

    [Fact]
    public void StartActivity_WhenTracingDisabled_ReturnsNull()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false);

        // Act
        var activity = provider.StartActivity(
            TelemetryComponents.State,
            "test.operation",
            ActivityKind.Internal);

        // Assert
        Assert.Null(activity);
    }

    #endregion

    #region RecordCounter

    [Fact]
    public void RecordCounter_WhenMetricsEnabled_DoesNotThrow()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: true);

        // Act & Assert - should not throw
        provider.RecordCounter(
            TelemetryComponents.State,
            TelemetryMetrics.StateOperations,
            1,
            new KeyValuePair<string, object?>("store", "test"),
            new KeyValuePair<string, object?>("operation", "get"));
    }

    [Fact]
    public void RecordCounter_WhenMetricsDisabled_DoesNotThrow()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: false);

        // Act & Assert - should not throw even when disabled
        provider.RecordCounter(
            TelemetryComponents.State,
            TelemetryMetrics.StateOperations,
            1,
            new KeyValuePair<string, object?>("store", "test"));
    }

    [Fact]
    public void RecordCounter_WithNoTags_DoesNotThrow()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: true);

        // Act & Assert - should handle empty tags
        provider.RecordCounter(TelemetryComponents.State, TelemetryMetrics.StateOperations);
    }

    #endregion

    #region RecordHistogram

    [Fact]
    public void RecordHistogram_WhenMetricsEnabled_DoesNotThrow()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: true);

        // Act & Assert - should not throw
        provider.RecordHistogram(
            TelemetryComponents.State,
            TelemetryMetrics.StateDuration,
            0.123,
            new KeyValuePair<string, object?>("store", "test"));
    }

    [Fact]
    public void RecordHistogram_WhenMetricsDisabled_DoesNotThrow()
    {
        // Arrange
        using var provider = CreateProvider(metricsEnabled: false);

        // Act & Assert - should not throw even when disabled
        provider.RecordHistogram(
            TelemetryComponents.State,
            TelemetryMetrics.StateDuration,
            0.123);
    }

    #endregion

    #region WrapStateStore

    [Fact]
    public void WrapStateStore_WhenBothEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: true);
        var store = new TestStateStore();

        // Act
        var wrapped = provider.WrapStateStore(store, "test-store", "redis");

        // Assert
        Assert.NotSame(store, wrapped);
        Assert.IsType<Instrumentation.InstrumentedStateStore<TestModel>>(wrapped);
    }

    [Fact]
    public void WrapStateStore_WhenBothDisabled_ReturnsOriginal()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: false);
        var store = new TestStateStore();

        // Act
        var wrapped = provider.WrapStateStore(store, "test-store", "redis");

        // Assert
        Assert.Same(store, wrapped);
    }

    [Fact]
    public void WrapStateStore_WhenOnlyTracingEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: false);
        var store = new TestStateStore();

        // Act
        var wrapped = provider.WrapStateStore(store, "test-store", "redis");

        // Assert
        Assert.NotSame(store, wrapped);
    }

    [Fact]
    public void WrapStateStore_WhenOnlyMetricsEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: true);
        var store = new TestStateStore();

        // Act
        var wrapped = provider.WrapStateStore(store, "test-store", "redis");

        // Assert
        Assert.NotSame(store, wrapped);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_DisposesActivitySourcesAndMeters()
    {
        // Arrange
        var provider = CreateProvider(tracingEnabled: true, metricsEnabled: true);

        // Create some activity sources and meters before dispose
        var sourceBeforeDispose = provider.GetActivitySource("test.component");
        var meterBeforeDispose = provider.GetMeter("test.component");

        Assert.NotNull(sourceBeforeDispose);
        Assert.NotNull(meterBeforeDispose);

        // Act
        provider.Dispose();

        // Assert - after dispose, dictionaries are cleared, so requesting
        // the same component returns a NEW instance (not the cached one)
        var sourceAfterDispose = provider.GetActivitySource("test.component");
        var meterAfterDispose = provider.GetMeter("test.component");

        Assert.NotNull(sourceAfterDispose);
        Assert.NotNull(meterAfterDispose);
        Assert.NotSame(sourceBeforeDispose, sourceAfterDispose);
        Assert.NotSame(meterBeforeDispose, meterAfterDispose);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var provider = CreateProvider();

        // Act & Assert - multiple dispose calls should be safe
        provider.Dispose();
        provider.Dispose();
    }

    #endregion

    #region WrapQueryableStateStore

    [Fact]
    public void WrapQueryableStateStore_WhenBothEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: true);
        var store = new Mock<IQueryableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapQueryableStateStore(store, "test-queryable-store", "mysql");

        // Assert
        Assert.NotSame(store, wrapped);
        Assert.IsType<Instrumentation.InstrumentedQueryableStateStore<TestModel>>(wrapped);
    }

    [Fact]
    public void WrapQueryableStateStore_WhenBothDisabled_ReturnsOriginal()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: false);
        var store = new Mock<IQueryableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapQueryableStateStore(store, "test-queryable-store", "mysql");

        // Assert
        Assert.Same(store, wrapped);
    }

    [Fact]
    public void WrapQueryableStateStore_WhenOnlyTracingEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: false);
        var store = new Mock<IQueryableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapQueryableStateStore(store, "test-queryable-store", "mysql");

        // Assert
        Assert.NotSame(store, wrapped);
    }

    #endregion

    #region WrapSearchableStateStore

    [Fact]
    public void WrapSearchableStateStore_WhenBothEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: true);
        var store = new Mock<ISearchableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapSearchableStateStore(store, "test-search-store", "redis");

        // Assert
        Assert.NotSame(store, wrapped);
        Assert.IsType<Instrumentation.InstrumentedSearchableStateStore<TestModel>>(wrapped);
    }

    [Fact]
    public void WrapSearchableStateStore_WhenBothDisabled_ReturnsOriginal()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: false);
        var store = new Mock<ISearchableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapSearchableStateStore(store, "test-search-store", "redis");

        // Assert
        Assert.Same(store, wrapped);
    }

    [Fact]
    public void WrapSearchableStateStore_WhenOnlyMetricsEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: true);
        var store = new Mock<ISearchableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapSearchableStateStore(store, "test-search-store", "redis");

        // Assert
        Assert.NotSame(store, wrapped);
    }

    #endregion

    #region WrapJsonQueryableStateStore

    [Fact]
    public void WrapJsonQueryableStateStore_WhenBothEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: true);
        var store = new Mock<IJsonQueryableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapJsonQueryableStateStore(store, "test-json-store", "mysql");

        // Assert
        Assert.NotSame(store, wrapped);
        Assert.IsType<Instrumentation.InstrumentedJsonQueryableStateStore<TestModel>>(wrapped);
    }

    [Fact]
    public void WrapJsonQueryableStateStore_WhenBothDisabled_ReturnsOriginal()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: false);
        var store = new Mock<IJsonQueryableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapJsonQueryableStateStore(store, "test-json-store", "mysql");

        // Assert
        Assert.Same(store, wrapped);
    }

    [Fact]
    public void WrapJsonQueryableStateStore_WhenOnlyTracingEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: false);
        var store = new Mock<IJsonQueryableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapJsonQueryableStateStore(store, "test-json-store", "mysql");

        // Assert
        Assert.NotSame(store, wrapped);
    }

    #endregion

    #region WrapCacheableStateStore

    [Fact]
    public void WrapCacheableStateStore_WhenBothEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: true, metricsEnabled: true);
        var store = new Mock<ICacheableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapCacheableStateStore(store, "test-cacheable-store", "redis");

        // Assert
        Assert.NotSame(store, wrapped);
        Assert.IsType<Instrumentation.InstrumentedCacheableStateStore<TestModel>>(wrapped);
    }

    [Fact]
    public void WrapCacheableStateStore_WhenBothDisabled_ReturnsOriginal()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: false);
        var store = new Mock<ICacheableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapCacheableStateStore(store, "test-cacheable-store", "redis");

        // Assert
        Assert.Same(store, wrapped);
    }

    [Fact]
    public void WrapCacheableStateStore_WhenOnlyMetricsEnabled_ReturnsWrapper()
    {
        // Arrange
        using var provider = CreateProvider(tracingEnabled: false, metricsEnabled: true);
        var store = new Mock<ICacheableStateStore<TestModel>>().Object;

        // Act
        var wrapped = provider.WrapCacheableStateStore(store, "test-cacheable-store", "redis");

        // Assert
        Assert.NotSame(store, wrapped);
    }

    #endregion

    /// <summary>
    /// Test model for store wrapping tests.
    /// </summary>
    public class TestModel
    {
        public string Id { get; set; } = "test-id";
        public string Name { get; set; } = "test-name";
    }

    /// <summary>
    /// Stub state store for testing WrapStateStore.
    /// Implements minimal IStateStore interface methods.
    /// </summary>
    private class TestStateStore : IStateStore<TestModel>
    {
        public Task<TestModel?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<TestModel?>(null);

        public Task<(TestModel? Value, string? ETag)> GetWithETagAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<(TestModel?, string?)>((null, null));

        public Task<string> SaveAsync(string key, TestModel value, StateOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult("etag-1");

        public Task<string?> TrySaveAsync(string key, TestModel value, string etag, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("etag-2");

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, TestModel>> GetBulkAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, TestModel>>(new Dictionary<string, TestModel>());

        public Task<IReadOnlyDictionary<string, string>> SaveBulkAsync(IEnumerable<KeyValuePair<string, TestModel>> items, StateOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<IReadOnlySet<string>> ExistsBulkAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<int> DeleteBulkAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
