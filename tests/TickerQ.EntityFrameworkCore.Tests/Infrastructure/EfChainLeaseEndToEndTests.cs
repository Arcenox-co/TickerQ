using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using TickerQ.BackgroundServices;
using TickerQ.Dispatcher;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Runs a real time-ticker chain through the scheduler execution path, the lease-renewal loop
/// and the EF Core provider (file-backed SQLite, so the renewal loop and the chain write
/// concurrently on their own connections). The root finishes first; its descendants must keep
/// running under the chain lease instead of being cancelled as a lost lease.
/// </summary>
public sealed class EfChainLeaseEndToEndTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tickerq-chain-lease-{Guid.NewGuid():N}.db");
    private DbContextOptions<TestTickerQDbContext> _dbOptions = null!;
    private ServiceProvider _services = null!;
    private TestableProvider _provider = null!;
    private SchedulerOptionsBuilder _options = null!;
    private ITickerClock _clock = null!;

    public async Task InitializeAsync()
    {
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_ => DateTime.UtcNow);
        var redisContext = Substitute.For<ITickerQRedisContext>();
        redisContext.HasRedisConnection.Returns(false);

        _dbOptions = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=30")
            .Options;
        await using (var seedContext = new TestTickerQDbContext(_dbOptions))
            await seedContext.Database.EnsureCreatedAsync();

        _options = new SchedulerOptionsBuilder { NodeIdentifier = "chain-lease-node" };
        _services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
                new PooledDbContextFactory<TestTickerQDbContext>(_dbOptions))
            .BuildServiceProvider();
        _provider = new TestableProvider(_services, _clock, _options, redisContext);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task FinishedRoot_KeepsItsChainRunningAcrossLeaseRenewals_ThenReleasesTheLease()
    {
        var renewals = 0;
        var chain = await StartChainAsync(async ct =>
        {
            // Keep the child running until the root's chain lease has been renewed several times.
            DateTime? last = null;
            while (renewals < 3)
            {
                var root = await _provider.GetTimeTickerById(RootId, ct);
                if (root!.LeaseUntil is { } until && until != last)
                {
                    if (last != null) renewals++;
                    last = until;
                }
                await Task.Delay(10, ct);
            }
        });

        await chain.RunAsync();

        Assert.Equal(TickerStatus.Done, chain.Child.Status);
        Assert.True(renewals >= 3);
        var root = await _provider.GetTimeTickerById(RootId, CancellationToken.None);
        Assert.Equal(TickerStatus.Done, root!.Status);
        Assert.Equal(_options.ExecutionOwnerId, root.LockHolder);
        Assert.Equal(chain.Generation, root.ChainGeneration);
        Assert.Null(root.LeaseUntil);
        Assert.Equal(TickerStatus.Done, (await _provider.GetTimeTickerById(chain.Child.TickerId, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task ChainWhoseGenerationMovedElsewhere_IsStillCancelledAsALostLease()
    {
        var observedCancellation = false;
        var chain = await StartChainAsync(async ct =>
        {
            // Another owner fences the chain generation while this node still runs the child.
            await using (var db = new TestTickerQDbContext(_dbOptions))
                await db.Set<TimeTickerEntity>().Where(x => x.Id == RootId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ChainGeneration, Guid.NewGuid()), ct);
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { observedCancellation = true; throw; }
        });

        // The stale owner's terminal write is fenced and surfaces as unacknowledged.
        await Assert.ThrowsAsync<TickerQ.Utilities.Exceptions.TickerResultNotAcknowledgedException>(chain.RunAsync);

        Assert.True(observedCancellation);
        Assert.Equal(TickerStatus.Cancelled, chain.Child.Status);
        Assert.Equal(TickerStatus.InProgress, (await _provider.GetTimeTickerById(chain.Child.TickerId, CancellationToken.None))!.Status);
    }

    private Guid RootId { get; set; }

    private async Task<RunningChain> StartChainAsync(Func<CancellationToken, Task> childBody)
    {
        var rootRow = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "root-fn", Request = [], ExecutionTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var childRow = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "child-fn", Request = [], ParentId = rootRow.Id,
            RunCondition = RunCondition.OnSuccess, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        rootRow.Children = [childRow];
        RootId = rootRow.Id;
        await _provider.AddTimeTickers([rootRow], CancellationToken.None);
        Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([rootRow.Id], CancellationToken.None));
        var generation = (await _provider.GetTimeTickerById(rootRow.Id, CancellationToken.None))!.AcquisitionToken!.Value;

        var root = Context(rootRow.Id, "root-fn", (_, _, _) => Task.CompletedTask);
        root.AcquisitionToken = generation;
        root.ChainGeneration = generation;
        root.ChainRootId = rootRow.Id;
        root.Status = TickerStatus.InProgress;
        var child = Context(childRow.Id, "child-fn", (ct, _, _) => childBody(ct));
        child.ParentId = rootRow.Id;
        child.ChainRootId = rootRow.Id;
        child.ChainGeneration = generation;
        child.AcquisitionToken = generation;
        child.RunCondition = RunCondition.OnSuccess;
        root.TimeTickerChildren.Add(child);
        return new RunningChain(this, root, child, generation);
    }

    private static InternalFunctionContext Context(Guid id, string function, TickerFunctionDelegate body) => new()
    {
        TickerId = id, FunctionName = function, Type = TickerType.TimeTicker, ExecutionTime = DateTime.UtcNow,
        RetryIntervals = [], Status = TickerStatus.Idle, CachedDelegate = body, TimeTickerChildren = []
    };

    private sealed record RunningChain(
        EfChainLeaseEndToEndTests Owner, InternalFunctionContext Root, InternalFunctionContext Child, Guid Generation)
    {
        public async Task RunAsync()
        {
            var license = ActiveLicense(Owner._clock);
            var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(
                Owner._provider, Owner._clock, Substitute.For<ITickerQNotificationHubSender>(), Owner._options);
            var handler = new TickerExecutionTaskHandler(Owner._services, Owner._clock,
                Substitute.For<ITickerQInstrumentation>(), manager, Owner._options,
                Substitute.For<ITickerQFailureNotifier>(), license, new TickerFunctionConcurrencyGate());
            var scheduler = Substitute.For<ITickerQTaskScheduler>();
            var work = new List<Func<CancellationToken, Task>>();
            scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
                .Returns(call => { work.Add(call.ArgAt<Func<CancellationToken, Task>>(0)); return ValueTask.CompletedTask; });
            using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance, license);
            var dispatcher = new TickerQDispatcher(scheduler, handler, license, queue);
            using var renewal = new TickerQStaleJobRecoveryBackgroundService(manager,
                new SchedulerOptionsBuilder { LeaseRenewalInterval = TimeSpan.FromMilliseconds(40) },
                NullLogger<TickerQStaleJobRecoveryBackgroundService>.Instance,
                Substitute.For<ITickerQFailureNotifier>(), license);

            await renewal.StartAsync(CancellationToken.None);
            try
            {
                await dispatcher.DispatchAsync([Root]);
                await Task.WhenAll(work.Select(w => w(CancellationToken.None))).WaitAsync(TimeSpan.FromSeconds(20));
            }
            finally
            {
                await renewal.StopAsync(CancellationToken.None);
            }
        }
    }

    private static TickerQLicenseStateProvider ActiveLicense(ITickerClock clock)
    {
        var now = new DateTimeOffset(clock.UtcNow);
        var provider = new TickerQLicenseStateProvider(clock);
        provider.Publish(TickerQLicenseState.ForCertificate(
            TickerQLicenseStatus.Active, isEvaluation: false,
            Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "TickerQ Tests", "Business", "Full", now.AddDays(-1), now.AddYears(1), daysRemaining: 365,
            schemaVersion: 4, algorithm: "Ed25519", keyId: "production-2026-01", anchoredMinorLine: "5.x"));
        return provider;
    }
}
