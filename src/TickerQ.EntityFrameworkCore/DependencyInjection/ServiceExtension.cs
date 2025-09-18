using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;

namespace TickerQ.EntityFrameworkCore.DependencyInjection
{
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
            tickerConfiguration.UseExternalProviderApplication(async (serviceProvider) =>
            {
                var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
                
                var functionsToSeed = TickerFunctionProvider.TickerFunctions
                    .Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
                    .Select(x => (x.Key, x.Value.cronExpression)).ToArray();
                
                if(options.SeedDefinedCronTickers)
                    await internalTickerManager.MigrateDefinedCronTickers(functionsToSeed);

                if(options.TimeSeeder != null)
                    await options.TimeSeeder((TickerManager<TTimeTicker, TCronTicker>)internalTickerManager);
                
                if(options.CronSeeder != null)
                    await options.CronSeeder((TickerManager<TTimeTicker, TCronTicker>)internalTickerManager);
            });
        }
    }
}
