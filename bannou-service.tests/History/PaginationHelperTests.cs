using BeyondImmersion.BannouService.History;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests.History;

/// <summary>
/// Unit tests for PaginationHelper.
/// </summary>
[Collection("unit tests")]
public class PaginationHelperTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public PaginationHelperTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PaginationHelperTests>.Instance;
    }

    [Fact]
    public void CalculatePagination_Page1_ReturnsZeroSkip()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(1, 10);
        Assert.Equal(0, skip);
        Assert.Equal(10, take);
    }

    [Fact]
    public void CalculatePagination_Page2_ReturnsCorrectSkip()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(2, 10);
        Assert.Equal(10, skip);
        Assert.Equal(10, take);
    }

    [Fact]
    public void CalculatePagination_Page3_ReturnsCorrectSkip()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(3, 20);
        Assert.Equal(40, skip);
        Assert.Equal(20, take);
    }

    [Fact]
    public void CalculatePagination_NegativePage_NormalizesToPage1()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(-1, 10);
        Assert.Equal(0, skip);
        Assert.Equal(10, take);
    }

    [Fact]
    public void CalculatePagination_ZeroPage_NormalizesToPage1()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(0, 10);
        Assert.Equal(0, skip);
        Assert.Equal(10, take);
    }

    [Fact]
    public void CalculatePagination_PageSizeAboveMax_ClampsToMax()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(1, 200);
        Assert.Equal(0, skip);
        Assert.Equal(PaginationHelper.MaxPageSize, take);
    }

    [Fact]
    public void CalculatePagination_PageSizeBelowMin_ClampsToMin()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(1, 0);
        Assert.Equal(0, skip);
        Assert.Equal(1, take);
    }

    [Fact]
    public void CalculatePagination_NegativePageSize_ClampsToMin()
    {
        var (skip, take) = PaginationHelper.CalculatePagination(1, -5);
        Assert.Equal(0, skip);
        Assert.Equal(1, take);
    }

    [Fact]
    public void HasNextPage_MoreItemsAvailable_ReturnsTrue()
    {
        Assert.True(PaginationHelper.HasNextPage(totalCount: 25, page: 1, pageSize: 10));
    }

    [Fact]
    public void HasNextPage_ExactlyOnePage_ReturnsFalse()
    {
        Assert.False(PaginationHelper.HasNextPage(totalCount: 10, page: 1, pageSize: 10));
    }

    [Fact]
    public void HasNextPage_OnLastPage_ReturnsFalse()
    {
        Assert.False(PaginationHelper.HasNextPage(totalCount: 25, page: 3, pageSize: 10));
    }

    [Fact]
    public void HasNextPage_EmptyCollection_ReturnsFalse()
    {
        Assert.False(PaginationHelper.HasNextPage(totalCount: 0, page: 1, pageSize: 10));
    }

    [Fact]
    public void HasPreviousPage_Page1_ReturnsFalse()
    {
        Assert.False(PaginationHelper.HasPreviousPage(1));
    }

    [Fact]
    public void HasPreviousPage_Page2_ReturnsTrue()
    {
        Assert.True(PaginationHelper.HasPreviousPage(2));
    }

    [Fact]
    public void HasPreviousPage_ZeroPage_ReturnsFalse()
    {
        Assert.False(PaginationHelper.HasPreviousPage(0));
    }

    [Fact]
    public void TotalPages_ExactlyDivisible_ReturnsCorrectCount()
    {
        Assert.Equal(5, PaginationHelper.TotalPages(totalCount: 50, pageSize: 10));
    }

    [Fact]
    public void TotalPages_NotDivisible_RoundsUp()
    {
        Assert.Equal(6, PaginationHelper.TotalPages(totalCount: 55, pageSize: 10));
    }

    [Fact]
    public void TotalPages_EmptyCollection_ReturnsZero()
    {
        Assert.Equal(0, PaginationHelper.TotalPages(totalCount: 0, pageSize: 10));
    }

    [Fact]
    public void TotalPages_ZeroPageSize_ReturnsZero()
    {
        Assert.Equal(0, PaginationHelper.TotalPages(totalCount: 50, pageSize: 0));
    }

    [Fact]
    public void TotalPages_SingleItem_ReturnsOne()
    {
        Assert.Equal(1, PaginationHelper.TotalPages(totalCount: 1, pageSize: 10));
    }

    [Fact]
    public void Paginate_ReturnsCorrectItemsForPage1()
    {
        var items = Enumerable.Range(1, 30).ToList();
        var result = PaginationHelper.Paginate(items, page: 1, pageSize: 10);

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(1, result.Items[0]);
        Assert.Equal(10, result.Items[9]);
        Assert.Equal(30, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.True(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void Paginate_ReturnsCorrectItemsForPage2()
    {
        var items = Enumerable.Range(1, 30).ToList();
        var result = PaginationHelper.Paginate(items, page: 2, pageSize: 10);

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(11, result.Items[0]);
        Assert.Equal(20, result.Items[9]);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void Paginate_ReturnsCorrectItemsForLastPage()
    {
        var items = Enumerable.Range(1, 25).ToList();
        var result = PaginationHelper.Paginate(items, page: 3, pageSize: 10);

        Assert.Equal(5, result.Items.Count);
        Assert.Equal(21, result.Items[0]);
        Assert.Equal(25, result.Items[4]);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void Paginate_EmptyCollection_ReturnsEmptyResult()
    {
        var items = new List<int>();
        var result = PaginationHelper.Paginate(items, page: 1, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void Paginate_PageBeyondRange_ReturnsEmptyItems()
    {
        var items = Enumerable.Range(1, 10).ToList();
        var result = PaginationHelper.Paginate(items, page: 5, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(10, result.TotalCount);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void Paginate_NormalizesInvalidPage()
    {
        var items = Enumerable.Range(1, 10).ToList();
        var result = PaginationHelper.Paginate(items, page: -1, pageSize: 10);

        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public void CreateResult_BuildsCorrectMetadata()
    {
        var pagedItems = new List<string> { "a", "b", "c" };
        var result = PaginationHelper.CreateResult(pagedItems, totalCount: 15, page: 2, pageSize: 5);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(15, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public void CreateResult_NormalizesInvalidInputs()
    {
        var pagedItems = new List<string> { "a" };
        var result = PaginationHelper.CreateResult(pagedItems, totalCount: 1, page: -1, pageSize: 200);

        Assert.Equal(1, result.Page);
        Assert.Equal(PaginationHelper.MaxPageSize, result.PageSize);
    }

    [Fact]
    public void PaginationResult_Empty_CreatesEmptyResult()
    {
        var result = PaginationResult<int>.Empty(1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.False(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void PaginationResult_Empty_NormalizesInputs()
    {
        var result = PaginationResult<int>.Empty(-1, 200);

        Assert.Equal(1, result.Page);
        Assert.Equal(PaginationHelper.MaxPageSize, result.PageSize);
    }

    [Fact]
    public void DefaultPageSize_Is20()
    {
        Assert.Equal(20, PaginationHelper.DefaultPageSize);
    }

    [Fact]
    public void MaxPageSize_Is100()
    {
        Assert.Equal(100, PaginationHelper.MaxPageSize);
    }
}
