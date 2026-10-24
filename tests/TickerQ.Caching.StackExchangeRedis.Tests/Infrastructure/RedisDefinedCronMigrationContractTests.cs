using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Tests.Shared.ProviderReliability;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

[Collection("RedisRealScript")]
public sealed class RedisDefinedCronMigrationContractTests : DefinedCronMigrationContractTests, IAsyncLifetime
{
    private static readonly DateTime FixedNow = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly RedisRealScriptFixture _fixture;
    private readonly TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider;

    public RedisDefinedCronMigrationContractTests(RedisRealScriptFixture fixture)
    {
        _fixture = fixture;
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(FixedNow);
        _provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            fixture.Db,
            clock,
            new SchedulerOptionsBuilder { NodeIdentifier = "redis-namespaced-contract" },
            new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => _provider;
    protected override DateTime Now => FixedNow;

    public Task InitializeAsync()
    {
        _fixture.Db.Execute("FLUSHALL");
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Namespaced_ConcurrentSameFunctionInDistinctApplications_CreatesIndependentRows()
    {
        const string function = "redis-concurrent-distinct-owners";
        var appA = new DefinedCronSeedManifest("redis-app-a",
            [new DefinedCronTickerSeed(function, "*/5 * * * *")]);
        var appB = new DefinedCronSeedManifest("redis-app-b",
            [new DefinedCronTickerSeed(function, "*/9 * * * *")]);

        await Task.WhenAll(
            Provider.MigrateDefinedCronTickers(appA, CancellationToken.None),
            Provider.MigrateDefinedCronTickers(appB, CancellationToken.None));

        var rows = await Provider.GetCronTickers(x => x.Function == function, CancellationToken.None);
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, x => x.SeedOwnerNamespace == "redis-app-a"
            && x.Id == CronSeedIdentity.DeterministicId(CronSeedIdentity.SeedKey("redis-app-a", function)));
        Assert.Contains(rows, x => x.SeedOwnerNamespace == "redis-app-b"
            && x.Id == CronSeedIdentity.DeterministicId(CronSeedIdentity.SeedKey("redis-app-b", function)));
    }

    [Fact]
    public async Task Namespaced_ConcurrentLegacyAdoption_ConvergesOnOriginalRow()
    {
        const string function = "redis-concurrent-legacy-adoption";
        var legacy = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "*/5 * * * *",
            SeedKey = null,
            InitIdentifier = $"MemoryTicker_Seeded_{function}",
            Request = [],
            IsEnabled = true,
            CreatedAt = FixedNow.AddDays(-1),
            UpdatedAt = FixedNow.AddDays(-1)
        };
        await Provider.InsertCronTickers([legacy], CancellationToken.None);
        var manifest = new DefinedCronSeedManifest("redis-adopter",
            [new DefinedCronTickerSeed(function, "*/7 * * * *")],
            new Dictionary<string, string>(StringComparer.Ordinal) { [function] = "redis-adopter" });

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Provider.MigrateDefinedCronTickers(manifest, CancellationToken.None)));

        var row = Assert.Single(await Provider.GetCronTickers(x => x.Function == function, CancellationToken.None));
        Assert.Equal(legacy.Id, row.Id);
        Assert.Equal("redis-adopter", row.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey("redis-adopter", function), row.SeedKey);
    }
}