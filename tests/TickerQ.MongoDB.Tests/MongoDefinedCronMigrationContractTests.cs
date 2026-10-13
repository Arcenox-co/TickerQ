using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.Tests.Shared.ProviderReliability;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

/// <summary>
/// Runs the shared <see cref="DefinedCronMigrationContractTests"/> against the MongoDB provider over a
/// real MongoDB container, mirroring <see cref="MongoProviderReliabilityContractTests"/>. The container
/// is shared via the "Mongo" collection fixture, so no additional expensive setup is introduced.
/// </summary>
[Collection("Mongo")]
public sealed class MongoDefinedCronMigrationContractTests : DefinedCronMigrationContractTests, IAsyncLifetime
{
    private readonly MongoTestFixture _fixture;

    public MongoDefinedCronMigrationContractTests(MongoTestFixture fixture) => _fixture = fixture;

    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => _fixture.Provider;
    protected override DateTime Now => _fixture.FixedNow;

    public Task InitializeAsync() => _fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExpressionChange_QuarantinesResultBackedPendingHistory_WithoutDeletingResult()
    {
        const string function = "mongo-result-backed-pending";
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "*/5 * * * *",
            SeedKey = CronSeedIdentity.SeedKeyForFunction(function),
            InitIdentifier = $"MemoryTicker_Seeded_{function}",
            Request = Array.Empty<byte>(),
            IsEnabled = true,
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1),
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = Now.AddMinutes(5),
            Status = TickerStatus.Idle,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        await Provider.InsertCronTickers([cron], CancellationToken.None);
        await Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        await _fixture.Database.GetCollection<BsonDocument>("ticker_TickerResults").InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(occurrence.Id, GuidRepresentation.Standard),
            ["Kind"] = "cron-occurrence",
            ["Payload"] = new BsonBinaryData(new byte[] { 1, 2, 3 }),
            ["Version"] = 1,
            ["MediaType"] = "application/json",
        });

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(function, "*/9 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(await Provider.GetAllCronTickerOccurrences(
            x => x.Id == occurrence.Id, CancellationToken.None));
        Assert.Equal(TickerStatus.Skipped, persisted.Status);
        Assert.NotNull(await Provider.GetCronTickerOccurrenceResultAsync(occurrence.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DocumentedLegacySeedKeys_AreAdoptedInPlace(bool namespacedLegacyKey)
    {
        const string function = "mongo-documented-legacy-key";
        const string owner = "mongo-legacy-key-owner";
        const string stableId = "stable-definition";
        var id = Guid.NewGuid();
        var legacyKey = CronSeedIdentity.LegacyAdoptionKeys(owner, stableId)[namespacedLegacyKey ? 0 : 1];
        await Provider.InsertCronTickers([new CronTickerEntity
        {
            Id = id, Function = function, Expression = "*/5 * * * *", SeedKey = legacyKey,
            InitIdentifier = $"MemoryTicker_Seeded_{function}", Request = Array.Empty<byte>(),
            IsEnabled = true, CreatedAt = Now, UpdatedAt = Now
        }], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest(owner,
                [new DefinedCronTickerSeed(function, "*/7 * * * *", stableDefinitionId: stableId)],
                new Dictionary<string, string>(StringComparer.Ordinal) { [function] = owner }),
            CancellationToken.None);

        var row = Assert.Single(await Provider.GetCronTickers(x => x.Function == function, CancellationToken.None));
        Assert.Equal(id, row.Id);
        Assert.Equal(owner, row.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey(owner, stableId), row.SeedKey);
    }
}
