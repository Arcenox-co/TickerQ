using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfApplicationPartitionTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TestTickerQDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        await using var context = new TestTickerQDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task IdenticalIds_AreIsolatedAcrossWriteReadAcquireAndDelete()
    {
        var id = Guid.NewGuid();
        var providerA = CreateProvider("ef-app-a");
        var providerB = CreateProvider("ef-app-b");
        await providerA.AddTimeTickers([Ticker(id, "function-a")], CancellationToken.None);
        await providerB.AddTimeTickers([Ticker(id, "function-b")], CancellationToken.None);

        Assert.Equal("function-a", (await providerA.GetTimeTickerById(id)).Function);
        Assert.Equal("function-b", (await providerB.GetTimeTickerById(id)).Function);
        Assert.Single(await providerA.AcquireImmediateTimeTickersAsync([id], CancellationToken.None));
        Assert.Single(await providerB.AcquireImmediateTimeTickersAsync([id], CancellationToken.None));
        Assert.Equal(0, await providerA.RemoveTimeTickers([id], CancellationToken.None));
        Assert.NotNull(await providerA.GetTimeTickerById(id));
        Assert.NotNull(await providerB.GetTimeTickerById(id));

        await using (var release = new TestTickerQDbContext(_options))
        {
            var partitionA = new TickerQRuntimePartition("ef-app-a").StorageKey;
            await release.Set<TimeTickerEntity>()
                .Where(x => x.ApplicationNamespaceKey == partitionA && x.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, TickerStatus.Done)
                    .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTime?)null));
        }

        Assert.Equal(1, await providerA.RemoveTimeTickers([id], CancellationToken.None));
        Assert.Null(await providerA.GetTimeTickerById(id));
        Assert.NotNull(await providerB.GetTimeTickerById(id));
    }

    [Fact]
    public async Task DefinedCronReconciliation_DoesNotRetireAnotherPartitionsSeed()
    {
        var providerA = CreateProvider("ef-reconciler-a");
        var providerB = CreateProvider("ef-reconciler-b");
        await providerB.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("ef-reconciler-b",
                [new DefinedCronTickerSeed("foreign-seed", "*/5 * * * *", 1, null)]),
            CancellationToken.None);
        var before = Assert.Single(await providerB.GetCronTickers(
            x => x.Function == "foreign-seed", CancellationToken.None));

        await providerA.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("ef-reconciler-a", Array.Empty<DefinedCronTickerSeed>()),
            CancellationToken.None);

        var after = await providerB.GetCronTickerById(before.Id, CancellationToken.None);
        Assert.NotNull(after);
        Assert.True(after!.IsEnabled);
        Assert.Null(after.RetirementRequestedAt);
        Assert.Null(after.RetiredAt);
    }

    [Fact]
    public async Task ExplicitLegacyAdoption_IsAtomicIdempotentAndOwnerFenced()
    {
        var id = Guid.NewGuid();
        var legacy = CreateProvider(null);
        var target = CreateProvider("ef-adopter");
        await legacy.AddTimeTickers([Ticker(id, "legacy")], CancellationToken.None);
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("ef-adopter"), 31, legacyWritersDrained: true);

        target.AfterLegacyAdoptionLeaseForTestAsync = _ =>
            throw new OperationCanceledException("deterministic interruption");
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            target.AdoptLegacyRuntimePartitionAsync(adoption));
        target.AfterLegacyAdoptionLeaseForTestAsync = null;
        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        await target.AdoptLegacyRuntimePartitionAsync(adoption);

        Assert.Equal("legacy", (await target.GetTimeTickerById(id))!.Function);
        Assert.Null(await legacy.GetTimeTickerById(id));
        Assert.Equal(0, await legacy.AddTimeTickers(
            [Ticker(Guid.NewGuid(), "late-legacy-time")], CancellationToken.None));
        Assert.Equal(0, await legacy.InsertCronTickers(
            [new CronTickerEntity
            {
                Id = Guid.NewGuid(), Function = "late-legacy-cron", Expression = "*/5 * * * *",
                DefinitionRevision = 1, Request = [], CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("late-legacy-seed", "*/5 * * * *")], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateProvider("ef-other").AdoptLegacyRuntimePartitionAsync(
                new LegacyRuntimePartitionAdoption(new TickerQRuntimePartition("ef-other"), 31,
                    legacyWritersDrained: true)));
    }

    [Fact]
    public async Task ExplicitLegacyAdoption_MovesPrincipalsWithPersistedDependents()
    {
        var time = Ticker(Guid.NewGuid(), "time-with-result");
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "cron-with-result", Expression = "*/5 * * * *",
            DefinitionRevision = 1, Request = [], CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            Status = TickerStatus.Done, ExecutionTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        await using (var seed = new TestTickerQDbContext(_options))
        {
            seed.AddRange(time, cron, occurrence);
            await seed.SaveChangesAsync();
            seed.AddRange(
                new TimeTickerResultEntity<TimeTickerEntity>
                {
                    TickerId = time.Id, Payload = [1], EnvelopeVersion = 1,
                    MediaType = "application/json", ContractId = "time", ContractType = "time"
                },
                new CronTickerOccurrenceResultEntity<CronTickerEntity>
                {
                    TickerId = occurrence.Id, Payload = [2], EnvelopeVersion = 1,
                    MediaType = "application/json", ContractId = "cron", ContractType = "cron"
                });
            await seed.SaveChangesAsync();
        }

        var targetPartition = new TickerQRuntimePartition("ef-dependent-adopter");
        await CreateProvider(targetPartition.ApplicationNamespace).AdoptLegacyRuntimePartitionAsync(
            new LegacyRuntimePartitionAdoption(targetPartition, 32, legacyWritersDrained: true));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(targetPartition.StorageKey,
            (await verify.Set<TimeTickerEntity>().SingleAsync()).ApplicationNamespaceKey);
        Assert.Equal(targetPartition.StorageKey,
            (await verify.Set<CronTickerEntity>().SingleAsync()).ApplicationNamespaceKey);
        Assert.Equal(targetPartition.StorageKey,
            (await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().SingleAsync()).ApplicationNamespaceKey);
        Assert.Equal(targetPartition.StorageKey,
            (await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().SingleAsync()).ApplicationNamespaceKey);
        Assert.Equal(targetPartition.StorageKey,
            (await verify.Set<CronTickerOccurrenceResultEntity<CronTickerEntity>>().SingleAsync()).ApplicationNamespaceKey);
    }

    private TestableProvider CreateProvider(string? applicationNamespace)
    {
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope(applicationNamespace, 0, schedulerEnabled: false);
        var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(_options))
            .BuildServiceProvider();
        return new TestableProvider(services, Substitute.For<ITickerClock>(), scheduler,
            Substitute.For<ITickerQRedisContext>());
    }

    private static TimeTickerEntity Ticker(Guid id, string function) => new()
    {
        Id = id, Function = function, Status = TickerStatus.Idle,
        ExecutionTime = DateTime.UtcNow.AddMinutes(1), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
}
