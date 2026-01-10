namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Pagination calculation utilities for history queries.
/// Provides consistent pagination behavior across all history services.
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// Default page size when not specified.
    /// </summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Maximum allowed page size.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Calculates skip and take values for pagination.
    /// Uses 1-based page numbering.
    /// </summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>Tuple of (skip count, take count).</returns>
    public static (int Skip, int Take) CalculatePagination(int page, int pageSize)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (normalizedPage - 1) * normalizedPageSize;
        return (skip, normalizedPageSize);
    }

    /// <summary>
    /// Determines if there is a next page available.
    /// </summary>
    /// <param name="totalCount">Total number of items.</param>
    /// <param name="page">Current page number (1-based).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <returns>True if more pages exist after the current page.</returns>
    public static bool HasNextPage(int totalCount, int page, int pageSize)
        => page * pageSize < totalCount;

    /// <summary>
    /// Determines if there is a previous page available.
    /// </summary>
    /// <param name="page">Current page number (1-based).</param>
    /// <returns>True if current page is greater than 1.</returns>
    public static bool HasPreviousPage(int page)
        => page > 1;

    /// <summary>
    /// Calculates the total number of pages.
    /// </summary>
    /// <param name="totalCount">Total number of items.</param>
    /// <param name="pageSize">Items per page.</param>
    /// <returns>Total number of pages.</returns>
    public static int TotalPages(int totalCount, int pageSize)
        => pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0;

    /// <summary>
    /// Paginates a collection and returns a result with metadata.
    /// </summary>
    /// <typeparam name="T">Type of items in the collection.</typeparam>
    /// <param name="items">The full collection to paginate.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <returns>Pagination result with items and metadata.</returns>
    public static PaginationResult<T> Paginate<T>(IEnumerable<T> items, int page, int pageSize)
    {
        var itemsList = items as IList<T> ?? items.ToList();
        var totalCount = itemsList.Count;
        var (skip, take) = CalculatePagination(page, pageSize);
        var pagedItems = itemsList.Skip(skip).Take(take).ToList();

        return new PaginationResult<T>(
            Items: pagedItems,
            TotalCount: totalCount,
            Page: Math.Max(1, page),
            PageSize: take,
            HasNextPage: HasNextPage(totalCount, page, take),
            HasPreviousPage: HasPreviousPage(page)
        );
    }

    /// <summary>
    /// Paginates a collection with pre-calculated total count.
    /// Use when total count is already known (e.g., from database).
    /// </summary>
    /// <typeparam name="T">Type of items in the collection.</typeparam>
    /// <param name="pagedItems">Already-paginated items.</param>
    /// <param name="totalCount">Total count of all items (before pagination).</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <returns>Pagination result with metadata.</returns>
    public static PaginationResult<T> CreateResult<T>(
        IReadOnlyList<T> pagedItems,
        int totalCount,
        int page,
        int pageSize)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        return new PaginationResult<T>(
            Items: pagedItems,
            TotalCount: totalCount,
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            HasNextPage: HasNextPage(totalCount, normalizedPage, normalizedPageSize),
            HasPreviousPage: HasPreviousPage(normalizedPage)
        );
    }
}

/// <summary>
/// Result of a pagination operation with items and metadata.
/// </summary>
/// <typeparam name="T">Type of items in the result.</typeparam>
/// <param name="Items">The items for the current page.</param>
/// <param name="TotalCount">Total number of items across all pages.</param>
/// <param name="Page">Current page number (1-based).</param>
/// <param name="PageSize">Number of items per page.</param>
/// <param name="HasNextPage">Whether there are more pages after this one.</param>
/// <param name="HasPreviousPage">Whether there are pages before this one.</param>
public record PaginationResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage
)
{
    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => PaginationHelper.TotalPages(TotalCount, PageSize);

    /// <summary>
    /// Creates an empty pagination result.
    /// </summary>
    /// <param name="page">Page number.</param>
    /// <param name="pageSize">Page size.</param>
    /// <returns>Empty pagination result.</returns>
    public static PaginationResult<T> Empty(int page, int pageSize)
        => new(
            Items: Array.Empty<T>(),
            TotalCount: 0,
            Page: Math.Max(1, page),
            PageSize: Math.Clamp(pageSize, 1, PaginationHelper.MaxPageSize),
            HasNextPage: false,
            HasPreviousPage: false
        );
}
