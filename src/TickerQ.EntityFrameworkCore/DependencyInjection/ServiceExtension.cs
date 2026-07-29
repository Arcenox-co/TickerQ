using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.DependencyInjection;

public static class ServiceExtension
{
        
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOperationalStore<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker,TCronTicker> tickerConfiguration, Action<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>> efConfiguration = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var efCoreOptionBuilder = new TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>();

        efConfiguration?.Invoke(efCoreOptionBuilder);
            
        if (efCoreOptionBuilder.PoolSize <= 0) 
            throw new ArgumentOutOfRangeException(nameof(efCoreOptionBuilder.PoolSize), "Pool size must be greater than 0");
            
        tickerConfiguration.ExternalProviderConfigServiceAction += (services) 
            => services.AddSingleton(_ => efCoreOptionBuilder);
            
        tickerConfiguration.ExternalProviderConfigServiceAction += efCoreOptionBuilder.ConfigureServices;
            
        UseApplicationService(tickerConfiguration, efCoreOptionBuilder);
            
        return tickerConfiguration;
    }
        
    private static void UseApplicationService<TTimeTicker, TCronTicker>(TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> options)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.UseExternalProviderApplication((serviceProvider) =>
        {
            var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
            var hostLifetime = serviceProvider.GetService<IHostApplicationLifetime>();
            var schedulerOptions = serviceProvider.GetService<SchedulerOptionsBuilder>();
            var hostScheduler = serviceProvider.GetService<ITickerQHostScheduler>();

            hostLifetime.ApplicationStarted.Register(() =>
            {
                Task.Run(async () =>
                {
                    // Ownership is process-instance unique. Do not release current-label rows on
                    // startup: a sibling process with the same logical NodeIdentifier may still be
                    // live. Expired predecessors are handled by lease/stale recovery.

                    // After startup, restart the host scheduler so it immediately
                    // picks up newly seeded cron tickers and jobs configured via the core pipeline.
                    if (hostScheduler != null && hostScheduler.IsRunning)
                    {
                        hostScheduler.Restart();
                    }
                });
            });
        });
    }
}
