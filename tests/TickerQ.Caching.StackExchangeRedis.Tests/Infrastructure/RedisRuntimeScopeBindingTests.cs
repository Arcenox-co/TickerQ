using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

public sealed class RedisRuntimeScopeBindingTests
{
    [Fact]
    public void Constructor_binds_runtime_admission_to_the_configured_application_scope()
    {
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 5 };
        options.BindRuntimeActivationScope("redis-scope-a", 5, schedulerEnabled: true);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            Substitute.For<IDatabase>(), Substitute.For<ITickerClock>(), options,
            new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);

        Assert.Equal(
            new RedisKeyBuilder(options.RuntimePartition!).ReconciliationActivationMetadataForScope(
                new ReconciliationActivationScope("redis-scope-a").ScopeKey),
            provider.RuntimeActivationMetadataKeyForTest);
    }
}
