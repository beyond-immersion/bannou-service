using System.Diagnostics;
using System.Diagnostics.Metrics;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
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

        // Create some activity sources and meters
        _ = provider.GetActivitySource("test.component");
        _ = provider.GetMeter("test.component");

        // Act
        provider.Dispose();

        // Assert - subsequent calls should still work (return null for disabled)
        // After dispose, the provider should be in a clean state
        // This test verifies no exceptions are thrown during dispose
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

    /// <summary>
    /// Test model for store wrapping tests.
    /// </summary>
    private class TestModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
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

        public Task<bool> AddToSetAsync<TItem>(string key, TItem item, StateOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<long> AddToSetAsync<TItem>(string key, IEnumerable<TItem> items, StateOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<bool> RemoveFromSetAsync<TItem>(string key, TItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<TItem>> GetSetAsync<TItem>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TItem>>(new List<TItem>());

        public Task<bool> SetContainsAsync<TItem>(string key, TItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<long> SetCountAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<bool> DeleteSetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> RefreshSetTtlAsync(string key, int ttlSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> SortedSetAddAsync(string key, string member, double score, StateOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<long> SortedSetAddBatchAsync(string key, IEnumerable<(string member, double score)> entries, StateOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<bool> SortedSetRemoveAsync(string key, string member, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<double?> SortedSetScoreAsync(string key, string member, CancellationToken cancellationToken = default)
            => Task.FromResult<double?>(null);

        public Task<long?> SortedSetRankAsync(string key, string member, bool descending = true, CancellationToken cancellationToken = default)
            => Task.FromResult<long?>(null);

        public Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByRankAsync(string key, long start, long stop, bool descending = true, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<(string member, double score)>>(new List<(string, double)>());

        public Task<long> SortedSetCountAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<double> SortedSetIncrementAsync(string key, string member, double increment, CancellationToken cancellationToken = default)
            => Task.FromResult(increment);

        public Task<bool> SortedSetDeleteAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
