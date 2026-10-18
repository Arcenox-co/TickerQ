using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed partial class EfTransactionalSchedulingTests
{
    private static CronTickerEntity Cron() => new() { Function = "transactional-test", Expression = "* * * * *" };

    private static async Task<int> CronCount(Harness h)
    {
        await using var scope = h.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CronTickerEntity>().CountAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CronAndTimeStage_SaveTogether_AndNotifyBothEvenWithSameId(int registration)
    {
        await using var h = new Harness(registration);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order());
        var time = Job(); var cron = Cron(); cron.Id = time.Id;
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(time);
        Assert.Same(cron, await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(cron));
        Assert.Equal(EntityState.Added, db.Entry(cron).State);
        Assert.Equal(0, await CronCount(h));
        Assert.Empty(h.Notifications.ReceivedCalls());
        await db.SaveChangesAsync();
        Assert.Equal((1, 1), await h.Counts());
        Assert.Equal(1, await CronCount(h));
        Assert.Equal(1, cron.DefinitionRevision);
        Assert.Equal(new TickerQRuntimePartition("staged-jobs").StorageKey, cron.ApplicationNamespaceKey);
        Assert.Null(cron.SeedKey);
        await h.Notifications.Received(1).AddTimeTickerNotifyAsync(time.Id);
        await h.Notifications.Received(1).AddCronTickerNotifyAsync(cron.Id);
        Assert.Empty(h.Dispatcher.ReceivedCalls());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CronStage_CacheAndScheduleRemainUnchangedUntilCommit(bool commit)
    {
        await using var h = new Harness(withCronCache: true);
        await h.InitializeAsync();
        var provider = h.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        Assert.Empty(await provider.GetAllCronTickerExpressions(CancellationToken.None)); // Warm an empty cache.
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Orders.Add(new Order());
        var cron = Cron();
        await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(cron);
        await db.SaveChangesAsync();
        Assert.Equal((0, 0), await h.Counts());
        Assert.Equal(0, await CronCount(h));
        Assert.Empty(await provider.GetAllCronTickerExpressions(CancellationToken.None));
        Assert.Empty(h.Cache.ReceivedCalls());
        Assert.Empty(h.Notifications.ReceivedCalls());
        Assert.Empty(h.Scheduler.ReceivedCalls());
        if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        Assert.Equal(commit ? (1, 0) : (0, 0), await h.Counts());
        Assert.Equal(commit ? 1 : 0, await CronCount(h));
        Assert.Equal(commit ? 1 : 0, (await provider.GetAllCronTickerExpressions(CancellationToken.None)).Length);
        var key = $"cron:expressions:{new TickerQRuntimePartition("staged-jobs").StorageKey}";
        await h.Cache.Received(commit ? 1 : 0).RemoveAsync(key, Arg.Any<CancellationToken>());
        await h.Notifications.Received(commit ? 1 : 0).AddCronTickerNotifyAsync(cron.Id);
        h.Scheduler.Received(commit ? 1 : 0).RestartIfNeeded(Arg.Any<DateTime?>());
    }

    [Theory]
    [InlineData("expression")]
    [InlineData("function")]
    [InlineData("seed")]
    public async Task CronStage_ValidatesAtStagingAndAgainAtSave(string mutation)
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var invalid = Cron(); invalid.Expression = "not a schedule";
        await Assert.ThrowsAsync<TickerValidatorException>(() => manager.StageAsync(invalid));
        invalid.Expression = "";
        await Assert.ThrowsAsync<TickerValidatorException>(() => manager.StageAsync(invalid));
        Assert.Empty(db.ChangeTracker.Entries<CronTickerEntity>());
        var cron = Cron();
        db.Orders.Add(new Order());
        await manager.StageAsync(cron);
        if (mutation == "expression") cron.Expression = "invalid";
        if (mutation == "function") cron.Function = "missing";
        if (mutation == "seed") cron.SeedKey = "code-owned";
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Equal((0, 0), await h.Counts());
        Assert.Equal(0, await CronCount(h));
        cron.Expression = "0 * * * * *"; cron.Function = "transactional-test"; cron.SeedKey = null;
        await db.SaveChangesAsync();
        Assert.Equal((1, 0), await h.Counts());
        Assert.Equal(1, await CronCount(h));
    }

    [Fact]
    public async Task CronStage_BusinessFailureRollsBackSchedule_AndRetryPublishesOnce()
    {
        await using var h = new Harness(withCronCache: true);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Name = null! }; db.Orders.Add(order);
        var cron = Cron();
        await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(cron);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(0, await CronCount(h));
        Assert.Empty(h.Cache.ReceivedCalls());
        Assert.Empty(h.Notifications.ReceivedCalls());
        order.Name = "fixed";
        await db.SaveChangesAsync();
        Assert.Equal((1, 0), await h.Counts());
        Assert.Equal(1, await CronCount(h));
        await h.Notifications.Received(1).AddCronTickerNotifyAsync(cron.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CronStage_TransientWriteOrCommitFailure_RetriesAtomically(bool atCommit)
    {
        var fault = new TransientFailure(atCommit, "CronTickers");
        await using var h = new Harness(interceptor: fault, retry: true, withCronCache: true);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order());
        var cron = Cron();
        await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(cron);
        fault.Armed = true;
        await db.SaveChangesAsync();
        Assert.Equal(1, fault.Failures);
        Assert.Equal((1, 0), await h.Counts());
        Assert.Equal(1, await CronCount(h));
        await h.Notifications.Received(1).AddCronTickerNotifyAsync(cron.Id);
        await h.Cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CronStage_SavepointRollbackNotifiesOnlyRetainedSchedule()
    {
        await using var h = new Harness(withCronCache: true);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var manager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var retained = Cron(); var discarded = Cron();
        await manager.StageAsync(retained);
        await db.SaveChangesAsync();
        await tx.CreateSavepointAsync("before_second_cron");
        await manager.StageAsync(discarded);
        await db.SaveChangesAsync();
        await tx.RollbackToSavepointAsync("before_second_cron");
        await tx.CommitAsync();
        Assert.Equal(1, await CronCount(h));
        await h.Notifications.Received(1).AddCronTickerNotifyAsync(retained.Id);
        await h.Notifications.DidNotReceive().AddCronTickerNotifyAsync(discarded.Id);
        await h.Cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CronStage_UnsavedPooledScopeDoesNotLeak_AndRootAddRemainsIndependent()
    {
        await using var h = new Harness(registration: 1);
        await h.InitializeAsync();
        var rootManager = h.Services.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        Assert.Contains("DI scope", (await Assert.ThrowsAsync<InvalidOperationException>(() => rootManager.StageAsync(Cron()))).Message);
        await using (var scope = h.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(Cron());
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.Add(new Order());
            await db.SaveChangesAsync();
            Assert.Equal(0, await CronCount(h));
            db.Orders.Add(new Order()); // Remains unsaved while AddAsync persists independently.
            var result = await rootManager.AddAsync(Cron());
            Assert.True(result.IsSucceeded, result.Exception?.ToString());
        }
        Assert.Equal((1, 0), await h.Counts());
        Assert.Equal(1, await CronCount(h));
    }

    [Fact]
    public async Task CronStage_MissingIntegrationFailsClearly()
    {
        await using var h = new Harness(withStore: false);
        await using var scope = h.Services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(Cron()));
        Assert.Contains("UseApplicationDbContext", error.Message);
    }

    [Fact]
    public async Task CronStage_DisabledScheduleIsSavedWithoutWakingScheduler_EvenIfCacheInvalidationFails()
    {
        await using var h = new Harness(withCronCache: true);
        await h.InitializeAsync();
        h.Cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cache offline")));
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cron = Cron(); cron.IsEnabled = false;
        await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(cron);
        db.SaveChanges(); // Synchronous path shares the commit behavior.
        Assert.Equal(1, await CronCount(h));
        Assert.Empty(h.Scheduler.ReceivedCalls());
        await h.Notifications.Received(1).AddCronTickerNotifyAsync(cron.Id);
    }
}
