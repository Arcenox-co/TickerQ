using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.DependencyInjection;

public static class ServiceExtension
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddStackExchangeRedis<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, Action<RedisCacheOptions> setupAction)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            services.AddSingleton<ITickerQRedisContext, TickerQRedisContext>();
            services.AddKeyedSingleton<IDistributedCache>("tickerq", (sp, key) =>
            {
                var options = new RedisCacheOptions
                {
                    InstanceName = "tickerq:"
                };

                setupAction?.Invoke(options);

                return new RedisCache(options);
            });
        };

        return tickerConfiguration;
    }
}