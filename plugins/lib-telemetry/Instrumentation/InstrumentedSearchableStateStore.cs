#nullable enable

using System.Diagnostics;
using BeyondImmersion.BannouService.Services;
using OpenTelemetry.Trace;

namespace BeyondImmersion.BannouService.Telemetry.Instrumentation;

/// <summary>
/// Decorator that wraps an ISearchableStateStore to add telemetry instrumentation.
/// Records traces and metrics for all searchable state store operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public class InstrumentedSearchableStateStore<TValue> : InstrumentedStateStore<TValue>, ISearchableStateStore<TValue>
    where TValue : class
{
    private readonly ISearchableStateStore<TValue> _innerSearchable;
    private readonly ITelemetryProvider _telemetry;
    private readonly string _storeName;
    private readonly string _backend;

    /// <summary>
    /// Creates a new instrumented searchable state store wrapper.
    /// </summary>
    /// <param name="inner">The underlying searchable state store to wrap.</param>
    /// <param name="telemetry">Telemetry provider for instrumentation.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (redis).</param>
    public InstrumentedSearchableStateStore(
        ISearchableStateStore<TValue> inner,
        ITelemetryProvider telemetry,
        string storeName,
        string backend)
        : base(inner, telemetry, storeName, backend)
    {
        _innerSearchable = inner;
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
    public async Task<bool> CreateIndexAsync(
        string indexName,
        IReadOnlyList<SearchSchemaField> schema,
        SearchIndexOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("create_index");
        activity?.SetTag("bannou.state.index", indexName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.CreateIndexAsync(indexName, schema, options, cancellationToken);
            RecordSuccess(activity, "create_index", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "create_index", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DropIndexAsync(
        string indexName,
        bool deleteDocuments = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("drop_index");
        activity?.SetTag("bannou.state.index", indexName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.DropIndexAsync(indexName, deleteDocuments, cancellationToken);
            RecordSuccess(activity, "drop_index", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "drop_index", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<SearchPagedResult<TValue>> SearchAsync(
        string indexName,
        string query,
        SearchQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("search");
        activity?.SetTag("bannou.state.index", indexName);
        activity?.SetTag("bannou.state.query", query);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.SearchAsync(indexName, query, options, cancellationToken);
            RecordSuccess(activity, "search", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "search", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string Suggestion, double Score)>> SuggestAsync(
        string indexName,
        string prefix,
        int maxResults = 5,
        bool fuzzy = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("suggest");
        activity?.SetTag("bannou.state.index", indexName);
        activity?.SetTag("bannou.state.prefix", prefix);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.SuggestAsync(indexName, prefix, maxResults, fuzzy, cancellationToken);
            RecordSuccess(activity, "suggest", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "suggest", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<SearchIndexInfo?> GetIndexInfoAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("get_index_info");
        activity?.SetTag("bannou.state.index", indexName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.GetIndexInfoAsync(indexName, cancellationToken);
            RecordSuccess(activity, "get_index_info", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "get_index_info", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListIndexesAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("list_indexes");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.ListIndexesAsync(cancellationToken);
            RecordSuccess(activity, "list_indexes", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "list_indexes", sw, ex);
            throw;
        }
    }
}
