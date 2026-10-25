using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using Testcontainers.PostgreSql;
using TickerQ.EntityFrameworkCore.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

[CollectionDefinition("Transactional scheduling", DisableParallelization = true)]
public sealed class TransactionalSchedulingCollection;

[Collection("Transactional scheduling")]
public sealed partial class EfTransactionalSchedulingTests
{
    public sealed class Order
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "order";
    }

    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Order>().Property(x => x.Name).IsRequired();
            base.OnModelCreating(builder);
        }
    }

    private static TimeTickerEntity Job() => new() { Function = "transactional-test" };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), $"tickerq-stage-{Guid.NewGuid():N}.db");
        public ServiceProvider Services { get; }
        public ITickerQNotificationHubSender Notifications { get; } = Substitute.For<ITickerQNotificationHubSender>();
        public ITickerQHostScheduler Scheduler { get; } = Substitute.For<ITickerQHostScheduler>();
        public ITickerQDispatcher Dispatcher { get; } = Substitute.For<ITickerQDispatcher>();
        public IDistributedCache Cache { get; } = Substitute.For<IDistributedCache>();
        private CronTickerEntity[]? _cachedCrons;

        public Harness(int registration = 0, IInterceptor? interceptor = null, bool withStore = true,
            Action<DbContextOptionsBuilder>? configureProvider = null, bool runtimeEnabled = false, bool retry = false,
            bool legacyPartition = false, bool withCronCache = false,
            Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>>? configureTickerQ = null)
        {
            TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                ["transactional-test"] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton(Substitute.For<IHostEnvironment>());
            Action<DbContextOptionsBuilder> configure = db =>
            {
                db.UseSqlite($"Data Source={_file};Pooling=False");
                if (retry) db.ReplaceService<IExecutionStrategyFactory, RetryableAdmissionExecutionStrategyFactory>();
                if (interceptor != null) db.AddInterceptors(interceptor);
            };
            if (configureProvider != null)
                configure = db => { configureProvider(db); if (interceptor != null) db.AddInterceptors(interceptor); };
            if (registration == 1) services.AddDbContextPool<AppDbContext>(configure, 1);
            else if (registration == 2) services.AddDbContextFactory<AppDbContext>(configure);
            else if (registration == 3) services.AddDbContext<AppDbContext>(configure, contextLifetime: ServiceLifetime.Transient);
            else services.AddDbContext<AppDbContext>(configure);
            services.AddTickerQ(options =>
            {
                if (!runtimeEnabled) options.DisableBackgroundServices();
                if (!legacyPartition) options.UseApplicationNamespace("staged-jobs");
                if (withStore) options.AddOperationalStore(ef => ef
                    .UseApplicationDbContext<AppDbContext>(ConfigurationType.UseModelCustomizer));
                configureTickerQ?.Invoke(options);
            });
            services.AddSingleton(Notifications);
            services.AddSingleton(Scheduler);
            services.AddSingleton(Dispatcher);
            if (withCronCache)
            {
                var redis = Substitute.For<ITickerQRedisContext>();
                redis.HasRedisConnection.Returns(true);
                redis.DistributedCache.Returns(Cache);
                redis.GetOrSetArrayAsync(Arg.Any<string>(),
                        Arg.Any<Func<CancellationToken, Task<CronTickerEntity[]>>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(async call => _cachedCrons ??= await call.ArgAt<Func<CancellationToken, Task<CronTickerEntity[]>>>(1)(call.ArgAt<CancellationToken>(3)));
                Cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
                {
                    _cachedCrons = null;
                    return Task.CompletedTask;
                });
                services.AddSingleton(redis);
            }
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }

        public async Task InitializeAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        }

        public async Task<(int Orders, int Jobs)> Counts()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return (await db.Orders.CountAsync(), await db.Set<TimeTickerEntity>().CountAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            File.Delete(_file);
            TickerFunctionProvider.Build();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SaveChanges_CommitsBusinessAndTickerInSameScopedContext(int registration)
    {
        await using var h = new Harness(registration);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var order = new Order();
        var job = Job();
        db.Orders.Add(order);

        Assert.Same(job, await manager.StageAsync(job));
        Assert.Equal(EntityState.Added, db.Entry(job).State);
        Assert.Equal((0, 0), await h.Counts());
        Assert.Empty(h.Notifications.ReceivedCalls());
        Assert.Empty(h.Dispatcher.ReceivedCalls());
        var previous = db.Database.AutoTransactionBehavior;
        await db.SaveChangesAsync();

        Assert.Equal((1, 1), await h.Counts());
        Assert.Equal(previous, db.Database.AutoTransactionBehavior);
        Assert.Equal(TickerStatus.Idle, job.Status);
        Assert.Equal(job.Id, job.ChainRootId);
        Assert.Equal(new TickerQRuntimePartition("staged-jobs").StorageKey, job.ApplicationNamespaceKey);
        await h.Notifications.Received(1).AddTimeTickerNotifyAsync(job.Id);
        h.Scheduler.Received(1).RestartIfNeeded(job.ExecutionTime!.Value);
        Assert.Empty(h.Dispatcher.ReceivedCalls());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitTransaction_OnlyPublishesAfterCommit_AndRollbackRemovesBoth(bool commit)
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var job = Job();
        db.Orders.Add(new Order());
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(job);
        await db.SaveChangesAsync();
        Assert.Equal((0, 0), await h.Counts());
        Assert.Empty(h.Notifications.ReceivedCalls());
        Assert.Empty(h.Scheduler.ReceivedCalls());
        if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        Assert.Equal(commit ? (1, 1) : (0, 0), await h.Counts());
        await h.Notifications.Received(commit ? 1 : 0).AddTimeTickerNotifyAsync(job.Id);
    }

    [Fact]
    public async Task DisposedScopeWithoutSaving_DiscardsStagedWork_AndDoesNotLeakIntoPooledContext()
    {
        await using var h = new Harness(1);
        await h.InitializeAsync();
        await using (var scope = h.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.Add(new Order());
            await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(Job());
        }
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.Add(new Order());
            await db.SaveChangesAsync();
        }
        Assert.Equal((1, 0), await h.Counts());
        Assert.Empty(h.Notifications.ReceivedCalls());
    }

    [Fact]
    public async Task BusinessWriteFailure_RollsBackTicker_AndCanRetrySave()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Name = null! };
        db.Orders.Add(order);
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(Job());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal((0, 0), await h.Counts());
        Assert.Empty(h.Notifications.ReceivedCalls());
        order.Name = "fixed";
        await db.SaveChangesAsync();
        Assert.Equal((1, 1), await h.Counts());
    }

    [Fact]
    public async Task DuplicateTickerWrite_RollsBackBusinessChange()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        var id = Guid.NewGuid();
        await using (var first = h.Services.CreateAsyncScope())
        {
            var job = Job(); job.Id = id;
            await first.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(job);
            await first.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        }
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order());
        var duplicate = Job(); duplicate.Id = id;
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal((0, 1), await h.Counts());
    }

    [Fact]
    public async Task AddAsync_RemainsIndependent_AndRootScopeCannotStage()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        var rootManager = h.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        Assert.Contains("DI scope", (await Assert.ThrowsAsync<InvalidOperationException>(() => rootManager.StageAsync(Job()))).Message);
        await using var scope = h.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.Add(new Order());
        var added = await rootManager.AddAsync(Job());
        Assert.True(added.IsSucceeded, added.Exception?.ToString());
        Assert.Equal((0, 1), await h.Counts());
    }

    [Fact]
    public async Task MissingIntegration_ThrowsInsteadOfPersisting()
    {
        await using var h = new Harness(withStore: false);
        await using var scope = h.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        Assert.Contains("UseApplicationDbContext", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StageAsync(Job()))).Message);
    }

    [Fact]
    public async Task InvalidRequestOrCycle_IsRejectedBeforeTracking()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAnyAsync<Exception>(() => manager.StageAsync(new() { Function = "unregistered" }));
        var cycle = Job(); cycle.Children.Add(cycle);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StageAsync(cycle));
        Assert.Empty(db.ChangeTracker.Entries<TimeTickerEntity>());
    }

    [Fact]
    public async Task Chain_StagesCompleteGraph_AndRevalidatesBeforeSaving()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var root = Job(); var child = Job(); var grandchild = Job();
        root.Children.Add(child); child.Children.Add(grandchild);
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(root);
        grandchild.Function = "invalid-after-staging";
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Equal((0, 0), await h.Counts());
        grandchild.Function = root.Function;
        await db.SaveChangesAsync();
        Assert.Equal((0, 3), await h.Counts());
        Assert.Equal(root.Id, grandchild.ChainRootId);
        Assert.Equal(child.Id, grandchild.ParentId);
        Assert.Null(child.ExecutionTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialGraphRemoval_RejectsSave_AndClearDiscardsStagedWork(bool detachRoot)
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var root = Job(); var child = Job(); root.Children.Add(child);
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(root);
        if (detachRoot) db.Entry(root).State = EntityState.Detached;
        else root.Children.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal((0, 0), await h.Counts());
        db.ChangeTracker.Clear();
        db.Orders.Add(new Order());
        await db.SaveChangesAsync();
        Assert.Equal((1, 0), await h.Counts());
    }

    [Fact]
    public async Task CanceledSave_RestoresTransactionBehavior_AndCanRetry()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.StageAsync(Job(), new CancellationToken(true)));
        Assert.Empty(db.ChangeTracker.Entries());
        db.Orders.Add(new Order());
        await manager.StageAsync(Job());
        var previous = db.Database.AutoTransactionBehavior;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.SaveChangesAsync(new CancellationToken(true)));
        Assert.Equal(previous, db.Database.AutoTransactionBehavior);
        Assert.Equal((0, 0), await h.Counts());
        Assert.Empty(h.Notifications.ReceivedCalls());
        await db.SaveChangesAsync();
        Assert.Equal((1, 1), await h.Counts());
    }

    [Fact]
    public async Task TransientContextRegistration_ThrowsBeforeStagingIntoAnotherContext()
    {
        await using var h = new Harness(registration: 3);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(Job()));
        Assert.Contains("scoped lifetime", error.Message);
        Assert.Equal((0, 0), await h.Counts());
    }

    [Fact]
    public async Task LegacyPartition_AutomaticAndSerializableExplicitTransactionsWork()
    {
        await using var h = new Harness(legacyPartition: true);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        await manager.StageAsync(Job());
        await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await manager.StageAsync(Job());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Equal((0, 2), await h.Counts());
    }

    [Fact]
    public async Task NotificationFailure_DoesNotFailCommit_AndDatabaseRecoveryFindsJob()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        h.Notifications.AddTimeTickerNotifyAsync(Arg.Any<Guid>()).Returns(Task.FromException(new InvalidOperationException("offline")));
        await using var scope = h.Services.CreateAsyncScope();
        var job = Job(); job.ExecutionTime = DateTime.UtcNow.AddMinutes(-1);
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(job);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        var provider = h.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var recovered = new List<TimeTickerEntity>();
        await foreach (var item in provider.QueueTimedOutTimeTickers()) recovered.Add(item);
        Assert.Equal(job.Id, Assert.Single(recovered).Id);
    }

    [Fact]
    public async Task SynchronousSaveAndCommit_AndSaveChangesFalse_PreserveEfSemantics()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        using var scope = h.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var tx = db.Database.BeginTransaction();
        var job = Job();
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(job);
        db.SaveChanges(acceptAllChangesOnSuccess: false);
        Assert.Equal(EntityState.Added, db.Entry(job).State);
        Assert.Empty(h.Notifications.ReceivedCalls());
        tx.Commit();
        db.ChangeTracker.AcceptAllChanges();
        Assert.Equal((0, 1), await h.Counts());
        await h.Notifications.Received(1).AddTimeTickerNotifyAsync(job.Id);
    }

    [Fact]
    public async Task SeparateScopes_OnlySaveTheirOwnStagedJobs()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var first = h.Services.CreateAsyncScope();
        await using var second = h.Services.CreateAsyncScope();
        var firstJob = Job(); var secondJob = Job();
        await first.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(firstJob);
        await second.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(secondJob);
        await first.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        var provider = h.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        Assert.NotNull(await provider.GetTimeTickerById(firstJob.Id));
        Assert.Null(await provider.GetTimeTickerById(secondJob.Id));
    }

    [Fact]
    public async Task SavepointRollback_DoesNotNotifyOrPersistRolledBackJob()
    {
        await using var h = new Harness();
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var retained = Job();
        await manager.StageAsync(retained);
        await db.SaveChangesAsync();
        await tx.CreateSavepointAsync("before_second_job");
        var discarded = Job();
        await manager.StageAsync(discarded);
        await db.SaveChangesAsync();
        await tx.RollbackToSavepointAsync("before_second_job");
        await tx.CommitAsync();
        Assert.Equal((0, 1), await h.Counts());
        await h.Notifications.Received(1).AddTimeTickerNotifyAsync(retained.Id);
        await h.Notifications.DidNotReceive().AddTimeTickerNotifyAsync(discarded.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivationRevokedAfterStaging_RejectsEntireSave(bool includeTimeTicker)
    {
        await using var h = new Harness(runtimeEnabled: true);
        await h.InitializeAsync();
        TickerFunctionProvider.Build();
        var options = h.Services.GetRequiredService<SchedulerOptionsBuilder>();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(new TickerQStoreMetadata
        {
            ApplicationNamespaceKey = options.RuntimePartition.StorageKey,
            Id = options.RuntimeActivationScope.ScopeKey, ActivationEpoch = options.RuntimeActivationEpoch,
            ActivationPhase = ActivationEpochPhase.Activated
        });
        await db.SaveChangesAsync();
        h.Services.GetRequiredService<ITickerQActivationGate>().SignalActivated();
        db.Orders.Add(new Order());
        if (includeTimeTicker)
            await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(Job());
        await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(Cron());
        await db.Set<TickerQStoreMetadata>().ExecuteUpdateAsync(set => set
            .SetProperty(x => x.ActivationPhase, ActivationEpochPhase.Activating));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Contains("activation epoch", error.ToString());
        Assert.Equal((0, 0), await h.Counts());
        Assert.Equal(0, await CronCount(h));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientWriteOrCommitFailure_RetriesAtomicSave(bool atCommit)
    {
        var fault = new TransientFailure(atCommit);
        await using var h = new Harness(interceptor: fault, retry: true);
        await h.InitializeAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order());
        var job = Job();
        await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(job);
        fault.Armed = true;
        await db.SaveChangesAsync();
        Assert.Equal(1, fault.Failures);
        Assert.Equal((1, 1), await h.Counts());
        await h.Notifications.Received(1).AddTimeTickerNotifyAsync(job.Id);
    }

    private sealed class TransientFailure(bool atCommit, string table = "TimeTickers") : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public bool Armed;
        public int Failures;
        private void Fail()
        {
            if (Armed && Failures == 0) { Failures++; throw new RetryableAdmissionTestException(); }
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (!atCommit && command.CommandText.Contains($"INSERT INTO \"{table}\"")) Fail();
            return new(result);
        }
        public ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken ct = default)
        { if (atCommit) Fail(); return new(result); }
    }

    [PostgreSqlRaceFact]
    public async Task PostgreSql_WithProviderRetries_CommitRollbackAndAcquisitionRespectTransactionBoundary()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await using var h = new Harness(configureProvider: db => db.UseNpgsql(
            postgres.GetConnectionString(), pg => pg.EnableRetryOnFailure()));
        await h.InitializeAsync();
        foreach (var commit in new[] { false, true })
        {
            await using var scope = h.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                db.Orders.Add(new Order());
                var job = Job();
                await manager.StageAsync(job);
                var cron = Cron(); cron.Id = job.Id;
                await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(cron);
                await db.SaveChangesAsync();
                var provider = h.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
                Assert.Null(await provider.GetTimeTickerById(job.Id));
                Assert.Null(await provider.GetCronTickerById(cron.Id, CancellationToken.None));
                Assert.Empty(await provider.GetAllCronTickerExpressions(CancellationToken.None));
                if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
                Assert.Equal(commit ? (1, 1) : (0, 0), await h.Counts());
                Assert.Equal(commit ? 1 : 0, await CronCount(h));
                Assert.Equal(commit ? 1 : 0, (await provider.GetAllCronTickerExpressions(CancellationToken.None)).Length);
                var acquired = await provider.AcquireImmediateTimeTickersAsync([job.Id], CancellationToken.None);
                Assert.Equal(commit ? 1 : 0, acquired.Length);
            });
        }
        // Also cover EF's own transaction/retry strategy, without a user transaction.
        await using var automatic = h.Services.CreateAsyncScope();
        await automatic.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(Job());
        await automatic.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(Cron());
        await automatic.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        Assert.Equal((1, 2), await h.Counts());
        Assert.Equal(2, await CronCount(h));
    }
}
