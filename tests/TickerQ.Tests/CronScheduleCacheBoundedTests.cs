using TickerQ.Utilities;

namespace TickerQ.Tests;

public sealed class CronScheduleCacheBoundedTests
{
    [Fact]
    public void LeastRecentlyUsedEntryIsEvicted_AndReparsedWithTheSameSchedule()
    {
        var cache = new CronScheduleCache.BoundedScheduleCache(2, 1024);
        var first = cache.Get("0 1 * * * *");
        var second = cache.Get("0 2 * * * *");
        Assert.Same(first, cache.Get("0 1 * * * *")); // First is hot; second is the eviction victim.
        cache.Get("0 3 * * * *");
        Assert.Same(first, cache.Get("0 1 * * * *"));
        var reloaded = cache.Get("0 2 * * * *");
        Assert.NotSame(second, reloaded);
        var now = new DateTime(2026, 1, 1);
        Assert.Equal(second.GetNextOccurrence(now), reloaded.GetNextOccurrence(now));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void InvalidAndOversizedExpressions_AreNotRetained()
    {
        var cache = new CronScheduleCache.BoundedScheduleCache(2, 32);
        var hot = cache.Get("0 * * * * *");
        for (var i = 0; i < 100; i++) Assert.Null(cache.Get($"invalid-{i}"));
        Assert.NotNull(cache.Get(string.Join(",", Enumerable.Repeat("0", 40)) + " * * * * *"));
        Assert.Same(hot, cache.Get("0 * * * * *"));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ConcurrentInsertsAndInvalidations_StayBounded()
    {
        var cache = new CronScheduleCache.BoundedScheduleCache(16, 1024);
        Parallel.For(0, 10000, i =>
        {
            var expression = $"{i % 60} {(i / 60) % 60} * * * *";
            Assert.NotNull(cache.Get(expression));
            if (i % 3 == 0) cache.Invalidate(expression);
            Assert.InRange(cache.Count, 0, 16);
        });
    }
}
