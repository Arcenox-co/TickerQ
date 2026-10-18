using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed partial class EfTransactionalSchedulingTests
{
    [Fact]
    public async Task StagedRecurringChain_PreservesTemplateAndSupportsScopedManagerOperations()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cronManager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var timeManager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var cron = Cron();
        cron.SkipIfAlreadyRunning = true;
        cron.Chain = new CronChainTemplate
        {
            Children = [new CronChainNode { Function = "transactional-test" }]
        };
        var job = Job();
        await cronManager.StageAsync(cron);
        await timeManager.StageAsync(job);
        await db.SaveChangesAsync();

        Assert.True((await cronManager.SetEnabledAsync(cron.Id, false)).IsSucceeded);
        Assert.True((await timeManager.SetDeleteBehaviorAsync(job.Id, ChainDeleteBehavior.Restrict)).IsSucceeded);
        Assert.Equal(0, (await timeManager.RequestCancellationAsync(job.Id)).AffectedRows);
        Assert.Equal(0, (await cronManager.RequestCancellationAsync(Guid.NewGuid())).AffectedRows);
        await using var verify = h.Services.CreateAsyncScope();
        var stored = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedCron = await stored.Set<CronTickerEntity>().SingleAsync();
        Assert.False(savedCron.IsEnabled);
        Assert.True(savedCron.SkipIfAlreadyRunning);
        Assert.Equal("transactional-test", Assert.Single(savedCron.Chain.Children).Function);
        Assert.Equal(ChainDeleteBehavior.Restrict, (await stored.Set<TimeTickerEntity>().SingleAsync()).DeleteBehavior);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagedRecurringChain_RejectsUnknownChildBeforeAnyBusinessCommit(bool changeAfterStaging)
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var cron = Cron();
        cron.Chain = new CronChainTemplate
        {
            Children = [new CronChainNode { Function = changeAfterStaging ? "transactional-test" : "unknown-child" }]
        };
        db.Orders.Add(new Order());
        if (changeAfterStaging)
        {
            await manager.StageAsync(cron);
            cron.Chain.Children[0].Function = "unknown-child";
            await Assert.ThrowsAsync<TickerValidatorException>(() => db.SaveChangesAsync());
        }
        else
            await Assert.ThrowsAsync<TickerValidatorException>(() => manager.StageAsync(cron));

        Assert.Equal((0, 0), await h.Counts());
        Assert.Equal(0, await CronCount(h));
    }
}
