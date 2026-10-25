using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.Conformance;

/// <summary>
/// Starts a real TickerQ host on each configured database and runs work through the scheduler:
/// startup schema certification and activation, an immediate time ticker, a two-step chain, and a
/// per-second cron. Requires TICKERQ_CONFORMANCE_LICENSE in addition to a target connection string.
/// </summary>
[Collection("Relational conformance")]
public sealed class RelationalRuntimeConformanceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    [RelationalRuntimeConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Scheduler_executes_time_tickers_chains_and_crons(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target, createTables: false);
        var suffix = target.ToString().ToLowerInvariant();
        var single = $"conformance.{suffix}.single";
        var parent = $"conformance.{suffix}.parent";
        var child = $"conformance.{suffix}.child";
        var cron = $"conformance.{suffix}.cron";
        var singleRan = Signal();
        var childRan = Signal();
        var cronRan = Signal();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.MapTicker(single, (_, _) => { singleRan.TrySetResult(); return Task.CompletedTask; });
        builder.Services.MapTicker(parent, (_, _) => Task.CompletedTask);
        builder.Services.MapTicker(child, (_, _) => { childRan.TrySetResult(); return Task.CompletedTask; });
        builder.Services.MapTicker(cron, (_, _) => { cronRan.TrySetResult(); return Task.CompletedTask; });
        builder.Services.AddTickerQ(options => options
            .UseApplicationNamespace("conformance-" + suffix)
            .UseReconciliationEpoch(1)
            .UseLicense(Environment.GetEnvironmentVariable("TICKERQ_CONFORMANCE_LICENSE")!)
            .AddOperationalStore(ef => ef
                .UseTickerQDbContext<TickerQDbContext>(db =>
                    RelationalConformanceTargets.Configure(db, target, database.ConnectionString))
                .SetSchema(database.TickerQSchema)));

        using var host = builder.Build();
        await CreateTablesAsync(host.Services);
        host.UseTickerQ();
        using var timeout = new CancellationTokenSource(Deadline);
        await host.StartAsync(timeout.Token);
        try
        {
            var timeManager = host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
            var cronManager = host.Services.GetRequiredService<ICronTickerManager<CronTickerEntity>>();

            var job = await timeManager.AddAsync(new TimeTickerEntity { Function = single, ExecutionTime = DateTime.UtcNow },
                timeout.Token);
            Assert.True(job.IsSucceeded, job.Exception?.ToString());
            var chain = await timeManager.AddChainAsync(c => c
                .RunAt(DateTime.UtcNow)
                .StartWith(parent)
                .Then(child), timeout.Token);
            Assert.True(chain.IsSucceeded, chain.Exception?.ToString());
            var recurring = await cronManager.AddAsync(new CronTickerEntity { Function = cron, Expression = "* * * * * *" },
                timeout.Token);
            Assert.True(recurring.IsSucceeded, recurring.Exception?.ToString());

            await Task.WhenAll(singleRan.Task, childRan.Task, cronRan.Task).WaitAsync(timeout.Token);

            await WaitUntilAsync(host.Services, async db =>
                await db.Set<TimeTickerEntity>().AsNoTracking().CountAsync(x => x.Status == TickerStatus.Done, timeout.Token) >= 3 &&
                await db.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AsNoTracking()
                    .CountAsync(x => x.Status == TickerStatus.Done, timeout.Token) > 0, timeout.Token);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task CreateTablesAsync(IServiceProvider services)
    {
        // Explicit table creation: EnsureCreated can skip CockroachDB (see ConformanceDatabase).
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    private static async Task WaitUntilAsync(IServiceProvider services, Func<TickerQDbContext, Task<bool>> condition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using (var scope = services.CreateAsyncScope())
            {
                if (await condition(scope.ServiceProvider.GetRequiredService<TickerQDbContext>()))
                    return;
            }
            await Task.Delay(250, cancellationToken);
        }
    }
}
