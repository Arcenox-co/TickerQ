
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.DependencyInjection
{
    public static class ServiceExtension
    {
        /// <summary>
        /// Add the operational store for Ticker, OperationalStore will consume the DbContextOptions from the original configuration.
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <param name="tickerConfiguration"></param>
        /// <param name="optionsBuilderAction"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static TickerOptionsBuilder AddOperationalStore<TContext>(this TickerOptionsBuilder tickerConfiguration, Action<EfCoreOptionBuilder> optionsBuilderAction = null) where TContext : DbContext
            => AddOperationalStore<TContext, TimeTickerEntity, CronTickerEntity>(tickerConfiguration, optionsBuilderAction);

        /// <summary>
        /// Add the operational store for Ticker, OperationalStore will consume the DbContextOptions from the original configuration.
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <typeparam name="TTimeTickerEntity"></typeparam>
        /// <typeparam name="TCronTickerEntity"></typeparam>
        /// <param name="tickerConfiguration"></param>
        /// <param name="optionsBuilderAction"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static TickerOptionsBuilder AddOperationalStore<TContext, TTimeTickerEntity, TCronTickerEntity>(this TickerOptionsBuilder tickerConfiguration, Action<EfCoreOptionBuilder> optionsBuilderAction = null)
            where TContext : DbContext
            where TTimeTickerEntity : TimeTickerEntity, new()
            where TCronTickerEntity : CronTickerEntity, new()
        {
            var efCoreOptionBuilder = new EfCoreOptionBuilder();

            optionsBuilderAction?.Invoke(efCoreOptionBuilder);

            tickerConfiguration.ExternalProviderConfigServiceAction = (services) =>
            {
                if (efCoreOptionBuilder.UsesModelCustomizer)
                {
                    var originalDescriptor = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>));

                    if (originalDescriptor == null)
                        throw new Exception($"Ticker: Cannot add OperationalStore with empty {typeof(TContext).Name} configurations");

                    var newDescriptor = new ServiceDescriptor(
                        typeof(DbContextOptions<TContext>),
                        provider => UpdateDbContextOptionsService<TContext, TTimeTickerEntity, TCronTickerEntity>(provider, originalDescriptor.ImplementationFactory),
                        originalDescriptor.Lifetime
                    );

                    services.Remove(originalDescriptor);
                    services.Add(newDescriptor);
                }
              
                services.AddScoped<ITickerPersistenceProvider<TimeTicker, CronTicker>, TickerEFCorePersistenceProvider<TContext, TimeTicker, CronTicker>>();
            };

            UseApplicationService(tickerConfiguration, efCoreOptionBuilder.CancelMissedTickersOnReset);
            
            return tickerConfiguration;
        }

        private static DbContextOptions<TContext> UpdateDbContextOptionsService<TContext, TTimeTickerEntity, TCronTickerEntity>(IServiceProvider serviceProvider, Func<IServiceProvider, object> oldFactory) where TContext : DbContext where TTimeTickerEntity : TimeTickerEntity where TCronTickerEntity : CronTickerEntity
        {
            var factory = (DbContextOptions<TContext>)oldFactory(serviceProvider);

            return new DbContextOptionsBuilder<TContext>(factory)
                        .ReplaceService<IModelCustomizer, TickerModelCustomizer<TTimeTickerEntity, TCronTickerEntity>>()
                        .Options;
        }

        private static void UseApplicationService(this TickerOptionsBuilder tickerConfiguration, bool cancelMissedTickersOnReset)
        {
            tickerConfiguration.ExternalProviderConfigApplicationAction = (serviceProvider) =>
            {
                using var scope = serviceProvider.CreateScope();

                var internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();
                
                var functionsToSeed = TickerFunctionProvider.TickerFunctions
                    .Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
                    .Select(x => (x.Key, x.Value.cronExpression)).ToArray();
                
                internalTickerManager.SyncWithDbMemoryCronTickers(functionsToSeed).GetAwaiter().GetResult();

                internalTickerManager.ReleaseOrCancelAllAcquiredResources(cancelMissedTickersOnReset).GetAwaiter().GetResult();
            };
        }
    }
}
