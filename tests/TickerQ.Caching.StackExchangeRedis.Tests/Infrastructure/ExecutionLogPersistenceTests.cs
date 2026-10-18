using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

[Collection("RedisRealScript")]
public sealed class ExecutionLogPersistenceTests(RedisRealScriptFixture fixture) : ExecutionLogPersistenceContract
{
    private readonly string _prefix = "log-tests:" + Guid.NewGuid().ToString("N") + ":";
    protected override ITickerExecutionLogPersistence NewStore() => new RedisExecutionLogPersistence(fixture.Db, _prefix);
}
