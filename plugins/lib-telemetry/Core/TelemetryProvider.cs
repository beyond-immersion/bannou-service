#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Telemetry.Instrumentation;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Telemetry;

/// <summary>
/// Default implementation of ITelemetryProvider.
/// Manages ActivitySources and Meters for Bannou infrastructure instrumentation.
/// </summary>
public sealed class TelemetryProvider : ITelemetryProvider, IDisposable
{
    private readonly TelemetryServiceConfiguration _configuration;
    private readonly ILogger<TelemetryProvider> _logger;

    // ActivitySources by component name (for tracing)
    private readonly ConcurrentDictionary<string, ActivitySource> _activitySources = new();

    // Meters by component name (for metrics)
    private readonly ConcurrentDictionary<string, Meter> _meters = new();

    // Counters by component:metricName key
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();

    // Histograms by component:metricName key
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();

    private bool _disposed;

    /// <summary>
    /// Service name used for telemetry identification.
    /// </summary>
    public string ServiceName { get; }

    /// <inheritdoc/>
    public bool TracingEnabled => _configuration.TracingEnabled;

    /// <inheritdoc/>
    public bool MetricsEnabled => _configuration.MetricsEnabled;

    /// <summary>
    /// Creates a new TelemetryProvider instance.
    /// </summary>
    /// <param name="configuration">Telemetry configuration.</param>
    /// <param name="serviceName">Service name for telemetry (typically effective app-id).</param>
    /// <param name="logger">Logger instance.</param>
    public TelemetryProvider(
        TelemetryServiceConfiguration configuration,
        string serviceName,
        ILogger<TelemetryProvider> logger)
    {
        _configuration = configuration;
        ServiceName = serviceName;
        _logger = logger;

        _logger.LogInformation(
            "TelemetryProvider initialized: tracing={TracingEnabled}, metrics={MetricsEnabled}, serviceName={ServiceName}",
            TracingEnabled, MetricsEnabled, ServiceName);
    }

    /// <inheritdoc/>
    public ActivitySource? GetActivitySource(string componentName)
    {
        if (!TracingEnabled)
        {
            return null;
        }

        return _activitySources.GetOrAdd(componentName, name =>
        {
            var source = new ActivitySource(name, "1.0.0");
            _logger.LogDebug("Created ActivitySource for component: {ComponentName}", name);
            return source;
        });
    }

    /// <inheritdoc/>
    public Meter? GetMeter(string componentName)
    {
        if (!MetricsEnabled)
        {
            return null;
        }

        return _meters.GetOrAdd(componentName, name =>
        {
            var meter = new Meter(name, "1.0.0");
            _logger.LogDebug("Created Meter for component: {ComponentName}", name);
            return meter;
        });
    }

    /// <inheritdoc/>
    public Activity? StartActivity(
        string componentName,
        string operationName,
        ActivityKind kind = ActivityKind.Internal,
        ActivityContext? parentContext = null)
    {
        var source = GetActivitySource(componentName);
        if (source == null)
        {
            return null;
        }

        var activity = parentContext.HasValue
            ? source.StartActivity(operationName, kind, parentContext.Value)
            : source.StartActivity(operationName, kind);

        return activity;
    }

    /// <inheritdoc/>
    public void RecordCounter(
        string componentName,
        string metricName,
        long value = 1,
        params KeyValuePair<string, object?>[] tags)
    {
        if (!MetricsEnabled)
        {
            return;
        }

        var counter = GetOrCreateCounter(componentName, metricName);
        if (counter == null)
        {
            return;
        }

        if (tags.Length > 0)
        {
            var tagList = new TagList();
            foreach (var tag in tags)
            {
                tagList.Add(tag.Key, tag.Value);
            }
            counter.Add(value, tagList);
        }
        else
        {
            counter.Add(value);
        }
    }

    /// <inheritdoc/>
    public void RecordHistogram(
        string componentName,
        string metricName,
        double value,
        params KeyValuePair<string, object?>[] tags)
    {
        if (!MetricsEnabled)
        {
            return;
        }

        var histogram = GetOrCreateHistogram(componentName, metricName);
        if (histogram == null)
        {
            return;
        }

        if (tags.Length > 0)
        {
            var tagList = new TagList();
            foreach (var tag in tags)
            {
                tagList.Add(tag.Key, tag.Value);
            }
            histogram.Record(value, tagList);
        }
        else
        {
            histogram.Record(value);
        }
    }

    private Counter<long>? GetOrCreateCounter(string componentName, string metricName)
    {
        var key = $"{componentName}:{metricName}";

        return _counters.GetOrAdd(key, _ =>
        {
            var meter = GetMeter(componentName);
            if (meter == null)
            {
                // This shouldn't happen since we check MetricsEnabled above,
                // but handle gracefully
                return null!;
            }

            return meter.CreateCounter<long>(
                metricName,
                unit: "{operations}",
                description: $"Counter for {metricName}");
        });
    }

    private Histogram<double>? GetOrCreateHistogram(string componentName, string metricName)
    {
        var key = $"{componentName}:{metricName}";

        return _histograms.GetOrAdd(key, _ =>
        {
            var meter = GetMeter(componentName);
            if (meter == null)
            {
                return null!;
            }

            return meter.CreateHistogram<double>(
                metricName,
                unit: "s",
                description: $"Histogram for {metricName}");
        });
    }

    /// <inheritdoc/>
    public IStateStore<TValue> WrapStateStore<TValue>(IStateStore<TValue> store, string storeName, string backend)
        where TValue : class
    {
        // Only wrap if tracing or metrics is enabled
        if (!TracingEnabled && !MetricsEnabled)
        {
            return store;
        }

        return new InstrumentedStateStore<TValue>(store, this, storeName, backend);
    }

    /// <inheritdoc/>
    public IQueryableStateStore<TValue> WrapQueryableStateStore<TValue>(IQueryableStateStore<TValue> store, string storeName, string backend)
        where TValue : class
    {
        // Only wrap if tracing or metrics is enabled
        if (!TracingEnabled && !MetricsEnabled)
        {
            return store;
        }

        return new InstrumentedQueryableStateStore<TValue>(store, this, storeName, backend);
    }

    /// <inheritdoc/>
    public ISearchableStateStore<TValue> WrapSearchableStateStore<TValue>(ISearchableStateStore<TValue> store, string storeName, string backend)
        where TValue : class
    {
        // Only wrap if tracing or metrics is enabled
        if (!TracingEnabled && !MetricsEnabled)
        {
            return store;
        }

        return new InstrumentedSearchableStateStore<TValue>(store, this, storeName, backend);
    }

    /// <inheritdoc/>
    public IJsonQueryableStateStore<TValue> WrapJsonQueryableStateStore<TValue>(IJsonQueryableStateStore<TValue> store, string storeName, string backend)
        where TValue : class
    {
        // Only wrap if tracing or metrics is enabled
        if (!TracingEnabled && !MetricsEnabled)
        {
            return store;
        }

        return new InstrumentedJsonQueryableStateStore<TValue>(store, this, storeName, backend);
    }

    /// <summary>
    /// Disposes all managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var source in _activitySources.Values)
        {
            source.Dispose();
        }
        _activitySources.Clear();

        foreach (var meter in _meters.Values)
        {
            meter.Dispose();
        }
        _meters.Clear();

        _counters.Clear();
        _histograms.Clear();

        _disposed = true;
        _logger.LogInformation("TelemetryProvider disposed");
    }
}
