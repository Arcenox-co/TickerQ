using TickerQ.Tests.Shared;
using MongoDB.Driver;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class OverdueAcquisitionTests(MongoTestFixture fixture) : OverdueAcquisitionContract<TimeTickerEntity, CronTickerEntity>, IAsyncLifetime
{
    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => fixture.Provider;
    protected override DateTime Now => fixture.FixedNow;
    public async Task InitializeAsync() { await fixture.DropAllAsync(); fixture.Options.FallbackBatchSize = 2; }
    public Task DisposeAsync() => Task.CompletedTask;

}
