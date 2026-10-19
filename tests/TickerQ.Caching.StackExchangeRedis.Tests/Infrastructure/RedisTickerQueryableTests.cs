using System.Runtime.CompilerServices;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

public sealed class RedisTickerQueryableTests
{
    [Fact]
    public async Task DeepSortedPage_PreservesTiesPredicateAndExactCount()
    {
        var input = Enumerable.Range(0, 900).Select(i => new Row(i, i % 17)).ToArray();
        var query = Query(input).Where(row => row.Id % 3 != 0).OrderByDescending(row => row.Score);
        var expected = input.Where(row => row.Id % 3 != 0).OrderByDescending(row => row.Score).ToArray();

        var page = await query.ToPaginatedAsync(49, 7);

        Assert.Equal(expected.Length, page.TotalCount);
        Assert.Equal(expected.Skip(48 * 7).Take(7), page.Items);
    }

    [Fact]
    public async Task WindowsAndFilters_KeepTheirOriginalPipelineOrder()
    {
        var input = Enumerable.Range(0, 180).Select(i => new Row(i, i % 17)).ToArray();
        var query = Query(input).Take(150).OrderBy(row => row.Score).Skip(17)
            .Where(row => row.Id % 3 == 0).OrderByDescending(row => row.Id).Take(23);
        var expected = input.Take(150).OrderBy(row => row.Score).Skip(17)
            .Where(row => row.Id % 3 == 0).OrderByDescending(row => row.Id).Take(23).ToArray();

        Assert.Equal(expected, await query.ToArrayAsync());
        Assert.Equal(expected.Length, await query.CountAsync());
        var page = await query.ToPaginatedAsync(2, 7);
        Assert.Equal(expected.Length, page.TotalCount);
        Assert.Equal(expected.Skip(7).Take(7), page.Items);
    }

    [Fact]
    public async Task Count_DoesNotEvaluateSortKeys_AndHonorsExistingWindow()
    {
        var input = Enumerable.Range(0, 300).Select(i => new Row(i, i)).ToArray();
        var query = Query(input).Where(row => row.Id % 2 == 0)
            .OrderBy(row => ForbiddenSortKey(row)).Skip(13).Take(19);

        Assert.Equal(19, await query.CountAsync());
    }

    [Fact]
    public async Task UnsortedFirstAndEmptyTake_StopBeforeReadingRemainingDocuments()
    {
        var examined = 0;
        var query = new RedisTickerQueryable<Row>((_, ct) => Source(ct));
        Assert.Null(await query.Take(0).FirstOrDefaultAsync());
        Assert.Equal(0, examined);
        Assert.Equal(4, (await query.Where(row => row.Id == 4).FirstOrDefaultAsync()).Id);
        Assert.Equal(5, examined);

        async IAsyncEnumerable<Row> Source([EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < 2000; i++)
            {
                ct.ThrowIfCancellationRequested();
                examined++;
                yield return new Row(i, i);
            }
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProjectedSortedPage_DoesNotRetainPayloadsInSelectionWindow()
    {
        var references = new List<WeakReference>();
        var maximumRetained = 0;
        var query = new RedisTickerQueryable<PayloadRow>((_, ct) => Source(ct)).OrderBy(row => row.Id);
        var projected = (ITickerProjectionQueryable<PayloadRow>)query;

        var page = await projected.ToPaginatedAsync(row => row.Id, 2, 11);

        Assert.Equal(1200, page.TotalCount);
        Assert.Equal(Enumerable.Range(11, 11), page.Items);
        Assert.InRange(maximumRetained, 0, 10);

        async IAsyncEnumerable<PayloadRow> Source([EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1199; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                var row = new PayloadRow(i, new byte[16 * 1024]);
                references.Add(new WeakReference(row));
                yield return row;
                if (i % 300 == 0)
                {
                    GC.Collect();
                    maximumRetained = Math.Max(maximumRetained, references.Count(reference => reference.IsAlive));
                }
            }
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Cancellation_IsCheckedDuringScanAndBeforeEmptyQuery()
    {
        using var cancellation = new CancellationTokenSource();
        var query = new RedisTickerQueryable<Row>((_, ct) => Source(ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.OrderBy(row => row.Score)
            .ToPaginatedAsync(1, 10, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.Take(0).ToArrayAsync(cancellation.Token));

        async IAsyncEnumerable<Row> Source([EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < 300; i++)
            {
                if (i == 20) cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
                yield return new Row(i, i);
            }
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task OutOfRangePage_DoesNotOverflowOffsetOrEvaluateSort()
    {
        var page = await Query([new Row(1, 1)]).OrderBy(row => ForbiddenSortKey(row))
            .ToPaginatedAsync(int.MaxValue, 1000);
        Assert.Equal(1, page.TotalCount);
        Assert.Empty(page.Items);
    }

    private static ITickerQueryable<Row> Query(Row[] rows) => new RedisTickerQueryable<Row>((_, ct) => Source(rows, ct));

    private static async IAsyncEnumerable<Row> Source(Row[] rows, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return row;
        }
        await Task.CompletedTask;
    }

    private static int ForbiddenSortKey(Row row) => throw new InvalidOperationException("Counting must not sort.");
    private sealed record Row(int Id, int Score);
    private sealed record PayloadRow(int Id, byte[] Request);
}
