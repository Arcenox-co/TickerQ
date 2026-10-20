using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

internal static class EfGraphMutationContentionContract
{
    internal static async Task ConcurrentFirstWritersAndAcquirersAsync(DbContextOptions<TestTickerQDbContext> options)
    {
        const int replicas = 16;
        await using (var schema = new TestTickerQDbContext(options))
            await schema.Database.EnsureCreatedAsync();
        await using var services = Services(options);
        var providers = Enumerable.Range(0, replicas).Select(index => Provider(services, $"writer-{index}")).ToArray();
        var jobs = Enumerable.Range(0, replicas).Select(_ => Job()).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var writes = providers.Select(async (provider, index) =>
        {
            await start.Task.WaitAsync(timeout.Token);
            Assert.Equal(1, await provider.AddTimeTickers([jobs[index]], timeout.Token));
            var acquired = Assert.Single(await provider.AcquireImmediateTimeTickersAsync([jobs[index].Id], timeout.Token));
            Assert.Equal(jobs[index].Id, acquired.Id);
            Assert.NotNull(acquired.AcquisitionToken);
        }).ToArray();
        start.TrySetResult();
        await Task.WhenAll(writes);

        await using var verify = new TestTickerQDbContext(options);
        var stored = await verify.Set<TimeTickerEntity>().AsNoTracking().ToArrayAsync(timeout.Token);
        Assert.Equal(replicas, stored.Length);
        Assert.Equal(jobs.Select(x => x.Id).Order(), stored.Select(x => x.Id).Order());
        Assert.All(stored, ticker => Assert.Equal(TickerStatus.InProgress, ticker.Status));
        Assert.Single(await verify.Set<TickerQStoreMetadata>().AsNoTracking()
            .Where(x => x.Id == TickerQStoreMetadata.TimeTickerGraphMutationSentinelId).ToArrayAsync(timeout.Token));
    }

    internal static async Task SnapshotConflict_ReplaysInsertAndAcquisitionAsync(DbContextOptions<TestTickerQDbContext> options)
    {
        await using (var schema = new TestTickerQDbContext(options))
            await schema.Database.EnsureCreatedAsync();
        await using var initialServices = Services(options);
        var existing = Job();
        Assert.Equal(1, await Provider(initialServices, "seed").AddTimeTickers([existing], CancellationToken.None));

        // This connection deliberately owns a manually controlled transaction; only
        // the competing provider operations are under the retry policy being tested.
        var holderOptions = new DbContextOptionsBuilder<TestTickerQDbContext>(options)
            .ReplaceService<IExecutionStrategyFactory, HolderExecutionStrategyFactory>().Options;
        await using var holder = new TestTickerQDbContext(holderOptions);
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Set<TickerQStoreMetadata>()
            .Where(x => x.Id == TickerQStoreMetadata.TimeTickerGraphMutationSentinelId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Version, x => x.Version + 1));

        var waiting = new SentinelWaitInterceptor();
        var contenderOptions = new DbContextOptionsBuilder<TestTickerQDbContext>(options).AddInterceptors(waiting).Options;
        await using var services = Services(contenderOptions);
        var writer = Provider(services, "writer");
        var acquirer = Provider(services, "acquirer");
        var writeContexts = new ConcurrentBag<Guid>();
        var acquireContexts = new ConcurrentBag<Guid>();
        writer.RunnableAdmissionContextCreatedForTest = writeContexts.Add;
        acquirer.RunnableAdmissionContextCreatedForTest = acquireContexts.Add;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var added = Job();
        var insert = writer.AddTimeTickers([added], timeout.Token);
        var acquire = acquirer.AcquireImmediateTimeTickersAsync([existing.Id], timeout.Token);
        try
        {
            // On an MVCC store both serializable transactions have read the old sentinel
            // version before the holder commits. Their conflicting UPDATE must restart the
            // entire transaction; retrying that statement in the same transaction cannot work.
            await waiting.BothAttempted.Task.WaitAsync(timeout.Token);
        }
        finally { await transaction.CommitAsync(CancellationToken.None); }

        Assert.Equal(1, await insert.WaitAsync(timeout.Token));
        Assert.Equal(existing.Id, Assert.Single(await acquire.WaitAsync(timeout.Token)).Id);
        Assert.True(writeContexts.Count >= 2);
        Assert.True(acquireContexts.Count >= 2);
        Assert.Equal(writeContexts.Count, writeContexts.Distinct().Count());
        Assert.Equal(acquireContexts.Count, acquireContexts.Distinct().Count());
        await using var verify = new TestTickerQDbContext(options);
        var rows = await verify.Set<TimeTickerEntity>().AsNoTracking().ToArrayAsync(timeout.Token);
        Assert.Equal(2, rows.Length);
        Assert.Equal(TickerStatus.Idle, rows.Single(x => x.Id == added.Id).Status);
        Assert.Equal(TickerStatus.InProgress, rows.Single(x => x.Id == existing.Id).Status);
    }

    private static ServiceProvider Services(DbContextOptions<TestTickerQDbContext> options)
        => new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new NonPooledTestTickerQDbContextFactory(options))
            .BuildServiceProvider();

    private static TestableProvider Provider(IServiceProvider services, string node)
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        return new TestableProvider(services, clock, new SchedulerOptionsBuilder { NodeIdentifier = node },
            Substitute.For<ITickerQRedisContext>());
    }

    private static TimeTickerEntity Job() => new()
    {
        Id = Guid.NewGuid(),
        Function = "graph-contention",
        ExecutionTime = DateTime.UtcNow.AddHours(1)
    };

    private sealed class SentinelWaitInterceptor : DbCommandInterceptor
    {
        private int _attempts;
        internal TaskCompletionSource BothAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("TickerQStoreMetadata", StringComparison.Ordinal) &&
                command.CommandText.Contains("Version", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _attempts) == 2) BothAttempted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class HolderExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new NonRetryingExecutionStrategy(dependencies);
    }
}
