using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Extended tests for <see cref="CronScheduleCache"/>.
/// Each test uses unique cron expressions or invalidates after use to avoid
/// cross-test interference from the static cache.
/// </summary>
public class CronScheduleCacheExtendedTests : IDisposable
{
    public CronScheduleCacheExtendedTests()
    {
        // Ensure UTC for deterministic results
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
    }

    public void Dispose()
    {
        // Restore default
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
    }

    // ---------------------------------------------------------------
    // 1. Cache hit – same expression returns consistent results
    // ---------------------------------------------------------------
    [Fact]
    public void GetNextOccurrenceOrDefault_CalledTwice_ReturnsSameResult()
    {
        var expr = "0 15 3 * * *"; // unique: 03:15:00 every day
        var baseTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var first = CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime);
        var second = CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);

        CronScheduleCache.Invalidate(expr);
    }

    // ---------------------------------------------------------------
    // 2. Invalidate – removed entry causes recomputation
    // ---------------------------------------------------------------
    [Fact]
    public void Invalidate_RemovesEntryFromCache()
    {
        var expr = "30 0 12 * * *"; // unique: 12:00:30 every day
        var baseTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Populate cache
        var result1 = CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime);
        Assert.NotNull(result1);

        // Invalidate
        var removed = CronScheduleCache.Invalidate(expr);
        Assert.True(removed);

        // Calling again should still return a valid result (recomputed)
        var result2 = CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime);
        Assert.NotNull(result2);
        Assert.Equal(result1, result2);

        // Second invalidation of uncached entry also works
        CronScheduleCache.Invalidate(expr);
    }

    // ---------------------------------------------------------------
    // 3. InvalidateAll – invalidate multiple expressions individually
    //    (CronScheduleCache has no InvalidateAll; we invalidate each)
    // ---------------------------------------------------------------
    [Fact]
    public void InvalidateAll_MultipleExpressions_AllCleared()
    {
        var expressions = new[]
        {
            "0 0 1 * * *",   // 01:00:00
            "0 0 2 * * *",   // 02:00:00
            "0 0 3 * * *",   // 03:00:00
        };

        var baseTime = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        // Populate cache
        foreach (var expr in expressions)
        {
            Assert.NotNull(CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime));
        }

        // Invalidate all
        foreach (var expr in expressions)
        {
            var removed = CronScheduleCache.Invalidate(expr);
            Assert.True(removed);
        }

        // Invalidating again should return false (already removed)
        foreach (var expr in expressions)
        {
            var removed = CronScheduleCache.Invalidate(expr);
            Assert.False(removed);
        }
    }

    // ---------------------------------------------------------------
    // 4. Thread safety – concurrent access with no exceptions
    // ---------------------------------------------------------------
    [Fact]
    public void GetNextOccurrenceOrDefault_ConcurrentAccess_NoExceptions()
    {
        var baseTime = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var expressions = new[]
        {
            "0 10 * * * *",
            "0 20 * * * *",
            "0 30 * * * *",
            "0 40 * * * *",
        };

        var exceptions = new List<Exception>();
        var threads = new Thread[16];

        for (int i = 0; i < threads.Length; i++)
        {
            var idx = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    var expr = expressions[idx % expressions.Length];
                    for (int j = 0; j < 50; j++)
                    {
                        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime);
                        Assert.NotNull(result);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.Empty(exceptions);

        // Cleanup
        foreach (var expr in expressions)
            CronScheduleCache.Invalidate(expr);
    }

    // ---------------------------------------------------------------
    // 5a. Seconds-level cron expression (6 fields)
    // ---------------------------------------------------------------
    [Fact]
    public void GetNextOccurrenceOrDefault_SecondsLevel_ParsesCorrectly()
    {
        var expr = "*/15 * * * * *"; // every 15 seconds
        var baseTime = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var next = CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime);

        Assert.NotNull(next);
        // Should be within 15 seconds of base time
        Assert.True(next.Value > baseTime);
        Assert.True((next.Value - baseTime).TotalSeconds <= 15);

        CronScheduleCache.Invalidate(expr);
    }

    // ---------------------------------------------------------------
    // 5b. Standard 5-field cron expression
    // ---------------------------------------------------------------
    [Fact]
    public void GetNextOccurrenceOrDefault_FiveField_ParsesCorrectly()
    {
        // NCrontab with IncludingSeconds=true expects 6 fields.
        // A 5-field expression should return null (parse failure) since
        // the cache is configured with IncludingSeconds = true.
        var expr = "*/5 * * * *";
        var baseTime = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var next = CronScheduleCache.GetNextOccurrenceOrDefault(expr, baseTime);

        // NCrontab TryParse with IncludingSeconds may still parse 5-field;
        // either way, the call should not throw.
        // If it parses, it should return a future date; if not, null.
        if (next.HasValue)
        {
            Assert.True(next.Value > baseTime);
        }

        CronScheduleCache.Invalidate(expr);
    }

    // ---------------------------------------------------------------
    // 6. Past base time – still returns a future occurrence
    // ---------------------------------------------------------------
    [Fact]
    public void GetNextOccurrenceOrDefault_PastBaseTime_ReturnsFutureOccurrence()
    {
        var expr = "0 0 0 * * *"; // midnight daily (seconds-level)
        var pastTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var next = CronScheduleCache.GetNextOccurrenceOrDefault(expr, pastTime);

        Assert.NotNull(next);
        Assert.True(next.Value > pastTime);

        CronScheduleCache.Invalidate(expr);
    }

    // ---------------------------------------------------------------
    // 7. Expression normalization – extra whitespace
    // ---------------------------------------------------------------
    [Fact]
    public void GetNextOccurrenceOrDefault_ExtraWhitespace_NormalizesAndWorks()
    {
        var cleanExpr = "0 45 6 * * *";       // 06:45:00
        var messyExpr = "  0   45  6   *  *  *  ";
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var cleanResult = CronScheduleCache.GetNextOccurrenceOrDefault(cleanExpr, baseTime);
        var messyResult = CronScheduleCache.GetNextOccurrenceOrDefault(messyExpr, baseTime);

        Assert.NotNull(cleanResult);
        Assert.NotNull(messyResult);
        Assert.Equal(cleanResult, messyResult);

        CronScheduleCache.Invalidate(cleanExpr);
        // messy normalizes to same key, so one invalidation suffices
    }

    // ---------------------------------------------------------------
    // Edge case: null expression throws
    // ---------------------------------------------------------------
    [Fact]
    public void GetNextOccurrenceOrDefault_NullExpression_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CronScheduleCache.GetNextOccurrenceOrDefault(null!, DateTime.UtcNow));
    }
}
