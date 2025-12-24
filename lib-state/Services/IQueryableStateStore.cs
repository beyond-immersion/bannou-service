#nullable enable

using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Paged result for query operations.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>
    /// Whether there are more pages available.
    /// </summary>
    public bool HasMore => (Page + 1) * PageSize < TotalCount;

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

/// <summary>
/// Queryable state store - extends IStateStore for MySQL backends.
/// Redis stores do NOT implement this interface.
/// </summary>
/// <typeparam name="TValue">Value type stored</typeparam>
public interface IQueryableStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    /// <summary>
    /// Query with LINQ expression.
    /// </summary>
    /// <param name="predicate">Filter predicate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching values.</returns>
    Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query with pagination.
    /// </summary>
    /// <param name="predicate">Filter predicate (null for all).</param>
    /// <param name="page">Page number (0-indexed).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <param name="orderBy">Sort expression (null for default order).</param>
    /// <param name="descending">Whether to sort descending.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged result with items and metadata.</returns>
    Task<PagedResult<TValue>> QueryPagedAsync(
        Expression<Func<TValue, bool>>? predicate,
        int page,
        int pageSize,
        Expression<Func<TValue, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Count matching entries.
    /// </summary>
    /// <param name="predicate">Filter predicate (null for all).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of matching entries.</returns>
    Task<long> CountAsync(
        Expression<Func<TValue, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
}
