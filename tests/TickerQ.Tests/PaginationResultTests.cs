using FluentAssertions;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class PaginationResultTests
{
    [Fact]
    public void DefaultConstructor_InitializesEmptyItems()
    {
        var result = new PaginationResult<string>();

        result.Items.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.PageNumber.Should().Be(0);
        result.PageSize.Should().Be(0);
    }

    [Fact]
    public void ParameterizedConstructor_SetsAllProperties()
    {
        var items = new[] { "a", "b", "c" };
        var result = new PaginationResult<string>(items, totalCount: 10, pageNumber: 2, pageSize: 3);

        result.Items.Should().BeEquivalentTo(items);
        result.TotalCount.Should().Be(10);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public void ParameterizedConstructor_HandlesNullItems()
    {
        var result = new PaginationResult<string>(null, totalCount: 5, pageNumber: 1, pageSize: 5);

        result.Items.Should().NotBeNull();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public void TotalPages_CalculatesCorrectly()
    {
        var result = new PaginationResult<string>
        {
            TotalCount = 25,
            PageSize = 10
        };

        result.TotalPages.Should().Be(3); // ceil(25/10)
    }

    [Fact]
    public void TotalPages_ReturnsOne_WhenItemsFitInOnePage()
    {
        var result = new PaginationResult<string>
        {
            TotalCount = 5,
            PageSize = 10
        };

        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public void TotalPages_ReturnsExact_WhenEvenlySplit()
    {
        var result = new PaginationResult<string>
        {
            TotalCount = 20,
            PageSize = 10
        };

        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public void HasPreviousPage_ReturnsFalse_OnFirstPage()
    {
        var result = new PaginationResult<string> { PageNumber = 1 };

        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_ReturnsTrue_OnLaterPages()
    {
        var result = new PaginationResult<string> { PageNumber = 2 };

        result.HasPreviousPage.Should().BeTrue();
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

        result.HasNextPage.Should().BeTrue();
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

        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void FirstItemIndex_CalculatesCorrectly()
    {
        var result = new PaginationResult<string>
        {
            PageNumber = 3,
            PageSize = 10
        };

        result.FirstItemIndex.Should().Be(21); // (3-1)*10 + 1
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

        result.LastItemIndex.Should().Be(25); // Min(30, 25)
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

        result.LastItemIndex.Should().Be(20);
    }
}
