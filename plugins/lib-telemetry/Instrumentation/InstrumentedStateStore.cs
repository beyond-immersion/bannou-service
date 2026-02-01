#nullable enable

using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Telemetry.Instrumentation;

/// <summary>
/// Decorator that wraps an IStateStore to add telemetry instrumentation.
/// Records traces and metrics for all state store operations.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public class InstrumentedStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    private readonly IStateStore<TValue> _inner;
    private readonly ITelemetryProvider _telemetry;
    private readonly string _storeName;
    private readonly string _backend;

    /// <summary>
    /// Creates a new instrumented state store wrapper.
    /// </summary>
    /// <param name="inner">The underlying state store to wrap.</param>
    /// <param name="telemetry">Telemetry provider for instrumentation.</param>
    /// <param name="storeName">Name of the state store.</param>
    /// <param name="backend">Backend type (redis, mysql, memory).</param>
    public InstrumentedStateStore(
        IStateStore<TValue> inner,
        ITelemetryProvider telemetry,
        string storeName,
        string backend)
    {
        _inner = inner;
        _telemetry = telemetry;
        _storeName = storeName;
        _backend = backend;
    }

    /// <summary>
    /// Gets the underlying store for advanced operations.
    /// </summary>
    protected IStateStore<TValue> Inner => _inner;

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
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("get");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.GetAsync(key, cancellationToken);
            RecordSuccess(activity, "get", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "get", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("get_with_etag");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.GetWithETagAsync(key, cancellationToken);
            RecordSuccess(activity, "get_with_etag", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "get_with_etag", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(string key, TValue value, StateOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("save");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SaveAsync(key, value, options, cancellationToken);
            RecordSuccess(activity, "save", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "save", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> TrySaveAsync(string key, TValue value, string etag, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("try_save");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.TrySaveAsync(key, value, etag, cancellationToken);
            RecordSuccess(activity, "try_save", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "try_save", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("delete");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.DeleteAsync(key, cancellationToken);
            RecordSuccess(activity, "delete", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "delete", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("exists");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.ExistsAsync(key, cancellationToken);
            RecordSuccess(activity, "exists", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "exists", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("get_bulk");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.GetBulkAsync(keys, cancellationToken);
            RecordSuccess(activity, "get_bulk", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "get_bulk", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> SaveBulkAsync(IEnumerable<KeyValuePair<string, TValue>> items, StateOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("save_bulk");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SaveBulkAsync(items, options, cancellationToken);
            RecordSuccess(activity, "save_bulk", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "save_bulk", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> ExistsBulkAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("exists_bulk");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.ExistsBulkAsync(keys, cancellationToken);
            RecordSuccess(activity, "exists_bulk", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "exists_bulk", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteBulkAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("delete_bulk");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.DeleteBulkAsync(keys, cancellationToken);
            RecordSuccess(activity, "delete_bulk", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "delete_bulk", sw, ex);
            throw;
        }
    }

    // ==================== Set Operations ====================

    /// <inheritdoc/>
    public async Task<bool> AddToSetAsync<TItem>(string key, TItem item, StateOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("add_to_set");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.AddToSetAsync(key, item, options, cancellationToken);
            RecordSuccess(activity, "add_to_set", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "add_to_set", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> AddToSetAsync<TItem>(string key, IEnumerable<TItem> items, StateOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("add_to_set_bulk");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.AddToSetAsync(key, items, options, cancellationToken);
            RecordSuccess(activity, "add_to_set_bulk", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "add_to_set_bulk", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveFromSetAsync<TItem>(string key, TItem item, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("remove_from_set");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.RemoveFromSetAsync(key, item, cancellationToken);
            RecordSuccess(activity, "remove_from_set", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "remove_from_set", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TItem>> GetSetAsync<TItem>(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("get_set");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.GetSetAsync<TItem>(key, cancellationToken);
            RecordSuccess(activity, "get_set", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "get_set", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetContainsAsync<TItem>(string key, TItem item, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("set_contains");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SetContainsAsync(key, item, cancellationToken);
            RecordSuccess(activity, "set_contains", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "set_contains", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> SetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("set_count");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SetCountAsync(key, cancellationToken);
            RecordSuccess(activity, "set_count", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "set_count", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSetAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("delete_set");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.DeleteSetAsync(key, cancellationToken);
            RecordSuccess(activity, "delete_set", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "delete_set", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshSetTtlAsync(string key, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("refresh_set_ttl");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.RefreshSetTtlAsync(key, ttlSeconds, cancellationToken);
            RecordSuccess(activity, "refresh_set_ttl", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "refresh_set_ttl", sw, ex);
            throw;
        }
    }

    // ==================== Sorted Set Operations ====================

    /// <inheritdoc/>
    public async Task<bool> SortedSetAddAsync(string key, string member, double score, StateOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_add");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetAddAsync(key, member, score, options, cancellationToken);
            RecordSuccess(activity, "sorted_set_add", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_add", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetAddBatchAsync(string key, IEnumerable<(string member, double score)> entries, StateOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_add_batch");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetAddBatchAsync(key, entries, options, cancellationToken);
            RecordSuccess(activity, "sorted_set_add_batch", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_add_batch", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetRemoveAsync(string key, string member, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_remove");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetRemoveAsync(key, member, cancellationToken);
            RecordSuccess(activity, "sorted_set_remove", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_remove", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<double?> SortedSetScoreAsync(string key, string member, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_score");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetScoreAsync(key, member, cancellationToken);
            RecordSuccess(activity, "sorted_set_score", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_score", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long?> SortedSetRankAsync(string key, string member, bool descending = true, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_rank");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetRankAsync(key, member, descending, cancellationToken);
            RecordSuccess(activity, "sorted_set_rank", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_rank", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByRankAsync(string key, long start, long stop, bool descending = true, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_range_by_rank");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetRangeByRankAsync(key, start, stop, descending, cancellationToken);
            RecordSuccess(activity, "sorted_set_range_by_rank", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_range_by_rank", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByScoreAsync(
        string key,
        double minScore,
        double maxScore,
        int offset = 0,
        int count = -1,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_range_by_score");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetRangeByScoreAsync(key, minScore, maxScore, offset, count, descending, cancellationToken);
            RecordSuccess(activity, "sorted_set_range_by_score", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_range_by_score", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_count");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetCountAsync(key, cancellationToken);
            RecordSuccess(activity, "sorted_set_count", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_count", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<double> SortedSetIncrementAsync(string key, string member, double increment, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_increment");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetIncrementAsync(key, member, increment, cancellationToken);
            RecordSuccess(activity, "sorted_set_increment", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_increment", sw, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetDeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("sorted_set_delete");
        activity?.SetTag("bannou.state.key", key);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.SortedSetDeleteAsync(key, cancellationToken);
            RecordSuccess(activity, "sorted_set_delete", sw);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(activity, "sorted_set_delete", sw, ex);
            throw;
        }
    }
}
