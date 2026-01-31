#nullable enable

using System.Diagnostics;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using OpenTelemetry.Trace;

namespace BeyondImmersion.BannouService.Telemetry.Instrumentation;

/// <summary>
/// Decorator that wraps an IJsonQueryableStateStore to add telemetry instrumentation.
/// Records traces and metrics for all JSON queryable state store operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public class InstrumentedJsonQueryableStateStore<TValue> : InstrumentedQueryableStateStore<TValue>, IJsonQueryableStateStore<TValue>
    where TValue : class
{
    private readonly IJsonQueryableStateStore<TValue> _innerJsonQueryable;
    private readonly ITelemetryProvider _telemetry;
    private readonly string _storeName;
    private readonly string _backend;

    /// <summary>
    /// Creates a new instrumented JSON queryable state store wrapper.
    /// </summary>
    /// <param name="inner">The underlying JSON queryable state store to wrap.</param>
    /// <param name="telemetry">Telemetry provider for instrumentation.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (mysql).</param>
    public InstrumentedJsonQueryableStateStore(
        IJsonQueryableStateStore<TValue> inner,
        ITelemetryProvider telemetry,
        string storeName,
        string backend)
        : base(inner, telemetry, storeName, backend)
    {
        _innerJsonQueryable = inner;
        _telemetry = telemetry;
        _storeName = storeName;
        _backend = backend;
    }

    private Activity? StartOperation(string operation)
    {
        var activity = _telemetry.StartActivity(
            TelemetryComponents.State,
            $"state.{operation}",
            ActivityKind.Client);

        if (activity != null)
        {
            activity.SetTag("db.system", _backend);
            activity.SetTag("db.operation", operation);
            activity.SetTag("bannou.state.store", _storeName);
        }

        return activity;
    }

    private void RecordSuccess(Activity? activity, string operation, Stopwatch sw)
    {
        sw.Stop();
        var durationSeconds = sw.Elapsed.TotalSeconds;

        var tags = new[]
        {
            new KeyValuePair<string, object?>("store", _storeName),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("backend", _backend),
            new KeyValuePair<string, object?>("success", true)
        };

        _telemetry.RecordCounter(TelemetryComponents.State, TelemetryMetrics.StateOperations, 1, tags);
        _telemetry.RecordHistogram(TelemetryComponents.State, TelemetryMetrics.StateDuration, durationSeconds, tags);

        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private void RecordFailure(Activity? activity, string operation, Stopwatch sw, Exception ex)
    {
        sw.Stop();
        var durationSeconds = sw.Elapsed.TotalSeconds;

        var tags = new[]
        {
            new KeyValuePair<string, object?>("store", _storeName),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("backend", _backend),
            new KeyValuePair<string, object?>("success", false)
        };

        _telemetry.RecordCounter(TelemetryComponents.State, TelemetryMetrics.StateOperations, 1, tags);
        _telemetry.RecordHistogram(TelemetryComponents.State, TelemetryMetrics.StateDuration, durationSeconds, tags);

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JsonQueryResult<TValue>>> JsonQueryAsync(
        IReadOnlyList<QueryCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("json_query");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerJsonQueryable.JsonQueryAsync(conditions, cancellationToken);
            RecordSuccess(activity, "json_query", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "json_query", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<JsonPagedResult<TValue>> JsonQueryPagedAsync(
        IReadOnlyList<QueryCondition>? conditions,
        int offset,
        int limit,
        JsonSortSpec? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("json_query_paged");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerJsonQueryable.JsonQueryPagedAsync(conditions, offset, limit, sortBy, cancellationToken);
            RecordSuccess(activity, "json_query_paged", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "json_query_paged", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> JsonCountAsync(
        IReadOnlyList<QueryCondition>? conditions,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("json_count");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerJsonQueryable.JsonCountAsync(conditions, cancellationToken);
            RecordSuccess(activity, "json_count", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "json_count", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<object?>> JsonDistinctAsync(
        string path,
        IReadOnlyList<QueryCondition>? conditions = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("json_distinct");
        activity?.SetTag("bannou.state.path", path);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerJsonQueryable.JsonDistinctAsync(path, conditions, cancellationToken);
            RecordSuccess(activity, "json_distinct", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "json_distinct", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<object?> JsonAggregateAsync(
        string path,
        JsonAggregation aggregation,
        IReadOnlyList<QueryCondition>? conditions = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("json_aggregate");
        activity?.SetTag("bannou.state.path", path);
        activity?.SetTag("bannou.state.aggregation", aggregation.ToString());
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerJsonQueryable.JsonAggregateAsync(path, aggregation, conditions, cancellationToken);
            RecordSuccess(activity, "json_aggregate", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "json_aggregate", sw, ex);
            throw;
        }
    }
}
