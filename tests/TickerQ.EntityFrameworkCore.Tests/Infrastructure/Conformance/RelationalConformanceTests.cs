using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.Conformance;

/// <summary>
/// Behavioral contract every supported relational database must satisfy. Run against a real server by
/// setting TICKERQ_CONFORMANCE_&lt;TARGET&gt; to an administrator connection string, for example
/// TICKERQ_CONFORMANCE_MARIADB="Server=localhost;Port=3306;User=root;Password=...".
/// </summary>
[Collection("Relational conformance")]
public sealed class RelationalConformanceTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Schema_is_created_and_certified_by_the_startup_pipeline(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        var options = database.Options();
        await using var context = new TestTickerQDbContext(options);
        await context.Database.EnsureCreatedAsync();

        await Pipeline().RunAsync(context);

        context.ChangeTracker.Clear();
        Assert.NotEmpty(await context.Set<TickerQStoreMetadata>().AsNoTracking().ToArrayAsync());
    }

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Startup_pipeline_rejects_a_same_name_index_without_unique_semantics(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        var options = database.Options();
        await using var context = new TestTickerQDbContext(options);
        await context.Database.EnsureCreatedAsync();
        foreach (var sql in ReplaceSeedKeyIndexWithNonUnique(context, target))
            await context.Database.ExecuteSqlRawAsync(sql);

        var error = await Assert.ThrowsAsync<TickerQStoreSchemaException>(() => Pipeline().RunAsync(context));

        Assert.Contains("UX_CronTickers_SeedKey", error.ToString());
    }

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Activation_metadata_round_trips_the_full_runtime_key(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        await StoreMetadataActivationContract.VerifyAsync(database.Options());
    }

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Concurrent_first_writers_and_acquirers_converge(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        await EfGraphMutationContentionContract.ConcurrentFirstWritersAndAcquirersAsync(database.Options());
    }

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Seed_key_index_allows_many_unseeded_rows_and_rejects_duplicate_seeds(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        var options = database.Options();
        await using (var schema = new TestTickerQDbContext(options))
            await schema.Database.EnsureCreatedAsync();

        // User and dashboard crons have no SeedKey; any number of them must coexist in one namespace.
        await using (var context = new TestTickerQDbContext(options))
        {
            context.AddRange(Cron(seedKey: null), Cron(seedKey: null), Cron(seedKey: null), Cron(seedKey: "seed-a"));
            await context.SaveChangesAsync();
        }

        await using (var context = new TestTickerQDbContext(options))
        {
            context.Add(Cron(seedKey: "seed-a"));
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using var verify = new TestTickerQDbContext(options);
        Assert.Equal(4, await verify.Set<CronTickerEntity>().CountAsync());
    }

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Ticker_columns_round_trip_without_loss(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        var options = database.Options();
        await using (var schema = new TestTickerQDbContext(options))
            await schema.Database.EnsureCreatedAsync();
        await using var services = Services(options);
        var provider = Provider(services, "round-trip");

        var executionTime = new DateTime(2026, 10, 24, 12, 30, 15, 123, DateTimeKind.Utc);
        var job = new TimeTickerEntity
        {
            Function = "conformance.round-trip",
            Description = "Exercise every mapped column type",
            ExecutionTime = executionTime,
            Request = [0, 1, 2, 254, 255],
            Retries = 3,
            RetryIntervals = [5, 30, 120],
            SchedulePolicyName = "EU business hours",
            BlockedOccurrenceBehavior = BlockedOccurrenceBehavior.MoveForward,
            MissedExecutionAction = MissedExecutionAction.Skip,
            MaximumLateness = TimeSpan.FromMinutes(90),
            TimeoutSeconds = 45,
        };
        Assert.Equal(1, await provider.AddTimeTickers([job], CancellationToken.None));
        var cron = Cron(seedKey: null);
        cron.TimeZoneId = "Europe/Tirane";
        cron.CatchUpMode = CronCatchUpMode.RunLatest;
        cron.MissedExecutionAction = MissedExecutionAction.Skip;
        cron.MaximumLateness = TimeSpan.FromSeconds(30);
        cron.SkipIfAlreadyRunning = true;
        Assert.Equal(1, await provider.InsertCronTickers([cron], CancellationToken.None));

        var storedJob = await provider.GetTimeTickerById(job.Id, CancellationToken.None);
        Assert.Equal(job.Function, storedJob.Function);
        Assert.Equal(job.Description, storedJob.Description);
        Assert.Equal(executionTime, storedJob.ExecutionTime!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(job.Request, storedJob.Request);
        Assert.Equal(job.Retries, storedJob.Retries);
        Assert.Equal(job.RetryIntervals, storedJob.RetryIntervals);
        Assert.Equal(job.SchedulePolicyName, storedJob.SchedulePolicyName);
        Assert.Equal(job.BlockedOccurrenceBehavior, storedJob.BlockedOccurrenceBehavior);
        Assert.Equal(job.MissedExecutionAction, storedJob.MissedExecutionAction);
        Assert.Equal(job.MaximumLateness, storedJob.MaximumLateness);
        Assert.Equal(job.TimeoutSeconds, storedJob.TimeoutSeconds);
        Assert.Equal(TickerStatus.Idle, storedJob.Status);

        var storedCron = await provider.GetCronTickerById(cron.Id, CancellationToken.None);
        Assert.Equal(cron.Expression, storedCron.Expression);
        Assert.Equal("Europe/Tirane", storedCron.TimeZoneId);
        Assert.Equal(CronCatchUpMode.RunLatest, storedCron.CatchUpMode);
        Assert.Equal(TimeSpan.FromSeconds(30), storedCron.MaximumLateness);
        Assert.True(storedCron.SkipIfAlreadyRunning);
        Assert.True(storedCron.IsEnabled);
    }

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Concurrent_seed_reconciliation_converges_on_one_definition(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        var options = database.Options();
        await using (var schema = new TestTickerQDbContext(options))
            await schema.Database.EnsureCreatedAsync();
        await using var services = Services(options);
        var observedAt = FixedNow;
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_ => observedAt);
        var providers = Enumerable.Range(0, 8).Select(i => new TestableProvider(services, clock,
            new SchedulerOptionsBuilder { NodeIdentifier = "seed-" + i }, Substitute.For<ITickerQRedisContext>())).ToArray();
        var manifest = new DefinedCronSeedManifest([new DefinedCronTickerSeed("conformance.seed", "0 */5 * * * *")]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        // First round races the absent-row insert; the second races the existing-row heartbeat.
        for (var round = 0; round < 2; round++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var seeds = providers.Select(async provider =>
            {
                await start.Task;
                await provider.MigrateDefinedCronTickers(manifest, timeout.Token);
            }).ToArray();
            start.SetResult();
            await Task.WhenAll(seeds);

            await using var verify = new TestTickerQDbContext(options);
            var seeded = Assert.Single(await verify.Set<CronTickerEntity>().AsNoTracking().ToArrayAsync(timeout.Token));
            Assert.Equal(observedAt, seeded.SeedLastSeenAt!.Value, TimeSpan.FromMilliseconds(1));
            observedAt = observedAt.AddMinutes(1);
        }
    }

    [RelationalConformanceTheory]
    [MemberData(nameof(RelationalConformanceData.Targets), MemberType = typeof(RelationalConformanceData))]
    public async Task Schedule_policy_store_enforces_unique_names_and_optimistic_versions(RelationalConformanceTarget target)
    {
        await using var database = await ConformanceDatabase.CreateAsync(target);
        var options = database.Options();
        await using (var schema = new TestTickerQDbContext(options))
            await schema.Database.EnsureCreatedAsync();
        await using var services = Services(options);
        ISchedulePolicyStore store = Provider(services, "policies");

        var created = await store.CreateSchedulePolicyAsync(SchedulePolicy.Create("EU business hours")
            .InTimeZone("Europe/Berlin")
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
            .Build());
        Assert.Equal(1, created.Version);

        await Assert.ThrowsAsync<DuplicateSchedulePolicyNameException>(() =>
            store.CreateSchedulePolicyAsync(SchedulePolicy.Create("eu  BUSINESS hours").Build()));

        var edit = created.Copy();
        edit.Description = "Weekdays only";
        var updated = await store.UpdateSchedulePolicyAsync(edit, expectedVersion: 1);
        Assert.Equal(2, updated.Version);
        Assert.Null(await store.UpdateSchedulePolicyAsync(edit, expectedVersion: 1));

        var reloaded = await store.GetSchedulePolicyByNameAsync("EU business hours");
        Assert.Equal("Weekdays only", reloaded.Description);
        Assert.Equal("Europe/Berlin", reloaded.TimeZoneId);
        Assert.Single(reloaded.AllowedTimeWindows);

        Assert.False(await store.DeleteSchedulePolicyAsync(created.Id, expectedVersion: 1));
        Assert.True(await store.DeleteSchedulePolicyAsync(created.Id, expectedVersion: 2));
        Assert.Empty(await store.ListSchedulePoliciesAsync());
    }

    private static EfCoreDataMigrationPipeline Pipeline()
        => new([new TimeTickerChainRootDataMigration<TimeTickerEntity>()]);

    private static CronTickerEntity Cron(string seedKey) => new()
    {
        Id = Guid.NewGuid(),
        Function = "conformance.cron",
        Expression = "0 */5 * * * *",
        SeedKey = seedKey,
        Request = [],
        CreatedAt = FixedNow,
        UpdatedAt = FixedNow,
    };

    private static ServiceProvider Services(DbContextOptions<TestTickerQDbContext> options)
        => new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options))
            .BuildServiceProvider();

    private static TestableProvider Provider(IServiceProvider services, string node)
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        return new TestableProvider(services, clock, new SchedulerOptionsBuilder { NodeIdentifier = node },
            Substitute.For<ITickerQRedisContext>());
    }

    private static IEnumerable<string> ReplaceSeedKeyIndexWithNonUnique(TestTickerQDbContext context, RelationalConformanceTarget target)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(CronTickerEntity))!;
        var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        var index = sql.DelimitIdentifier("UX_CronTickers_SeedKey");
        var qualifiedIndex = sql.DelimitIdentifier("UX_CronTickers_SeedKey", entity.GetSchema());
        var column = sql.DelimitIdentifier("SeedKey");
        yield return target switch
        {
            RelationalConformanceTarget.MySql or RelationalConformanceTarget.MariaDb
                or RelationalConformanceTarget.MariaDbMicroting => $"DROP INDEX {index} ON {table}",
            RelationalConformanceTarget.SqlServer => $"DROP INDEX {index} ON {table}",
            RelationalConformanceTarget.CockroachDb => $"DROP INDEX {qualifiedIndex} CASCADE",
            _ => $"DROP INDEX {qualifiedIndex}",
        };
        yield return $"CREATE INDEX {index} ON {table} ({column})";
    }
}

[CollectionDefinition("Relational conformance", DisableParallelization = true)]
public sealed class RelationalConformanceCollection;
