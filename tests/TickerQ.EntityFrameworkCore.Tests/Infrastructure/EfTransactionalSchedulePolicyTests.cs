using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed partial class EfTransactionalSchedulingTests
{
    [Fact]
    public async Task TimeStage_MoveForward_PreservesRequestedInstantInsideBusinessTransaction()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        var policy = await CreateBusinessHoursPolicyAsync(h);
        var saturday = Next(DayOfWeek.Saturday).AddHours(12);

        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = Job();
        job.ExecutionTime = saturday;
        job.SchedulePolicyName = policy.Name.ToLowerInvariant();
        job.BlockedOccurrenceBehavior = BlockedOccurrenceBehavior.MoveForward;
        db.Orders.Add(new Order());

        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>()
            .StageAsync(job);

        Assert.Equal(saturday, job.RequestedExecutionTime);
        Assert.Equal(saturday.AddDays(2).Date.AddHours(9), job.ExecutionTime);
        Assert.Equal(policy.Name, job.SchedulePolicyName);
        Assert.Equal(policy.Version, job.SchedulePolicyVersion);
        Assert.NotNull(job.ScheduleAdjustmentReason);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == job.Id);
        Assert.Equal(saturday, stored.RequestedExecutionTime);
        Assert.Equal(job.ExecutionTime, stored.ExecutionTime);
    }

    [Fact]
    public async Task TimeStage_Skip_TerminalizesEntireGraphBeforeCommit()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        var policy = await CreateBusinessHoursPolicyAsync(h);
        var saturday = Next(DayOfWeek.Saturday).AddHours(12);

        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var root = Job();
        root.ExecutionTime = saturday;
        root.SchedulePolicyName = policy.Name;
        root.BlockedOccurrenceBehavior = BlockedOccurrenceBehavior.Skip;
        root.Children = [Job()];

        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>()
            .StageAsync(root);

        Assert.All(new[] { root, root.Children.Single() }, node =>
        {
            Assert.Equal(TickerStatus.Skipped, node.Status);
            Assert.NotNull(node.ExecutedAt);
            Assert.Contains("Schedule policy", node.SkippedReason);
        });

        await db.SaveChangesAsync();
        var stored = await db.Set<TimeTickerEntity>().AsNoTracking()
            .Where(x => x.ChainRootId == root.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, node => Assert.Equal(TickerStatus.Skipped, node.Status));
    }

    [Fact]
    public async Task CronStage_StoresPolicyVersionAndTimeZone()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        var policy = await CreateBusinessHoursPolicyAsync(h);

        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cron = Cron();
        // 10:00 Berlin is 09:00 UTC during standard time, inside the policy window.
        cron.Expression = "0 10 * * 1-5";
        cron.TimeZoneId = "Europe/Berlin";
        cron.SchedulePolicyName = policy.Name.ToUpperInvariant();
        cron.BlockedOccurrenceBehavior = BlockedOccurrenceBehavior.Skip;

        await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>()
            .StageAsync(cron);

        Assert.Equal(policy.Name, cron.SchedulePolicyName);
        Assert.Equal(policy.Version, cron.SchedulePolicyVersion);
        Assert.Equal("Europe/Berlin", cron.TimeZoneId);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var stored = await db.Set<CronTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == cron.Id);
        Assert.Equal(policy.Version, stored.SchedulePolicyVersion);
        Assert.Equal("Europe/Berlin", stored.TimeZoneId);
    }

    private static async Task<SchedulePolicy> CreateBusinessHoursPolicyAsync(Harness h)
        => await h.Services.GetRequiredService<ISchedulePolicyManager>().UpsertAsync(
            SchedulePolicy.Create("Business hours")
                .InTimeZone("UTC")
                .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday)
                .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
                .Build());

    private static DateTime Next(DayOfWeek day)
    {
        var date = DateTime.UtcNow.Date.AddDays(14);
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }
}
