using TickerQ.MongoDB.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Tests;

/// <summary>
/// Promise state rows in MongoDB, where a definition's <c>_id</c> is its id alone, so isolating namespaces depends
/// on the promise row id including the partition.
/// </summary>
[Collection("Mongo")]
public sealed class MongoPromiseStateStoreTests(MongoTestFixture fixture) : PromiseStateStoreContract
{
    // Each test gets its own collections, shared by both namespaces as in production.
    private readonly TickerMongoContext<TimeTickerEntity, CronTickerEntity> _context =
        new(fixture.Database, $"promise_{Guid.NewGuid():N}_"[..20]);

    protected override Task<ITickerPromiseStateStore> NewStoreAsync(string applicationNamespace)
    {
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope(applicationNamespace, 1, false);
        return Task.FromResult<ITickerPromiseStateStore>(
            new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(_context, fixture.Clock, options));
    }
}
