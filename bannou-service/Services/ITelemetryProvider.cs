#nullable enable

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Provides telemetry instrumentation capabilities for Bannou infrastructure libs.
/// Always injected into lib-state, lib-messaging, and lib-mesh (never null).
/// When telemetry is disabled, NullTelemetryProvider is used (all methods are no-ops).
/// </summary>
/// <remarks>
/// <para>
/// This interface is defined in bannou-service so that infrastructure libs (lib-state,
/// lib-messaging, lib-mesh) can reference it without depending on lib-telemetry.
/// </para>
/// <para>
/// NullTelemetryProvider is registered by default during startup. When lib-telemetry
/// plugin loads, its TelemetryProvider registration overrides the null implementation.
/// </para>
/// </remarks>
public interface ITelemetryProvider
{
    /// <summary>
    /// Whether distributed tracing is enabled and configured.
    /// </summary>
    bool TracingEnabled { get; }

    /// <summary>
    /// Whether metrics collection is enabled and configured.
    /// </summary>
    bool MetricsEnabled { get; }

    /// <summary>
    /// Get the ActivitySource for the specified component.
    /// ActivitySource is used to create trace spans.
    /// </summary>
    /// <param name="componentName">Component name (e.g., "state", "messaging", "mesh").</param>
    /// <returns>ActivitySource for the component, or null if tracing is disabled.</returns>
    ActivitySource? GetActivitySource(string componentName);

    /// <summary>
    /// Get the Meter for the specified component.
    /// Meter is used to create counters, histograms, and other metrics.
    /// </summary>
    /// <param name="componentName">Component name (e.g., "state", "messaging", "mesh").</param>
    /// <returns>Meter for the component, or null if metrics are disabled.</returns>
    Meter? GetMeter(string componentName);

    /// <summary>
    /// Start an activity (trace span) for an operation.
    /// This is a convenience method that handles null checks and configuration.
    /// </summary>
    /// <param name="componentName">Component name for the activity source.</param>
    /// <param name="operationName">Name of the operation being traced.</param>
    /// <param name="kind">The activity kind (default: Internal).</param>
    /// <param name="parentContext">Optional parent context for distributed tracing.</param>
    /// <returns>The started Activity, or null if tracing is disabled.</returns>
    Activity? StartActivity(
        string componentName,
        string operationName,
        ActivityKind kind = ActivityKind.Internal,
        ActivityContext? parentContext = null);

    /// <summary>
    /// Record a counter metric increment.
    /// </summary>
    /// <param name="componentName">Component name for the meter.</param>
    /// <param name="metricName">Name of the counter metric.</param>
    /// <param name="value">Value to add (default: 1).</param>
    /// <param name="tags">Optional tags for the metric.</param>
    void RecordCounter(
        string componentName,
        string metricName,
        long value = 1,
        params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Record a histogram metric value.
    /// </summary>
    /// <param name="componentName">Component name for the meter.</param>
    /// <param name="metricName">Name of the histogram metric.</param>
    /// <param name="value">Value to record.</param>
    /// <param name="tags">Optional tags for the metric.</param>
    void RecordHistogram(
        string componentName,
        string metricName,
        double value,
        params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Register a callback-based observable gauge that is sampled by the metrics exporter.
    /// The callback is invoked during each export/scrape cycle to read the current value.
    /// Registration is idempotent — duplicate registrations for the same component:metric are ignored.
    /// </summary>
    /// <typeparam name="T">The numeric type for the gauge value (int, long, double, etc.).</typeparam>
    /// <param name="componentName">Component name for the meter.</param>
    /// <param name="metricName">Name of the gauge metric.</param>
    /// <param name="observeValue">Callback that returns the current value when polled.</param>
    /// <param name="unit">Optional unit of measure (e.g., "{messages}", "{channels}").</param>
    /// <param name="description">Optional human-readable description.</param>
    void RegisterObservableGauge<T>(
        string componentName,
        string metricName,
        Func<T> observeValue,
        string? unit = null,
        string? description = null) where T : struct;

    /// <summary>
    /// Register a callback-based observable gauge that returns a <see cref="Measurement{T}"/>
    /// with attached tags. Use this overload when the gauge observation needs static tags.
    /// Registration is idempotent — duplicate registrations for the same component:metric are ignored.
    /// </summary>
    /// <typeparam name="T">The numeric type for the gauge value (int, long, double, etc.).</typeparam>
    /// <param name="componentName">Component name for the meter.</param>
    /// <param name="metricName">Name of the gauge metric.</param>
    /// <param name="observeValue">Callback that returns a measurement with tags when polled.</param>
    /// <param name="unit">Optional unit of measure.</param>
    /// <param name="description">Optional human-readable description.</param>
    void RegisterObservableGauge<T>(
        string componentName,
        string metricName,
        Func<Measurement<T>> observeValue,
        string? unit = null,
        string? description = null) where T : struct;

    /// <summary>
    /// Wrap a state store with instrumentation.
    /// Returns the original store if instrumentation is disabled.
    /// </summary>
    /// <typeparam name="TValue">Value type stored.</typeparam>
    /// <param name="store">The store to wrap.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (redis, mysql, memory).</param>
    /// <returns>Instrumented store wrapper or the original store.</returns>
    IStateStore<TValue> WrapStateStore<TValue>(IStateStore<TValue> store, string storeName, string backend)
        where TValue : class;

    /// <summary>
    /// Wrap a queryable state store with instrumentation.
    /// Returns the original store if instrumentation is disabled.
    /// </summary>
    /// <typeparam name="TValue">Value type stored.</typeparam>
    /// <param name="store">The store to wrap.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (mysql).</param>
    /// <returns>Instrumented store wrapper or the original store.</returns>
    IQueryableStateStore<TValue> WrapQueryableStateStore<TValue>(IQueryableStateStore<TValue> store, string storeName, string backend)
        where TValue : class;

    /// <summary>
    /// Wrap a searchable state store with instrumentation.
    /// Returns the original store if instrumentation is disabled.
    /// </summary>
    /// <typeparam name="TValue">Value type stored.</typeparam>
    /// <param name="store">The store to wrap.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (redis).</param>
    /// <returns>Instrumented store wrapper or the original store.</returns>
    ISearchableStateStore<TValue> WrapSearchableStateStore<TValue>(ISearchableStateStore<TValue> store, string storeName, string backend)
        where TValue : class;

    /// <summary>
    /// Wrap a JSON queryable state store with instrumentation.
    /// Returns the original store if instrumentation is disabled.
    /// </summary>
    /// <typeparam name="TValue">Value type stored.</typeparam>
    /// <param name="store">The store to wrap.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (mysql).</param>
    /// <returns>Instrumented store wrapper or the original store.</returns>
    IJsonQueryableStateStore<TValue> WrapJsonQueryableStateStore<TValue>(IJsonQueryableStateStore<TValue> store, string storeName, string backend)
        where TValue : class;

    /// <summary>
    /// Wrap a cacheable state store with instrumentation.
    /// Returns the original store if instrumentation is disabled.
    /// </summary>
    /// <typeparam name="TValue">Value type stored.</typeparam>
    /// <param name="store">The store to wrap.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (redis, memory).</param>
    /// <returns>Instrumented store wrapper or the original store.</returns>
    ICacheableStateStore<TValue> WrapCacheableStateStore<TValue>(ICacheableStateStore<TValue> store, string storeName, string backend)
        where TValue : class;
}

// TelemetryComponents and TelemetryMetrics classes are generated from
// schemas/telemetry-metrics.yaml into bannou-service/Generated/TelemetryMetrics.cs.
// Do not define metric or component constants in this file.
