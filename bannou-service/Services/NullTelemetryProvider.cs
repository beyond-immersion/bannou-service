#nullable enable

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Null-object implementation of ITelemetryProvider.
/// Registered as the default implementation before plugins load.
/// When lib-telemetry is enabled, its TelemetryProvider overrides this registration.
/// </summary>
public sealed class NullTelemetryProvider : ITelemetryProvider
{
    /// <inheritdoc />
    public bool TracingEnabled => false;

    /// <inheritdoc />
    public bool MetricsEnabled => false;

    /// <inheritdoc />
    public ActivitySource? GetActivitySource(string componentName) => null;

    /// <inheritdoc />
    public Meter? GetMeter(string componentName) => null;

    /// <inheritdoc />
    public Activity? StartActivity(
        string componentName,
        string operationName,
        ActivityKind kind = ActivityKind.Internal,
        ActivityContext? parentContext = null) => null;

    /// <inheritdoc />
    public void RecordCounter(
        string componentName,
        string metricName,
        long value = 1,
        params KeyValuePair<string, object?>[] tags)
    {
        // No-op when telemetry is disabled
    }

    /// <inheritdoc />
    public void RecordHistogram(
        string componentName,
        string metricName,
        double value,
        params KeyValuePair<string, object?>[] tags)
    {
        // No-op when telemetry is disabled
    }

    /// <inheritdoc />
    public IStateStore<TValue> WrapStateStore<TValue>(IStateStore<TValue> store, string storeName, string backend)
        where TValue : class => store;

    /// <inheritdoc />
    public IQueryableStateStore<TValue> WrapQueryableStateStore<TValue>(IQueryableStateStore<TValue> store, string storeName, string backend)
        where TValue : class => store;

    /// <inheritdoc />
    public ISearchableStateStore<TValue> WrapSearchableStateStore<TValue>(ISearchableStateStore<TValue> store, string storeName, string backend)
        where TValue : class => store;

    /// <inheritdoc />
    public IJsonQueryableStateStore<TValue> WrapJsonQueryableStateStore<TValue>(IJsonQueryableStateStore<TValue> store, string storeName, string backend)
        where TValue : class => store;
}
