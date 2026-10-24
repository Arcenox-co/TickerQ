using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TICKERQ_SQLSERVER_CONNECTION")))
            Skip = "Set TICKERQ_SQLSERVER_CONNECTION to an isolated SQL Server test instance.";
    }
}

public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TICKERQ_SQLSERVER_CONNECTION")))
            Skip = "Set TICKERQ_SQLSERVER_CONNECTION to an isolated SQL Server test instance.";
    }
}

internal sealed class SqlServerTestDatabase : IAsyncDisposable
{
    public string ConnectionString { get; } = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("TICKERQ_SQLSERVER_CONNECTION"))
    { InitialCatalog = "tickerq_test_" + Guid.NewGuid().ToString("N"), TrustServerCertificate = true }.ConnectionString;

    public async ValueTask DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlServer(ConnectionString).Options;
        await using var db = new TestTickerQDbContext(options);
        await db.Database.EnsureDeletedAsync();
    }
}

public class SqlServerRuntimeTests
{
    [SqlServerFact]
    public async Task GraphMutations_ConcurrentFirstWritersAndAcquirersConverge_WithProviderRetries()
    {
        await using var database = new SqlServerTestDatabase();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            // SqlClient does not expose deadlocks through DbException.IsTransient.
            // Keep its error-number detection in EF's recommended provider strategy.
            .UseSqlServer(database.ConnectionString, provider => provider.EnableRetryOnFailure()).Options;
        await EfGraphMutationContentionContract.ConcurrentFirstWritersAndAcquirersAsync(options);
    }

    [SqlServerFact]
    public async Task ActivationMetadata_PersistsCompleteRuntimeKey()
    {
        await using var database = new SqlServerTestDatabase();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlServer(database.ConnectionString).Options;
        await StoreMetadataActivationContract.VerifyAsync(options);
    }

    public sealed class CustomSeedTableModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<CronTickerEntity>().ToTable("Defined]Cron", "schedule]owner");
        }
    }

    [SqlServerTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ConcurrentSeedAndIdleEdit_WorkOnSqlServer(bool enableRetries, bool customMapping)
    {
        await using var database = new SqlServerTestDatabase();
        var optionsBuilder = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlServer(database.ConnectionString, sql =>
            {
                sql.CommandTimeout(60);
                if (enableRetries) sql.EnableRetryOnFailure();
            });
        if (customMapping) optionsBuilder.ReplaceService<IModelCustomizer, CustomSeedTableModelCustomizer>();
        var dbOptions = optionsBuilder.Options;
        await using (var schema = new TestTickerQDbContext(dbOptions)) await schema.Database.EnsureCreatedAsync();
        using var services = new ServiceCollection().AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(dbOptions)).BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        var observedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(_ => observedAt);
        var providers = Enumerable.Range(0, 16).Select(i => new TestableProvider(services, clock,
            new SchedulerOptionsBuilder { NodeIdentifier = "sql-test-" + i }, Substitute.For<ITickerQRedisContext>())).ToArray();
        var manifest = new DefinedCronSeedManifest([new DefinedCronTickerSeed("sql-seed", "0 */5 * * * *")]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        // Exercise both the absent-row insert race and the existing-row heartbeat upgrade race.
        // All sixteen independent reconcilers must finish, including without provider retries.
        for (var round = 0; round < 2; round++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var seeds = providers.Select(async provider =>
            {
                await start.Task;
                await provider.MigrateDefinedCronTickers(manifest, timeout.Token);
                return true;
            }).ToArray();
            start.SetResult();
            var completed = await Task.WhenAll(seeds);
            Assert.Equal(16, completed.Length);
            Assert.All(completed, succeeded => Assert.True(succeeded));
            await using var roundVerification = new TestTickerQDbContext(dbOptions);
            var seeded = Assert.Single(await roundVerification.Set<CronTickerEntity>()
                .AsNoTracking().ToArrayAsync(timeout.Token));
            Assert.Equal(observedAt, seeded.SeedLastSeenAt);
            observedAt = observedAt.AddMinutes(1);
        }
        var job = new TimeTickerEntity { Function = "before", ExecutionTime = DateTime.UtcNow.AddHours(1) };
        Assert.Equal(1, await providers[0].AddTimeTickers([job], timeout.Token));
        job.Description = "edited";
        Assert.Equal(1, await providers[0].UpdateTimeTickers([job], timeout.Token));
        Assert.Equal("edited", (await providers[0].GetTimeTickerById(job.Id, timeout.Token)).Description);
    }
}

public sealed partial class EfTransactionalSchedulingTests
{
    [SqlServerFact]
    public async Task SqlServer_StagedTimeAndCronCommitOrRollbackWithBusinessData()
    {
        await using var database = new SqlServerTestDatabase();
        await using var h = new Harness(configureProvider: db => db.UseSqlServer(database.ConnectionString,
            sql => sql.EnableRetryOnFailure()));
        await h.InitializeAsync();
        foreach (var commit in new[] { false, true })
        {
            await using var scope = h.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Orders.Add(new Order());
                await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().StageAsync(Job());
                await scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>().StageAsync(Cron());
                await db.SaveChangesAsync();
                if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            });
            // SQL Server's default isolation can block outside reads before commit;
            // verify the atomic outcome from a fresh scope after the transaction ends.
            Assert.Equal(commit ? (1, 1) : (0, 0), await h.Counts());
            Assert.Equal(commit ? 1 : 0, await CronCount(h));
        }
    }
}
