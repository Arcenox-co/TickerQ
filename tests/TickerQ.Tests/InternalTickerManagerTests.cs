using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class InternalTickerManagerTests
{
    public class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public class FakeCronTicker : CronTickerEntity { }

    private readonly ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker> _persistence;
    private readonly ITickerClock _clock;
    private readonly ITickerQNotificationHubSender _notificationHub;
    private readonly IInternalTickerManager _manager;
    private readonly DateTime _now;

    public InternalTickerManagerTests()
    {
        _now = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        _persistence = Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>();
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);
        _notificationHub = Substitute.For<ITickerQNotificationHubSender>();

        // Default stubs: return empty by default so tests only configure what they need
        _persistence.GetAllCronTickerExpressions(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<CronTickerEntity>()));
        _persistence.GetEarliestAvailableCronOccurrence(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CronTickerOccurrenceEntity<FakeCronTicker>>(null));
        _persistence.GetEarliestTimeTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<TimeTickerEntity>()));

        _notificationHub.UpdateTimeTickerNotifyAsync(Arg.Any<object>()).Returns(Task.CompletedTask);
        _notificationHub.AddCronOccurrenceAsync(Arg.Any<Guid>(), Arg.Any<object>()).Returns(Task.CompletedTask);
        _notificationHub.UpdateCronOccurrenceAsync(Arg.Any<Guid>(), Arg.Any<object>()).Returns(Task.CompletedTask);
        _notificationHub.UpdateTimeTickerFromInternalFunctionContext<FakeTimeTicker>(Arg.Any<InternalFunctionContext>()).Returns(Task.CompletedTask);
        _notificationHub.UpdateCronOccurrenceFromInternalFunctionContext<FakeCronTicker>(Arg.Any<InternalFunctionContext>()).Returns(Task.CompletedTask);

        // Create the manager via reflection since it is internal
        var managerType = typeof(IInternalTickerManager).Assembly
            .GetType("TickerQ.Utilities.Managers.InternalTickerManager`2")!
            .MakeGenericType(typeof(FakeTimeTicker), typeof(FakeCronTicker));

        _manager = (IInternalTickerManager)Activator.CreateInstance(
            managerType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { _persistence, _clock, _notificationHub },
            culture: null)!;
    }

    #region Helper: invoke private static EarliestCronTickerGroup via reflection

    private static (DateTime Next, InternalManagerContext[] Items)? InvokeEarliestCronTickerGroup(
        CronTickerEntity[] cronTickers,
        DateTime now,
        CronTickerOccurrenceEntity<FakeCronTicker> earliestStored)
    {
        var managerType = typeof(IInternalTickerManager).Assembly
            .GetType("TickerQ.Utilities.Managers.InternalTickerManager`2")!
            .MakeGenericType(typeof(FakeTimeTicker), typeof(FakeCronTicker));

        var method = managerType.GetMethod("EarliestCronTickerGroup",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { cronTickers, now, earliestStored });
        if (result is null) return null;

        // Result is a ValueTuple<DateTime, InternalManagerContext[]>
        var tuple = ((DateTime, InternalManagerContext[]))result;
        return (tuple.Item1, tuple.Item2);
    }

    #endregion

    #region Helper: create async enumerables for NSubstitute

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    #endregion

    #region Helper: CronTickerEntity factory

    private static CronTickerEntity MakeCron(Guid id, string function, string expression, int retries = 0, int[] retryIntervals = null)
    {
        return new CronTickerEntity
        {
            Id = id,
            Function = function,
            Expression = expression,
            Retries = retries,
            RetryIntervals = retryIntervals ?? Array.Empty<int>()
        };
    }

    #endregion

    // ====================================================================
    // 1. EarliestCronTickerGroup (via reflection)
    // ====================================================================

    [Fact]
    public void EarliestCronTickerGroup_NoCronTickers_ReturnsNull()
    {
        var result = InvokeEarliestCronTickerGroup(Array.Empty<CronTickerEntity>(), _now, null);
        Assert.Null(result);
    }

    [Fact]
    public void EarliestCronTickerGroup_SingleCronTicker_ReturnsItsNextOccurrence()
    {
        // "0 0 13 * * *" => every day at 13:00:00 UTC (when timezone is UTC)
        var id = Guid.NewGuid();
        var cron = MakeCron(id, "MyFunc", "0 0 13 * * *");

        // Force UTC timezone for deterministic test
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var result = InvokeEarliestCronTickerGroup(new[] { cron }, _now, null);

            Assert.NotNull(result);
            var (next, items) = result!.Value;
            Assert.Single(items);
            Assert.Equal(id, items[0].Id);
            Assert.Equal("MyFunc", items[0].FunctionName);
            Assert.Equal(new DateTime(2025, 6, 15, 13, 0, 0, DateTimeKind.Utc), next);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public void EarliestCronTickerGroup_MultipleCronTickers_ReturnsEarliestGroup()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var idEarly = Guid.NewGuid();
            var idLate = Guid.NewGuid();
            // 12:30 today vs 13:00 today
            var early = MakeCron(idEarly, "EarlyFunc", "0 30 12 * * *");
            var late = MakeCron(idLate, "LateFunc", "0 0 13 * * *");

            var result = InvokeEarliestCronTickerGroup(new[] { late, early }, _now, null);

            Assert.NotNull(result);
            var (next, items) = result!.Value;
            Assert.Single(items);
            Assert.Equal(idEarly, items[0].Id);
            Assert.Equal(new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc), next);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public void EarliestCronTickerGroup_TiedCronTickers_ReturnsAllTied()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            // Both fire at exactly the same second-resolution time
            var cron1 = MakeCron(id1, "Func1", "0 30 12 * * *");
            var cron2 = MakeCron(id2, "Func2", "0 30 12 * * *");

            var result = InvokeEarliestCronTickerGroup(new[] { cron1, cron2 }, _now, null);

            Assert.NotNull(result);
            var (next, items) = result!.Value;
            Assert.Equal(2, items.Length);
            Assert.Contains(items, i => i.Id == id1);
            Assert.Contains(items, i => i.Id == id2);
            Assert.Equal(new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc), next);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public void EarliestCronTickerGroup_StoredOccurrenceEarlierThanCalculated_ReturnsStored()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var cronId = Guid.NewGuid();
            var storedCronId = Guid.NewGuid();
            // Calculated next occurrence at 12:30
            var cron = MakeCron(cronId, "CalcFunc", "0 30 12 * * *");

            // Stored occurrence at 12:10 (earlier)
            var storedOccurrenceId = Guid.NewGuid();
            var storedCreatedAt = _now.AddMinutes(-5);
            var stored = new CronTickerOccurrenceEntity<FakeCronTicker>
            {
                Id = storedOccurrenceId,
                CronTickerId = storedCronId,
                ExecutionTime = new DateTime(2025, 6, 15, 12, 10, 0, DateTimeKind.Utc),
                CreatedAt = storedCreatedAt,
                CronTicker = new FakeCronTicker
                {
                    Id = storedCronId,
                    Function = "StoredFunc",
                    Expression = "0 10 12 * * *",
                    Retries = 2,
                    RetryIntervals = new[] { 1000 }
                }
            };

            var result = InvokeEarliestCronTickerGroup(new[] { cron }, _now, stored);

            Assert.NotNull(result);
            var (next, items) = result!.Value;
            Assert.Single(items);
            Assert.Equal(new DateTime(2025, 6, 15, 12, 10, 0, DateTimeKind.Utc), next);
            Assert.Equal(storedCronId, items[0].Id);
            Assert.Equal("StoredFunc", items[0].FunctionName);
            Assert.NotNull(items[0].NextCronOccurrence);
            Assert.Equal(storedOccurrenceId, items[0].NextCronOccurrence.Id);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public void EarliestCronTickerGroup_StoredOccurrenceSameTimeAsCalculated_MergesBoth()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var cronId = Guid.NewGuid();
            var storedCronId = Guid.NewGuid();
            var targetTime = new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc);

            // Calculated fires at 12:30
            var cron = MakeCron(cronId, "CalcFunc", "0 30 12 * * *");

            // Stored occurrence also at 12:30 but for a DIFFERENT cron ticker
            var storedOccurrenceId = Guid.NewGuid();
            var stored = new CronTickerOccurrenceEntity<FakeCronTicker>
            {
                Id = storedOccurrenceId,
                CronTickerId = storedCronId,
                ExecutionTime = targetTime,
                CreatedAt = _now.AddMinutes(-5),
                CronTicker = new FakeCronTicker
                {
                    Id = storedCronId,
                    Function = "StoredFunc",
                    Expression = "0 30 12 * * *",
                    Retries = 0,
                    RetryIntervals = Array.Empty<int>()
                }
            };

            var result = InvokeEarliestCronTickerGroup(new[] { cron }, _now, stored);

            Assert.NotNull(result);
            var (next, items) = result!.Value;
            Assert.Equal(targetTime, next);
            Assert.Equal(2, items.Length);
            Assert.Contains(items, i => i.Id == cronId && i.NextCronOccurrence == null);
            Assert.Contains(items, i => i.Id == storedCronId && i.NextCronOccurrence != null);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public void EarliestCronTickerGroup_StoredOccurrenceLaterThanCalculated_ReturnsCalculatedOnly()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var cronId = Guid.NewGuid();
            var storedCronId = Guid.NewGuid();

            // Calculated fires at 12:30
            var cron = MakeCron(cronId, "CalcFunc", "0 30 12 * * *");

            // Stored occurrence at 14:00 (later)
            var stored = new CronTickerOccurrenceEntity<FakeCronTicker>
            {
                Id = Guid.NewGuid(),
                CronTickerId = storedCronId,
                ExecutionTime = new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Utc),
                CreatedAt = _now.AddMinutes(-5),
                CronTicker = new FakeCronTicker
                {
                    Id = storedCronId,
                    Function = "StoredFunc",
                    Expression = "0 0 14 * * *",
                    Retries = 0,
                    RetryIntervals = Array.Empty<int>()
                }
            };

            var result = InvokeEarliestCronTickerGroup(new[] { cron }, _now, stored);

            Assert.NotNull(result);
            var (next, items) = result!.Value;
            Assert.Equal(new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc), next);
            Assert.Single(items);
            Assert.Equal(cronId, items[0].Id);
            Assert.Null(items[0].NextCronOccurrence);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public void EarliestCronTickerGroup_SkipsCronTickerWhoseNextMatchesStored_Dedup()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var cronId = Guid.NewGuid();
            var targetTime = new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc);

            // Cron ticker fires at 12:30
            var cron = MakeCron(cronId, "MyFunc", "0 30 12 * * *");

            // Stored occurrence for the SAME cron ticker at the SAME time => dedup
            var stored = new CronTickerOccurrenceEntity<FakeCronTicker>
            {
                Id = Guid.NewGuid(),
                CronTickerId = cronId,
                ExecutionTime = targetTime,
                CreatedAt = _now.AddMinutes(-5),
                CronTicker = new FakeCronTicker
                {
                    Id = cronId,
                    Function = "MyFunc",
                    Expression = "0 30 12 * * *",
                    Retries = 0,
                    RetryIntervals = Array.Empty<int>()
                }
            };

            var result = InvokeEarliestCronTickerGroup(new[] { cron }, _now, stored);

            // The calculated occurrence is skipped because it duplicates the stored one.
            // Only the stored item is returned.
            Assert.NotNull(result);
            var (next, items) = result!.Value;
            Assert.Equal(targetTime, next);
            Assert.Single(items);
            Assert.Equal(cronId, items[0].Id);
            Assert.NotNull(items[0].NextCronOccurrence);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public void EarliestCronTickerGroup_NoStoredAndNoCronTickers_ReturnsNull()
    {
        var result = InvokeEarliestCronTickerGroup(Array.Empty<CronTickerEntity>(), _now, null);
        Assert.Null(result);
    }

    // ====================================================================
    // 2. GetNextTickers
    // ====================================================================

    [Fact]
    public async Task GetNextTickers_NoTickersAvailable_ReturnsInfiniteTimeSpanAndEmptyArray()
    {
        var (timeRemaining, functions) = await _manager.GetNextTickers(CancellationToken.None);

        Assert.Equal(Timeout.InfiniteTimeSpan, timeRemaining);
        Assert.Empty(functions);
    }

    [Fact]
    public async Task GetNextTickers_TimeTickersAvailable_ReturnsThemWithCorrectDelay()
    {
        var tickerId = Guid.NewGuid();
        var executionTime = _now.AddMinutes(5);
        var timeTicker = new TimeTickerEntity
        {
            Id = tickerId,
            Function = "TimeFunc",
            ExecutionTime = executionTime,
            Retries = 1,
            RetryIntervals = new[] { 500 },
            Children = new List<TimeTickerEntity>()
        };

        _persistence.GetEarliestTimeTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TimeTickerEntity[] { timeTicker }));

        _persistence.QueueTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new[] { timeTicker }));

        var (timeRemaining, functions) = await _manager.GetNextTickers(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(5), timeRemaining);
        Assert.Single(functions);
        Assert.Equal(tickerId, functions[0].TickerId);
        Assert.Equal("TimeFunc", functions[0].FunctionName);
        Assert.Equal(TickerType.TimeTicker, functions[0].Type);
        Assert.Equal(executionTime, functions[0].ExecutionTime);
    }

    [Fact]
    public async Task GetNextTickers_CronOccurrencesAvailable_QueuesAndReturnsThem()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var cronId = Guid.NewGuid();
            var occurrenceId = Guid.NewGuid();
            var nextTime = new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc);

            _persistence.GetAllCronTickerExpressions(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new CronTickerEntity[]
                {
                    MakeCron(cronId, "CronFunc", "0 30 12 * * *", retries: 2)
                }));

            var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
            {
                Id = occurrenceId,
                CronTickerId = cronId,
                ExecutionTime = nextTime,
                CreatedAt = _now,
                UpdatedAt = _now,
                CronTicker = new FakeCronTicker
                {
                    Id = cronId,
                    Function = "CronFunc",
                    Expression = "0 30 12 * * *",
                    Retries = 2,
                    RetryIntervals = Array.Empty<int>()
                }
            };

            _persistence.QueueCronTickerOccurrences(
                    Arg.Any<(DateTime Key, InternalManagerContext[] Items)>(),
                    Arg.Any<CancellationToken>())
                .Returns(ToAsyncEnumerable(new[] { occurrence }));

            var (timeRemaining, functions) = await _manager.GetNextTickers(CancellationToken.None);

            Assert.True(timeRemaining >= TimeSpan.Zero);
            Assert.True(timeRemaining <= TimeSpan.FromMinutes(31));
            Assert.Single(functions);
            Assert.Equal(occurrenceId, functions[0].TickerId);
            Assert.Equal("CronFunc", functions[0].FunctionName);
            Assert.Equal(TickerType.CronTickerOccurrence, functions[0].Type);
            Assert.Equal(cronId, functions[0].ParentId);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public async Task GetNextTickers_BothTimeAndCron_SameSecond_ReturnsBothMerged()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            var targetTime = new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc);

            // Time ticker at 12:30:00
            var timeTickerId = Guid.NewGuid();
            var timeTicker = new TimeTickerEntity
            {
                Id = timeTickerId,
                Function = "TimeFunc",
                ExecutionTime = targetTime,
                Retries = 0,
                RetryIntervals = Array.Empty<int>(),
                Children = new List<TimeTickerEntity>()
            };

            _persistence.GetEarliestTimeTickers(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new TimeTickerEntity[] { timeTicker }));
            _persistence.QueueTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
                .Returns(ToAsyncEnumerable(new[] { timeTicker }));

            // Cron ticker also at 12:30:00
            var cronId = Guid.NewGuid();
            var occurrenceId = Guid.NewGuid();

            _persistence.GetAllCronTickerExpressions(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new CronTickerEntity[]
                {
                    MakeCron(cronId, "CronFunc", "0 30 12 * * *")
                }));

            var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
            {
                Id = occurrenceId,
                CronTickerId = cronId,
                ExecutionTime = targetTime,
                CreatedAt = _now,
                UpdatedAt = _now,
                CronTicker = new FakeCronTicker
                {
                    Id = cronId,
                    Function = "CronFunc",
                    Expression = "0 30 12 * * *",
                    Retries = 0,
                    RetryIntervals = Array.Empty<int>()
                }
            };

            _persistence.QueueCronTickerOccurrences(
                    Arg.Any<(DateTime Key, InternalManagerContext[] Items)>(),
                    Arg.Any<CancellationToken>())
                .Returns(ToAsyncEnumerable(new[] { occurrence }));

            var (timeRemaining, functions) = await _manager.GetNextTickers(CancellationToken.None);

            Assert.True(timeRemaining >= TimeSpan.Zero);
            Assert.Equal(2, functions.Length);
            Assert.Contains(functions, f => f.TickerId == timeTickerId && f.Type == TickerType.TimeTicker);
            Assert.Contains(functions, f => f.TickerId == occurrenceId && f.Type == TickerType.CronTickerOccurrence);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public async Task GetNextTickers_TimeTickerEarlierThanCron_ReturnsOnlyTimeTickers()
    {
        var origTz = CronScheduleCache.TimeZoneInfo;
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;
        try
        {
            // Time ticker at 12:05
            var timeTickerId = Guid.NewGuid();
            var timeTicker = new TimeTickerEntity
            {
                Id = timeTickerId,
                Function = "TimeFunc",
                ExecutionTime = _now.AddMinutes(5),
                Retries = 0,
                RetryIntervals = Array.Empty<int>(),
                Children = new List<TimeTickerEntity>()
            };

            _persistence.GetEarliestTimeTickers(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new TimeTickerEntity[] { timeTicker }));
            _persistence.QueueTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
                .Returns(ToAsyncEnumerable(new[] { timeTicker }));

            // Cron ticker at 12:30
            var cronId = Guid.NewGuid();
            _persistence.GetAllCronTickerExpressions(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new CronTickerEntity[]
                {
                    MakeCron(cronId, "CronFunc", "0 30 12 * * *")
                }));

            var (timeRemaining, functions) = await _manager.GetNextTickers(CancellationToken.None);

            Assert.Equal(TimeSpan.FromMinutes(5), timeRemaining);
            Assert.Single(functions);
            Assert.Equal(timeTickerId, functions[0].TickerId);
            Assert.Equal(TickerType.TimeTicker, functions[0].Type);
        }
        finally
        {
            CronScheduleCache.TimeZoneInfo = origTz;
        }
    }

    [Fact]
    public async Task GetNextTickers_TimeRemainingIsZero_WhenExecutionTimeInPast()
    {
        var pastTime = _now.AddMinutes(-1);
        var timeTickerId = Guid.NewGuid();
        var timeTicker = new TimeTickerEntity
        {
            Id = timeTickerId,
            Function = "PastFunc",
            ExecutionTime = pastTime,
            Retries = 0,
            RetryIntervals = Array.Empty<int>(),
            Children = new List<TimeTickerEntity>()
        };

        _persistence.GetEarliestTimeTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TimeTickerEntity[] { timeTicker }));
        _persistence.QueueTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new[] { timeTicker }));

        var (timeRemaining, functions) = await _manager.GetNextTickers(CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, timeRemaining);
        Assert.Single(functions);
    }

    // ====================================================================
    // 3. SetTickersInProgress
    // ====================================================================

    [Fact]
    public async Task SetTickersInProgress_SetsStatusToInProgress_OnPersistenceProvider()
    {
        var timeTickerId = Guid.NewGuid();
        var resources = new[]
        {
            new InternalFunctionContext
            {
                TickerId = timeTickerId,
                Type = TickerType.TimeTicker,
                FunctionName = "Func1"
            }
        };

        _persistence.UpdateTimeTickersWithUnifiedContext(
                Arg.Any<Guid[]>(),
                Arg.Any<InternalFunctionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _manager.SetTickersInProgress(resources, CancellationToken.None);

        await _persistence.Received(1).UpdateTimeTickersWithUnifiedContext(
            Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == timeTickerId),
            Arg.Is<InternalFunctionContext>(ctx => ctx.Status == TickerStatus.InProgress),
            Arg.Any<CancellationToken>());

        Assert.Equal(TickerStatus.InProgress, resources[0].Status);
    }

    [Fact]
    public async Task SetTickersInProgress_HandlesMixOfTimeTickersAndCronOccurrences()
    {
        var timeTickerId = Guid.NewGuid();
        var cronOccurrenceId = Guid.NewGuid();
        var resources = new[]
        {
            new InternalFunctionContext
            {
                TickerId = timeTickerId,
                Type = TickerType.TimeTicker,
                FunctionName = "TimeFunc"
            },
            new InternalFunctionContext
            {
                TickerId = cronOccurrenceId,
                Type = TickerType.CronTickerOccurrence,
                FunctionName = "CronFunc"
            }
        };

        _persistence.UpdateTimeTickersWithUnifiedContext(
                Arg.Any<Guid[]>(), Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _persistence.UpdateCronTickerOccurrencesWithUnifiedContext(
                Arg.Any<Guid[]>(), Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _manager.SetTickersInProgress(resources, CancellationToken.None);

        // Both persistence calls should have been made (the WhenAll branch)
        await _persistence.Received(1).UpdateTimeTickersWithUnifiedContext(
            Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == timeTickerId),
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<CancellationToken>());
        await _persistence.Received(1).UpdateCronTickerOccurrencesWithUnifiedContext(
            Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == cronOccurrenceId),
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<CancellationToken>());

        Assert.All(resources, r => Assert.Equal(TickerStatus.InProgress, r.Status));
    }

    [Fact]
    public async Task SetTickersInProgress_OnlyCronOccurrences_CallsCronUpdateOnly()
    {
        var cronId = Guid.NewGuid();
        var resources = new[]
        {
            new InternalFunctionContext
            {
                TickerId = cronId,
                Type = TickerType.CronTickerOccurrence,
                FunctionName = "CronFunc"
            }
        };

        _persistence.UpdateCronTickerOccurrencesWithUnifiedContext(
                Arg.Any<Guid[]>(), Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _manager.SetTickersInProgress(resources, CancellationToken.None);

        await _persistence.Received(1).UpdateCronTickerOccurrencesWithUnifiedContext(
            Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == cronId),
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<CancellationToken>());
        await _persistence.DidNotReceive().UpdateTimeTickersWithUnifiedContext(
            Arg.Any<Guid[]>(), Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // 4. ReleaseAcquiredResources
    // ====================================================================

    [Fact]
    public async Task ReleaseAcquiredResources_CallsPersistenceProvider_ForBothTypes()
    {
        var timeTickerId = Guid.NewGuid();
        var cronOccurrenceId = Guid.NewGuid();
        var resources = new[]
        {
            new InternalFunctionContext { TickerId = timeTickerId, Type = TickerType.TimeTicker },
            new InternalFunctionContext { TickerId = cronOccurrenceId, Type = TickerType.CronTickerOccurrence }
        };

        _persistence.ReleaseAcquiredTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _persistence.ReleaseAcquiredCronTickerOccurrences(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _manager.ReleaseAcquiredResources(resources, CancellationToken.None);

        await _persistence.Received(1).ReleaseAcquiredTimeTickers(
            Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == timeTickerId),
            Arg.Any<CancellationToken>());
        await _persistence.Received(1).ReleaseAcquiredCronTickerOccurrences(
            Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == cronOccurrenceId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseAcquiredResources_EmptyArray_DoesNotCallPersistence()
    {
        _persistence.ReleaseAcquiredTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _persistence.ReleaseAcquiredCronTickerOccurrences(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _manager.ReleaseAcquiredResources(Array.Empty<InternalFunctionContext>(), CancellationToken.None);

        await _persistence.DidNotReceive().ReleaseAcquiredTimeTickers(
            Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
        await _persistence.DidNotReceive().ReleaseAcquiredCronTickerOccurrences(
            Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseAcquiredResources_NullArray_CallsPersistenceWithEmptyArrays()
    {
        _persistence.ReleaseAcquiredTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _persistence.ReleaseAcquiredCronTickerOccurrences(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _manager.ReleaseAcquiredResources(null, CancellationToken.None);

        await _persistence.Received(1).ReleaseAcquiredCronTickerOccurrences(
            Arg.Is<Guid[]>(ids => ids.Length == 0),
            Arg.Any<CancellationToken>());
        await _persistence.Received(1).ReleaseAcquiredTimeTickers(
            Arg.Is<Guid[]>(ids => ids.Length == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseAcquiredResources_OnlyTimeTickers_CallsOnlyTimeTickerRelease()
    {
        var timeTickerId = Guid.NewGuid();
        var resources = new[]
        {
            new InternalFunctionContext { TickerId = timeTickerId, Type = TickerType.TimeTicker }
        };

        _persistence.ReleaseAcquiredTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _persistence.ReleaseAcquiredCronTickerOccurrences(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _manager.ReleaseAcquiredResources(resources, CancellationToken.None);

        await _persistence.Received(1).ReleaseAcquiredTimeTickers(
            Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == timeTickerId),
            Arg.Any<CancellationToken>());
        await _persistence.DidNotReceive().ReleaseAcquiredCronTickerOccurrences(
            Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // 5. RunTimedOutTickers
    // ====================================================================

    [Fact]
    public async Task RunTimedOutTickers_ReturnsTimedOutTimeTickersAndCronOccurrences()
    {
        var timeTickerId = Guid.NewGuid();
        var cronOccurrenceId = Guid.NewGuid();
        var cronTickerId = Guid.NewGuid();
        var executionTime = _now.AddMinutes(-10);

        var timedOutTimeTicker = new TimeTickerEntity
        {
            Id = timeTickerId,
            Function = "TimedOutTimeFunc",
            ExecutionTime = executionTime,
            Retries = 1,
            RetryIntervals = new[] { 1000 },
            Children = new List<TimeTickerEntity>()
        };

        var timedOutCronOccurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = cronOccurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = executionTime,
            CronTicker = new FakeCronTicker
            {
                Id = cronTickerId,
                Function = "TimedOutCronFunc",
                Retries = 2,
                RetryIntervals = new[] { 500, 1000 }
            }
        };

        _persistence.QueueTimedOutTimeTickers(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new[] { timedOutTimeTicker }));
        _persistence.QueueTimedOutCronTickerOccurrences(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new[] { timedOutCronOccurrence }));

        var results = await _manager.RunTimedOutTickers(CancellationToken.None);

        Assert.Equal(2, results.Length);

        var timeResult = Assert.Single(results.Where(r => r.Type == TickerType.TimeTicker));
        Assert.Equal(timeTickerId, timeResult.TickerId);
        Assert.Equal("TimedOutTimeFunc", timeResult.FunctionName);
        Assert.Equal(1, timeResult.Retries);

        var cronResult = Assert.Single(results.Where(r => r.Type == TickerType.CronTickerOccurrence));
        Assert.Equal(cronOccurrenceId, cronResult.TickerId);
        Assert.Equal("TimedOutCronFunc", cronResult.FunctionName);
        Assert.Equal(cronTickerId, cronResult.ParentId);
        Assert.Equal(2, cronResult.Retries);
    }

    [Fact]
    public async Task RunTimedOutTickers_EmptyWhenNothingTimedOut()
    {
        _persistence.QueueTimedOutTimeTickers(Arg.Any<CancellationToken>())
            .Returns(EmptyAsyncEnumerable<TimeTickerEntity>());
        _persistence.QueueTimedOutCronTickerOccurrences(Arg.Any<CancellationToken>())
            .Returns(EmptyAsyncEnumerable<CronTickerOccurrenceEntity<FakeCronTicker>>());

        var results = await _manager.RunTimedOutTickers(CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task RunTimedOutTickers_TimeTickerWithChildren_MapsChildrenCorrectly()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        var grandchild = new TimeTickerEntity
        {
            Id = grandchildId,
            Function = "GrandchildFunc",
            ParentId = childId,
            Retries = 0,
            RetryIntervals = Array.Empty<int>(),
            RunCondition = RunCondition.OnAnyCompletedStatus,
            Children = new List<TimeTickerEntity>()
        };

        var child = new TimeTickerEntity
        {
            Id = childId,
            Function = "ChildFunc",
            ParentId = parentId,
            Retries = 1,
            RetryIntervals = new[] { 100 },
            RunCondition = RunCondition.OnAnyCompletedStatus,
            Children = new List<TimeTickerEntity> { grandchild }
        };

        var parent = new TimeTickerEntity
        {
            Id = parentId,
            Function = "ParentFunc",
            ExecutionTime = _now.AddMinutes(-5),
            Retries = 0,
            RetryIntervals = Array.Empty<int>(),
            Children = new List<TimeTickerEntity> { child }
        };

        _persistence.QueueTimedOutTimeTickers(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new[] { parent }));
        _persistence.QueueTimedOutCronTickerOccurrences(Arg.Any<CancellationToken>())
            .Returns(EmptyAsyncEnumerable<CronTickerOccurrenceEntity<FakeCronTicker>>());

        var results = await _manager.RunTimedOutTickers(CancellationToken.None);

        Assert.Single(results);
        var parentResult = results[0];
        Assert.Equal(parentId, parentResult.TickerId);
        Assert.Single(parentResult.TimeTickerChildren);

        var childResult = parentResult.TimeTickerChildren[0];
        Assert.Equal(childId, childResult.TickerId);
        Assert.Equal("ChildFunc", childResult.FunctionName);
        Assert.Equal(parentId, childResult.ParentId);
        Assert.Equal(RunCondition.OnAnyCompletedStatus, childResult.RunCondition);
        Assert.Single(childResult.TimeTickerChildren);

        var grandchildResult = childResult.TimeTickerChildren[0];
        Assert.Equal(grandchildId, grandchildResult.TickerId);
        Assert.Equal("GrandchildFunc", grandchildResult.FunctionName);
        Assert.Equal(childId, grandchildResult.ParentId);
    }

    [Fact]
    public async Task RunTimedOutTickers_NotifiesHub_ForTimeTickers()
    {
        var timedOutTimeTicker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "Func",
            ExecutionTime = _now.AddMinutes(-1),
            Retries = 0,
            RetryIntervals = Array.Empty<int>(),
            Children = new List<TimeTickerEntity>()
        };

        _persistence.QueueTimedOutTimeTickers(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new[] { timedOutTimeTicker }));
        _persistence.QueueTimedOutCronTickerOccurrences(Arg.Any<CancellationToken>())
            .Returns(EmptyAsyncEnumerable<CronTickerOccurrenceEntity<FakeCronTicker>>());

        await _manager.RunTimedOutTickers(CancellationToken.None);

        await _notificationHub.Received(1).UpdateTimeTickerNotifyAsync(timedOutTimeTicker);
    }

    [Fact]
    public async Task RunTimedOutTickers_NotifiesHub_ForCronOccurrences()
    {
        var cronTickerId = Guid.NewGuid();
        var cronOccurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(-1),
            CronTicker = new FakeCronTicker
            {
                Id = cronTickerId,
                Function = "CronFunc",
                Retries = 0,
                RetryIntervals = Array.Empty<int>()
            }
        };

        _persistence.QueueTimedOutTimeTickers(Arg.Any<CancellationToken>())
            .Returns(EmptyAsyncEnumerable<TimeTickerEntity>());
        _persistence.QueueTimedOutCronTickerOccurrences(Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new[] { cronOccurrence }));

        await _manager.RunTimedOutTickers(CancellationToken.None);

        await _notificationHub.Received(1)
            .UpdateCronOccurrenceFromInternalFunctionContext<FakeCronTicker>(Arg.Any<InternalFunctionContext>());
    }
}
