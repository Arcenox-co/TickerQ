using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

/// <summary>Promise state rows in Redis, whose keys carry the partition's hash tag.</summary>
[Collection("RedisRealScript")]
public sealed class RedisPromiseStateStoreTests(RedisRealScriptFixture fixture) : PromiseStateStoreContract, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.Db.ExecuteAsync("FLUSHALL");
    public Task DisposeAsync() => Task.CompletedTask;

    protected override Task<ITickerPromiseStateStore> NewStoreAsync(string applicationNamespace)
    {
        var options = new SchedulerOptionsBuilder { NodeIdentifier = applicationNamespace };
        options.BindRuntimeActivationScope(applicationNamespace, 1, false);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return Task.FromResult<ITickerPromiseStateStore>(new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            fixture.Db, clock, options,
            new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance));
    }
}
