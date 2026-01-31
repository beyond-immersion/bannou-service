#nullable enable

using BeyondImmersion.BannouService.Services;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Telemetry.Instrumentation;

/// <summary>
/// Decorator that wraps an IQueryableStateStore to add telemetry instrumentation.
/// Records traces and metrics for all queryable state store operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public class InstrumentedQueryableStateStore<TValue> : InstrumentedStateStore<TValue>, IQueryableStateStore<TValue>
    where TValue : class
{
    private readonly IQueryableStateStore<TValue> _innerQueryable;
    private readonly ITelemetryProvider _telemetry;
    private readonly string _storeName;
    private readonly string _backend;

    /// <summary>
    /// Creates a new instrumented queryable state store wrapper.
    /// </summary>
    /// <param name="inner">The underlying queryable state store to wrap.</param>
    /// <param name="telemetry">Telemetry provider for instrumentation.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (mysql).</param>
    public InstrumentedQueryableStateStore(
        IQueryableStateStore<TValue> inner,
        ITelemetryProvider telemetry,
        string storeName,
        string backend)
        : base(inner, telemetry, storeName, backend)
    {
        _innerQueryable = inner;
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
    public async Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("query");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerQueryable.QueryAsync(predicate, cancellationToken);
            RecordSuccess(activity, "query", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "query", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResult<TValue>> QueryPagedAsync(
        Expression<Func<TValue, bool>>? predicate,
        int page,
        int pageSize,
        Expression<Func<TValue, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("query_paged");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerQueryable.QueryPagedAsync(predicate, page, pageSize, orderBy, descending, cancellationToken);
            RecordSuccess(activity, "query_paged", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "query_paged", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(
        Expression<Func<TValue, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("count");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerQueryable.CountAsync(predicate, cancellationToken);
            RecordSuccess(activity, "count", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "count", sw, ex);
            throw;
        }
    }
}
