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

/// <summary>
/// Standard component names for telemetry instrumentation.
/// </summary>
public static class TelemetryComponents
{
    /// <summary>
    /// State store operations (lib-state).
    /// </summary>
    public const string State = "bannou.state";

    /// <summary>
    /// Messaging operations (lib-messaging).
    /// </summary>
    public const string Messaging = "bannou.messaging";

    /// <summary>
    /// Service mesh operations (lib-mesh).
    /// </summary>
    public const string Mesh = "bannou.mesh";
}

/// <summary>
/// Standard metric names for telemetry instrumentation.
/// </summary>
public static class TelemetryMetrics
{
    // State store metrics
    /// <summary>
    /// Counter for state store operations.
    /// </summary>
    public const string StateOperations = "bannou.state.operations";

    /// <summary>
    /// Histogram for state store operation durations.
    /// </summary>
    public const string StateDuration = "bannou.state.duration_seconds";

    // Messaging metrics
    /// <summary>
    /// Counter for published messages.
    /// </summary>
    public const string MessagingPublished = "bannou.messaging.published";

    /// <summary>
    /// Counter for consumed messages.
    /// </summary>
    public const string MessagingConsumed = "bannou.messaging.consumed";

    /// <summary>
    /// Histogram for message publish durations.
    /// </summary>
    public const string MessagingPublishDuration = "bannou.messaging.publish_duration_seconds";

    /// <summary>
    /// Histogram for message consume durations.
    /// </summary>
    public const string MessagingConsumeDuration = "bannou.messaging.consume_duration_seconds";

    // Mesh metrics
    /// <summary>
    /// Counter for mesh invocations.
    /// </summary>
    public const string MeshInvocations = "bannou.mesh.invocations";

    /// <summary>
    /// Histogram for mesh invocation durations.
    /// </summary>
    public const string MeshDuration = "bannou.mesh.duration_seconds";

    /// <summary>
    /// Gauge for circuit breaker state.
    /// </summary>
    public const string MeshCircuitBreakerState = "bannou.mesh.circuit_breaker_state";

    /// <summary>
    /// Counter for circuit breaker state changes.
    /// </summary>
    public const string MeshCircuitBreakerStateChanges = "bannou.mesh.circuit_breaker_state_changes";

    /// <summary>
    /// Counter for mesh retries.
    /// </summary>
    public const string MeshRetries = "bannou.mesh.retries";

    /// <summary>
    /// Counter for raw mesh invocations (no circuit breaker participation).
    /// Used by prebound APIs where target services may be intentionally offline.
    /// </summary>
    public const string MeshRawInvocations = "bannou.mesh.raw_invocations";
}
