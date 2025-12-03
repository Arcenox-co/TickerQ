using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.DependencyInjection;

public static class ServiceExtension
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddStackExchangeRedis<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, Action<TickerQRedisOptionBuilder> setupAction)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            var options = new TickerQRedisOptionBuilder
            {
                InstanceName = "tickerq:"
            };
            
            setupAction?.Invoke(options);
            if (string.IsNullOrWhiteSpace(options.Configuration) && options.ConfigurationOptions == null)
                throw new InvalidOperationException("Redis configuration must be provided when enabling StackExchange.Redis persistence.");

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                options.ConfigurationOptions != null
                    ? ConnectionMultiplexer.Connect(options.ConfigurationOptions)
                    : ConnectionMultiplexer.Connect(options.Configuration));
            services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
            services.AddHostedService<NodeHeartBeatBackgroundService>();
            services.AddSingleton<ITickerQRedisContext, TickerQRedisContext>();
            services.AddKeyedSingleton<IDistributedCache>("tickerq", (sp, key) => new RedisCache(options));
            services.AddSingleton(_ => options);
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>>();
        };

        return tickerConfiguration;
    }

    public class TickerQRedisOptionBuilder : RedisCacheOptions
    {
        public TimeSpan NodeHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
