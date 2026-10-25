using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

// UseApplicationDbContext replaces the managers with staging wrappers. These tests cover the operations that
// run through those wrappers instead of staging.
public sealed partial class EfTransactionalSchedulingTests
{
    public sealed class TransactionalTestJob : ITickerFunction
    {
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(typeof(EfStagingTimeTickerManager<TimeTickerEntity>), typeof(ITimeTickerManager<TimeTickerEntity>))]
    [InlineData(typeof(EfStagingCronTickerManager<CronTickerEntity>), typeof(ICronTickerManager<CronTickerEntity>))]
    public void Staging_managers_forward_every_manager_method(Type wrapper, Type managerInterface)
    {
        // A method the wrapper leaves out silently falls back to the interface's NotSupportedException default.
        var map = wrapper.GetInterfaceMap(managerInterface);
        var missing = map.InterfaceMethods
            .Where((_, i) => map.TargetMethods[i].DeclaringType != wrapper)
            .Select(method => method.ToString())
            .ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task Startup_seeding_reaches_the_built_in_manager_through_the_staging_wrappers()
    {
        var runAt = DateTime.UtcNow.AddDays(1);
        await using var h = new Harness(configureTickerQ: options => options
            .UseReconciliationEpoch(1)
            .SeedTimeTickers(tickers => tickers.Add("staged-seed", ticker => ticker.RunAt(runAt).StartWith("transactional-test")))
            .SeedCronTickers(crons => crons.Add("transactional-test", "0 * * * *")));
        await h.InitializeAsync();
        var seeding = h.Services.GetRequiredService<TickerExecutionContext>().OptionsSeeding;

        var crons = await seeding.CronTickerSeedFactory!(h.Services, CancellationToken.None);
        Assert.Single(crons);

        var scope = new ReconciliationActivationScope("staged-jobs");
        await h.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>()
            .BeginReconciliationActivationEpochAsync(scope, 1, CancellationToken.None);
        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 1))
            await seeding.TimeTickerSeedAction!(h.Services, CancellationToken.None);

        Assert.Equal((0, 1), await h.Counts());
    }

    [Fact]
    public async Task Reschedule_outcome_and_cancel_pending_work_through_the_staging_wrapper()
    {
        TickerFunctionProvider.RegisterTypeMapping(typeof(TransactionalTestJob), "transactional-test");
        await using var h = new Harness();
        await h.InitializeAsync();
        var manager = h.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var first = await manager.AddAsync(new TimeTickerEntity { Function = "transactional-test", ExecutionTime = DateTime.UtcNow.AddHours(1) });
        var second = await manager.AddAsync(new TimeTickerEntity { Function = "transactional-test", ExecutionTime = DateTime.UtcNow.AddHours(1) });
        Assert.True(first.IsSucceeded, first.Exception?.ToString());
        Assert.True(second.IsSucceeded, second.Exception?.ToString());

        var moveTo = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(3).Date, DateTimeKind.Utc);
        var moved = await manager.RescheduleAsync(first.Result.Id, moveTo);
        Assert.True(moved.IsSucceeded, moved.Exception?.ToString());
        var outcome = await manager.GetOutcomeAsync(first.Result.Id);
        Assert.Equal(TickerStatus.Idle, outcome!.Status);
        Assert.False(outcome.IsCompleted);

        var cancelled = await manager.CancelPendingAsync<TransactionalTestJob>();
        Assert.True(cancelled.IsSucceeded, cancelled.Exception?.ToString());
        Assert.Equal(2, cancelled.AffectedRows);
        var after = await manager.WaitForCompletionAsync(first.Result.Id, TimeSpan.FromSeconds(5));
        Assert.True(after!.IsCompleted);
        Assert.Equal(TickerStatus.Cancelled, after.Status);
        Assert.False((await manager.RescheduleAsync(second.Result.Id, moveTo)).IsSucceeded);

        var stored = await h.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>()
            .GetTimeTickerById(first.Result.Id);
        Assert.Equal(moveTo, stored!.ExecutionTime);
    }

    [Fact]
    public async Task Next_cron_occurrences_through_the_staging_wrapper()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        var manager = h.Services.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var cron = await manager.AddAsync(new CronTickerEntity { Function = "transactional-test", Expression = "0 * * * *" });
        Assert.True(cron.IsSucceeded, cron.Exception?.ToString());

        var next = await manager.GetNextOccurrencesAsync(cron.Result.Id, 3);

        Assert.True(next.IsSucceeded, next.Exception?.ToString());
        Assert.Equal(3, next.Result.Count);
        Assert.All(next.Result, time => Assert.Equal((0, 0), (time.Minute, time.Second)));
        Assert.Equal([TimeSpan.FromHours(1), TimeSpan.FromHours(1)],
            next.Result.Zip(next.Result.Skip(1), (earlier, later) => later - earlier));
    }
}
