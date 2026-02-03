#nullable enable

using BeyondImmersion.BannouService.Services;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Telemetry.Instrumentation;

/// <summary>
/// Decorator that wraps an ISearchableStateStore to add telemetry instrumentation.
/// Extends InstrumentedCacheableStateStore to inherit instrumentation for all cacheable operations
/// (sets, sorted sets, counters, hashes) and adds instrumentation for search-specific operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public class InstrumentedSearchableStateStore<TValue> : InstrumentedCacheableStateStore<TValue>, ISearchableStateStore<TValue>
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

    private Activity? StartSearchOperation(string operation)
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

    private void RecordSearchSuccess(Activity? activity, string operation, Stopwatch sw)
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

    private void RecordSearchFailure(Activity? activity, string operation, Stopwatch sw, Exception ex)
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
        using var activity = StartSearchOperation("create_index");
        activity?.SetTag("bannou.state.index", indexName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.CreateIndexAsync(indexName, schema, options, cancellationToken);
            RecordSearchSuccess(activity, "create_index", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordSearchFailure(activity, "create_index", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DropIndexAsync(
        string indexName,
        bool deleteDocuments = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartSearchOperation("drop_index");
        activity?.SetTag("bannou.state.index", indexName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.DropIndexAsync(indexName, deleteDocuments, cancellationToken);
            RecordSearchSuccess(activity, "drop_index", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordSearchFailure(activity, "drop_index", sw, ex);
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
        using var activity = StartSearchOperation("search");
        activity?.SetTag("bannou.state.index", indexName);
        activity?.SetTag("bannou.state.query", query);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.SearchAsync(indexName, query, options, cancellationToken);
            RecordSearchSuccess(activity, "search", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordSearchFailure(activity, "search", sw, ex);
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
        using var activity = StartSearchOperation("suggest");
        activity?.SetTag("bannou.state.index", indexName);
        activity?.SetTag("bannou.state.prefix", prefix);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.SuggestAsync(indexName, prefix, maxResults, fuzzy, cancellationToken);
            RecordSearchSuccess(activity, "suggest", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordSearchFailure(activity, "suggest", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<SearchIndexInfo?> GetIndexInfoAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartSearchOperation("get_index_info");
        activity?.SetTag("bannou.state.index", indexName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.GetIndexInfoAsync(indexName, cancellationToken);
            RecordSearchSuccess(activity, "get_index_info", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordSearchFailure(activity, "get_index_info", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListIndexesAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = StartSearchOperation("list_indexes");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _innerSearchable.ListIndexesAsync(cancellationToken);
            RecordSearchSuccess(activity, "list_indexes", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordSearchFailure(activity, "list_indexes", sw, ex);
            throw;
        }
    }
}
