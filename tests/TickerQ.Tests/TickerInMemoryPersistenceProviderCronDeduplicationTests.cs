using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class TickerInMemoryPersistenceProviderCronDeduplicationTests : IAsyncLifetime
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly ITickerClock _clock;
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;

    public TickerInMemoryPersistenceProviderCronDeduplicationTests()
    {
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(DateTime.UtcNow);

        var services = new ServiceCollection();
        services.AddSingleton(_clock);
        services.AddSingleton(new SchedulerOptionsBuilder());
        var serviceProvider = services.BuildServiceProvider();

        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(serviceProvider);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Clean up static state between tests by removing all cron occurrences and tickers
        var allOccurrences = await CollectAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));
        var ids = allOccurrences.Select(x => x.Id).ToArray();
        if (ids.Length > 0)
            await _provider.RemoveCronTickerOccurrences(ids, CancellationToken.None);
    }

    [Fact]
    public async Task QueueCronTickerOccurrences_SameExecutionTimeAndCronTickerId_ShouldNotCreateDuplicate()
    {
        var cronTickerId = Guid.NewGuid();
        var executionTime = DateTime.UtcNow.AddMinutes(1);

        await SetupCronTicker(cronTickerId);

        var context1 = new InternalManagerContext(cronTickerId) { FunctionName = "TestFunc", Expression = "* * * * *" };
        var context2 = new InternalManagerContext(cronTickerId) { FunctionName = "TestFunc", Expression = "* * * * *" };

        var firstResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((executionTime, new[] { context1 }), CancellationToken.None));

        var secondResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((executionTime, new[] { context2 }), CancellationToken.None));

        Assert.Single(firstResults);
        Assert.Empty(secondResults);

        // Cleanup
        await _provider.RemoveCronTickerOccurrences(new[] { firstResults[0].Id }, CancellationToken.None);
    }

    [Fact]
    public async Task QueueCronTickerOccurrences_DifferentExecutionTimes_ShouldCreateBoth()
    {
        var cronTickerId = Guid.NewGuid();
        var time1 = DateTime.UtcNow.AddMinutes(1);
        var time2 = DateTime.UtcNow.AddMinutes(2);

        await SetupCronTicker(cronTickerId);

        var context1 = new InternalManagerContext(cronTickerId) { FunctionName = "TestFunc", Expression = "* * * * *" };
        var context2 = new InternalManagerContext(cronTickerId) { FunctionName = "TestFunc", Expression = "* * * * *" };

        var firstResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((time1, new[] { context1 }), CancellationToken.None));

        var secondResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((time2, new[] { context2 }), CancellationToken.None));

        Assert.Single(firstResults);
        Assert.Single(secondResults);
        Assert.NotEqual(firstResults[0].Id, secondResults[0].Id);

        // Cleanup
        await _provider.RemoveCronTickerOccurrences(
            new[] { firstResults[0].Id, secondResults[0].Id }, CancellationToken.None);
    }

    [Fact]
    public async Task QueueCronTickerOccurrences_DifferentCronTickerIds_SameTime_ShouldCreateBoth()
    {
        var cronTickerId1 = Guid.NewGuid();
        var cronTickerId2 = Guid.NewGuid();
        var executionTime = DateTime.UtcNow.AddMinutes(1);

        await SetupCronTicker(cronTickerId1);
        await SetupCronTicker(cronTickerId2);

        var context1 = new InternalManagerContext(cronTickerId1) { FunctionName = "TestFunc1", Expression = "* * * * *" };
        var context2 = new InternalManagerContext(cronTickerId2) { FunctionName = "TestFunc2", Expression = "* * * * *" };

        var firstResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((executionTime, new[] { context1 }), CancellationToken.None));

        var secondResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((executionTime, new[] { context2 }), CancellationToken.None));

        Assert.Single(firstResults);
        Assert.Single(secondResults);

        // Cleanup
        await _provider.RemoveCronTickerOccurrences(
            new[] { firstResults[0].Id, secondResults[0].Id }, CancellationToken.None);
    }

    [Fact]
    public async Task RemoveCronTickerOccurrences_ShouldAllowRequeueingSameKey()
    {
        var cronTickerId = Guid.NewGuid();
        var executionTime = DateTime.UtcNow.AddMinutes(1);

        await SetupCronTicker(cronTickerId);

        var context = new InternalManagerContext(cronTickerId) { FunctionName = "TestFunc", Expression = "* * * * *" };

        var firstResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((executionTime, new[] { context }), CancellationToken.None));
        Assert.Single(firstResults);

        // Remove the occurrence
        var removed = await _provider.RemoveCronTickerOccurrences(
            new[] { firstResults[0].Id }, CancellationToken.None);
        Assert.Equal(1, removed);

        // Re-queue with the same (ExecutionTime, CronTickerId) should now succeed
        var context2 = new InternalManagerContext(cronTickerId) { FunctionName = "TestFunc", Expression = "* * * * *" };
        var secondResults = await CollectAsync(
            _provider.QueueCronTickerOccurrences((executionTime, new[] { context2 }), CancellationToken.None));
        Assert.Single(secondResults);

        // Cleanup
        await _provider.RemoveCronTickerOccurrences(
            new[] { secondResults[0].Id }, CancellationToken.None);
    }

    [Fact]
    public async Task InsertCronTickerOccurrences_SameExecutionTimeAndCronTickerId_ShouldNotCreateDuplicate()
    {
        var cronTickerId = Guid.NewGuid();
        var executionTime = DateTime.UtcNow.AddMinutes(1);

        await SetupCronTicker(cronTickerId);

        var occurrence1 = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = executionTime,
            Status = TickerStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var occurrence2 = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = executionTime,
            Status = TickerStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var count1 = await _provider.InsertCronTickerOccurrences(new[] { occurrence1 }, CancellationToken.None);
        var count2 = await _provider.InsertCronTickerOccurrences(new[] { occurrence2 }, CancellationToken.None);

        Assert.Equal(1, count1);
        Assert.Equal(0, count2);

        // Cleanup
        await _provider.RemoveCronTickerOccurrences(new[] { occurrence1.Id }, CancellationToken.None);
    }

    [Fact]
    public async Task MigrateDefinedCronTickers_UserRowSharingFunction_NotMatchedOrMutated()
    {
        // A user-created cron (no seed identity) sharing a function name with a code seed,
        // and a DIFFERENT expression. Seed reconciliation must not match or mutate it.
        var userId = Guid.NewGuid();
        var userRow = new FakeCronTicker
        {
            Id = userId,
            Function = "SeedShared",
            Expression = "0 0 * * *",          // differs from the seed
            InitIdentifier = "",               // user row: no seed identity
            RequestContractVersion = null,
            RequestContractFingerprint = null
        };
        await _provider.InsertCronTickers(new[] { userRow }, CancellationToken.None);

        try
        {
            var seed = new DefinedCronTickerSeed("SeedShared", "*/5 * * * *", 9, "sha256:seed-only");
            await _provider.MigrateDefinedCronTickers(new[] { seed }, CancellationToken.None);

            var rows = await _provider.GetCronTickers(x => x.Function == "SeedShared", CancellationToken.None);

            // The user row is untouched.
            var persistedUser = Assert.Single(rows.Where(r => r.Id == userId));
            Assert.Equal("0 0 * * *", persistedUser.Expression);
            Assert.Null(persistedUser.RequestContractVersion);
            Assert.Null(persistedUser.RequestContractFingerprint);
            Assert.Equal("", persistedUser.InitIdentifier);

            // The seed owns a separate, freshly inserted seeded row.
            var seededRow = Assert.Single(rows.Where(r => r.Id != userId));
            Assert.Equal("*/5 * * * *", seededRow.Expression);
            Assert.Equal(9, seededRow.RequestContractVersion);
            Assert.Equal("sha256:seed-only", seededRow.RequestContractFingerprint);
            Assert.StartsWith("MemoryTicker_Seeded_", seededRow.InitIdentifier);

            await _provider.RemoveCronTickers(rows.Select(r => r.Id).ToArray(), CancellationToken.None);
        }
        finally
        {
            await _provider.RemoveCronTickers(new[] { userId }, CancellationToken.None);
        }
    }

    private async Task SetupCronTicker(Guid cronTickerId)
    {
        var cronTicker = new FakeCronTicker
        {
            Id = cronTickerId,
            Function = "TestFunc",
            Expression = "* * * * *"
        };
        await _provider.InsertCronTickers(new[] { cronTicker }, CancellationToken.None);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
