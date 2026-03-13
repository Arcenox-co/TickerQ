using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class PaginationResultTests
{
    [Fact]
    public void DefaultConstructor_InitializesEmptyItems()
    {
        var result = new PaginationResult<string>();

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.PageNumber);
        Assert.Equal(0, result.PageSize);
    }

    [Fact]
    public void ParameterizedConstructor_SetsAllProperties()
    {
        var items = new[] { "a", "b", "c" };
        var result = new PaginationResult<string>(items, totalCount: 10, pageNumber: 2, pageSize: 3);

        Assert.Equal(items, result.Items);
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(3, result.PageSize);
    }

    [Fact]
    public void ParameterizedConstructor_HandlesNullItems()
    {
        var result = new PaginationResult<string>(null, totalCount: 5, pageNumber: 1, pageSize: 5);

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void TotalPages_CalculatesCorrectly()
    {
        var result = new PaginationResult<string>
        {
            TotalCount = 25,
            PageSize = 10
        };

        Assert.Equal(3, result.TotalPages); // ceil(25/10)
    }

    [Fact]
    public void TotalPages_ReturnsOne_WhenItemsFitInOnePage()
    {
        var result = new PaginationResult<string>
        {
            TotalCount = 5,
            PageSize = 10
        };

        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public void TotalPages_ReturnsExact_WhenEvenlySplit()
    {
        var result = new PaginationResult<string>
        {
            TotalCount = 20,
            PageSize = 10
        };

        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void HasPreviousPage_ReturnsFalse_OnFirstPage()
    {
        var result = new PaginationResult<string> { PageNumber = 1 };

        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void HasPreviousPage_ReturnsTrue_OnLaterPages()
    {
        var result = new PaginationResult<string> { PageNumber = 2 };

        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void HasNextPage_ReturnsTrue_WhenMorePagesExist()
    {
        var result = new PaginationResult<string>
        {
            PageNumber = 1,
            TotalCount = 20,
            PageSize = 10
        };

        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_ReturnsFalse_OnLastPage()
    {
        var result = new PaginationResult<string>
        {
            PageNumber = 2,
            TotalCount = 20,
            PageSize = 10
        };

        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void FirstItemIndex_CalculatesCorrectly()
    {
        var result = new PaginationResult<string>
        {
            PageNumber = 3,
            PageSize = 10
        };

        Assert.Equal(21, result.FirstItemIndex); // (3-1)*10 + 1
    }

    [Fact]
    public void LastItemIndex_CappedByTotalCount()
    {
        var result = new PaginationResult<string>
        {
            PageNumber = 3,
            PageSize = 10,
            TotalCount = 25
        };

        Assert.Equal(25, result.LastItemIndex); // Min(30, 25)
    }

    [Fact]
    public void LastItemIndex_EqualsPageEnd_WhenFullPage()
    {
        var result = new PaginationResult<string>
        {
            PageNumber = 2,
            PageSize = 10,
            TotalCount = 30
        };

        Assert.Equal(20, result.LastItemIndex);
    }
}
