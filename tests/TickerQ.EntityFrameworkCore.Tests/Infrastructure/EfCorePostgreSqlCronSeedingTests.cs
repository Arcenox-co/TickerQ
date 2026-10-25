using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit.Abstractions;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

[Collection("PostgreSql MVCC")]
public sealed class EfCorePostgreSqlCronSeedingTests(ITestOutputHelper output)
{
    [PostgreSqlRaceFact]
    public async Task GraphMutations_RecoverFromConcurrentSentinelWrites_WithAndWithoutProviderRetries()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("tickerq_graph_contention")
            .WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        foreach (var providerRetries in new[] { false, true })
        {
            var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), provider =>
                {
                    if (providerRetries) provider.EnableRetryOnFailure();
                }).Options;
            await using (var reset = new TestTickerQDbContext(options))
                await reset.Database.EnsureDeletedAsync();
            await EfGraphMutationContentionContract.ConcurrentFirstWritersAndAcquirersAsync(options);
            await using (var reset = new TestTickerQDbContext(options))
                await reset.Database.EnsureDeletedAsync();
            await EfGraphMutationContentionContract.SnapshotConflict_ReplaysInsertAndAcquisitionAsync(options);
        }
    }

    [PostgreSqlRaceFact]
    public async Task ActivationMetadata_PersistsCompleteRuntimeKey_InFreshAndUpgradedSchemas()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("tickerq_metadata")
            .WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await StoreMetadataActivationContract.VerifyAsync(options);
    }

    [PostgreSqlRaceFact]
    public async Task EightyConcurrentReconcilers_AllSucceedWithOneSeed_WithAndWithoutProviderRetries()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("tickerq_seed_race")
            .WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        var schemaOptions = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var schema = new TestTickerQDbContext(schemaOptions))
            await schema.Database.EnsureCreatedAsync();

        foreach (var namespaced in new[] { false, true })
        foreach (var providerRetries in new[] { false, true })
        {
            await using (var cleanup = new TestTickerQDbContext(schemaOptions))
                await cleanup.Set<CronTickerEntity>().ExecuteDeleteAsync();
            await VerifyConcurrentReconciliationAsync(postgres.GetConnectionString(), namespaced, providerRetries);
        }
    }

    private async Task VerifyConcurrentReconciliationAsync(string connectionString, bool namespaced, bool providerRetries)
    {
        const int replicas = 80;
        const string applicationNamespace = "seed-race";
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseNpgsql(connectionString, provider =>
            {
                if (providerRetries) provider.EnableRetryOnFailure();
            }).Options;
        var services = new List<ServiceProvider>();
        var providers = new List<TestableProvider>();
        var seed = new DefinedCronTickerSeed("concurrent-seed-repro", "0 */5 * * * *");
        var manifest = namespaced
            ? new DefinedCronSeedManifest(applicationNamespace, [seed])
            : new DefinedCronSeedManifest([seed]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            for (var index = 0; index < replicas; index++)
            {
                var clock = Substitute.For<ITickerClock>();
                clock.UtcNow.Returns(_ => DateTime.UtcNow);
                var sp = new ServiceCollection()
                    .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
                        new PooledDbContextFactory<TestTickerQDbContext>(options))
                    .BuildServiceProvider();
                services.Add(sp);
                var scheduler = new SchedulerOptionsBuilder { NodeIdentifier = $"seed-race-{index}" };
                if (namespaced) scheduler.BindRuntimeActivationScope(applicationNamespace, 1, true);
                providers.Add(new TestableProvider(sp, clock, scheduler, Substitute.For<ITickerQRedisContext>()));
            }

            // Each caller has its own provider and context factory, as separate replicas do. Re-run
            // with an existing row to cover heartbeat contention as well as the initial insert race.
            foreach (var phase in new[] { "initial insert", "existing seed" })
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tasks = providers.Select(async provider =>
                {
                    await gate.Task;
                    using var admission = namespaced
                        ? StartupSeederAdmissionContext.Enter(new ReconciliationActivationScope(applicationNamespace).ScopeKey, 1)
                        : null;
                    return await Record.ExceptionAsync(() => provider.MigrateDefinedCronTickers(manifest, timeout.Token));
                }).ToArray();
                gate.SetResult();
                var results = await Task.WhenAll(tasks);
                var errors = results.Where(error => error != null).ToArray();
                output.WriteLine($"{phase}: namespaced={namespaced}, providerRetries={providerRetries}, " +
                                 $"succeeded={replicas - errors.Length}/{replicas}");
                Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error!.ToString())));

                await using var verify = new TestTickerQDbContext(options);
                var row = Assert.Single(await verify.Set<CronTickerEntity>().AsNoTracking().ToArrayAsync());
                Assert.Equal(CronSeedIdentity.DeterministicId(manifest.SeedKeyFor(seed)), row.Id);
                Assert.Equal(manifest.SeedKeyFor(seed), row.SeedKey);
                Assert.Equal(seed.Expression, row.Expression);
                Assert.Equal(1, row.DefinitionRevision);
                Assert.True(row.IsEnabled);
                Assert.Null(row.RetiredAt);
            }
        }
        finally
        {
            foreach (var sp in services) await sp.DisposeAsync();
        }
    }
}
