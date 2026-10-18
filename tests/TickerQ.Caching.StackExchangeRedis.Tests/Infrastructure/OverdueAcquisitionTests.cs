using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using System.Text.Json.Nodes;
using TickerQ.Utilities.Enums;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

[Collection("RedisRealScript")]
public sealed class OverdueAcquisitionTests : OverdueAcquisitionContract<TimeTickerEntity, CronTickerEntity>
{
    private readonly RedisRealScriptFixture _fixture;
    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider { get; }
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public OverdueAcquisitionTests(RedisRealScriptFixture fixture)
    {
        _fixture = fixture;
        fixture.Db.Execute("FLUSHALL");
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(Now);
        Provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(fixture.Db, clock,
            new SchedulerOptionsBuilder { FallbackBatchSize = 2 }, new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

}
