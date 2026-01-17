#nullable enable

using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Sort specification for JSON queries.
/// </summary>
public record JsonSortSpec
{
    /// <summary>
    /// JSON path to sort by.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Whether to sort descending.
    /// </summary>
    public bool Descending { get; init; }
}

/// <summary>
/// Result of a JSON query with the raw key included.
/// </summary>
/// <typeparam name="TValue">Value type.</typeparam>
public record JsonQueryResult<TValue>(string Key, TValue Value) where TValue : class;

/// <summary>
/// Paged result for JSON queries.
/// </summary>
/// <typeparam name="TValue">Value type.</typeparam>
public record JsonPagedResult<TValue>(
    IReadOnlyList<JsonQueryResult<TValue>> Items,
    long TotalCount,
    int Offset,
    int Limit) where TValue : class
{
    /// <summary>
    /// Whether there are more results available.
    /// </summary>
    public bool HasMore => Offset + Items.Count < TotalCount;
}

/// <summary>
/// Aggregation functions for JSON queries.
/// </summary>
public enum JsonAggregation
{
    /// <summary>
    /// Count of values.
    /// </summary>
    Count,

    /// <summary>
    /// Sum of numeric values.
    /// </summary>
    Sum,

    /// <summary>
    /// Average of numeric values.
    /// </summary>
    Avg,

    /// <summary>
    /// Minimum value.
    /// </summary>
    Min,

    /// <summary>
    /// Maximum value.
    /// </summary>
    Max
}

/// <summary>
/// JSON queryable state store - extends IQueryableStateStore with efficient MySQL JSON functions.
/// Uses server-side JSON_EXTRACT, JSON_CONTAINS, etc. instead of loading all data into memory.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public interface IJsonQueryableStateStore<TValue> : IQueryableStateStore<TValue>
    where TValue : class
{
    /// <summary>
    /// Query entries using JSON path conditions.
    /// Executes directly on MySQL using JSON functions.
    /// </summary>
    /// <param name="conditions">Query conditions (combined with AND).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching values with their keys.</returns>
    Task<IReadOnlyList<JsonQueryResult<TValue>>> JsonQueryAsync(
        IReadOnlyList<QueryCondition> conditions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query entries using JSON path conditions with pagination.
    /// </summary>
    /// <param name="conditions">Query conditions (combined with AND). Null or empty for all.</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="sortBy">Optional sort specification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged result with matching values.</returns>
    Task<JsonPagedResult<TValue>> JsonQueryPagedAsync(
        IReadOnlyList<QueryCondition>? conditions,
        int offset,
        int limit,
        JsonSortSpec? sortBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Count entries matching JSON path conditions.
    /// </summary>
    /// <param name="conditions">Query conditions (combined with AND). Null or empty for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of matching entries.</returns>
    Task<long> JsonCountAsync(
        IReadOnlyList<QueryCondition>? conditions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get distinct values at a JSON path.
    /// </summary>
    /// <param name="path">JSON path to extract values from.</param>
    /// <param name="conditions">Optional filter conditions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of distinct values at the path.</returns>
    Task<IReadOnlyList<object?>> JsonDistinctAsync(
        string path,
        IReadOnlyList<QueryCondition>? conditions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregate values at a JSON path.
    /// </summary>
    /// <param name="path">JSON path to aggregate (must be numeric for SUM/AVG/MIN/MAX).</param>
    /// <param name="aggregation">Aggregation function to apply.</param>
    /// <param name="conditions">Optional filter conditions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated value.</returns>
    Task<object?> JsonAggregateAsync(
        string path,
        JsonAggregation aggregation,
        IReadOnlyList<QueryCondition>? conditions = null,
        CancellationToken cancellationToken = default);
}
