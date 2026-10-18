using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.Tests.Shared.ProviderReliability;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Tests;

/// <summary>
/// Runs the shared provider reliability contract against the MongoDB provider over a real
/// MongoDB container. These are genuine runtime contracts: without Docker/Testcontainers the
/// shared <see cref="MongoTestFixture"/> fails to start the container and every case errors
/// out — it never silently passes as a skipped runtime contract. CI (Task 23) is responsible
/// for guaranteeing the container is available.
///
/// The container is shared via the "Mongo" collection fixture, so this class adds no
/// additional expensive setup on top of <see cref="MongoPersistenceProviderTests"/>.
/// </summary>
[Collection("Mongo")]
public sealed class MongoProviderReliabilityContractTests : ProviderReliabilityContractTests, IAsyncLifetime
{
    private readonly MongoTestFixture _fixture;

    public MongoProviderReliabilityContractTests(MongoTestFixture fixture) => _fixture = fixture;

    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => _fixture.Provider;
    protected override DateTime Now => _fixture.FixedNow;
    protected override string OwnerId => _fixture.OwnerId;
    protected override SchedulerOptionsBuilder Options => _fixture.Options;

    public Task InitializeAsync() => _fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IntegrationSettings_RenameLegacyCollection_PreservesVersionAndEncryptedPayload()
    {
        var store = (ITickerIntegrationSettingsStore)Provider;
        var original = new IntegrationSettingsRecord(Guid.NewGuid(), "encrypted-settings");
        Assert.True(await store.SaveIntegrationSettingsAsync(original, Guid.Empty));
        var current = _fixture.TimeTickers.CollectionNamespace.CollectionName + "_IntegrationSettings";
        var legacy = _fixture.TimeTickers.CollectionNamespace.CollectionName + "_NotificationSettings";
        // Recreate the previous release's storage shape without knowing its private partition key.
        await _fixture.Database.RenameCollectionAsync(current, legacy, new RenameCollectionOptions { DropTarget = false });

        await _fixture.Database.RenameCollectionAsync(legacy, current, new RenameCollectionOptions { DropTarget = false });

        Assert.Equal(original, await store.ReadIntegrationSettingsAsync());
        // The migration must fail rather than replace a populated destination collection.
        await _fixture.Database.GetCollection<BsonDocument>(legacy).InsertOneAsync(new BsonDocument("_id", "other"));
        try
        {
            await Assert.ThrowsAsync<MongoCommandException>(() => _fixture.Database.RenameCollectionAsync(
                legacy, current, new RenameCollectionOptions { DropTarget = false }));
            Assert.Equal(original, await store.ReadIntegrationSettingsAsync());
        }
        finally
        {
            await _fixture.Database.DropCollectionAsync(legacy);
        }
    }
}
