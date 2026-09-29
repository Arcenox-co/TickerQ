using TickerQ.Tests.Shared.ProviderReliability;
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
}
