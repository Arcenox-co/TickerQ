
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
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
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static TickerOptionsBuilder AddOperationalStore<TContext>(this TickerOptionsBuilder tickerConfiguration) where TContext : DbContext
            => AddOperationalStore<TContext, TimeTickerEntity, CronTickerEntity>(tickerConfiguration);

        /// <summary>
        /// Add the operational store for Ticker, OperationalStore will consume the DbContextOptions from the original configuration.
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <typeparam name="TTimeTickerEntity"></typeparam>
        /// <typeparam name="TCronTickerEntity"></typeparam>
        /// <param name="tickerConfiguration"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static TickerOptionsBuilder AddOperationalStore<TContext, TTimeTickerEntity, TCronTickerEntity>(this TickerOptionsBuilder tickerConfiguration)
            where TContext : DbContext
            where TTimeTickerEntity : TimeTickerEntity, new()
            where TCronTickerEntity : CronTickerEntity, new()
        {
            tickerConfiguration.EfCoreConfigServiceAction = (services) =>
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

                services.AddScoped<ITickerPersistenceProvider<TimeTicker, CronTicker>, TickerEFCorePersistenceProvider<TContext, TimeTicker, CronTicker>>();
            };

            return tickerConfiguration;
        }

        /// <summary>
        /// Timeout checker default is 1 minute, cannot set less than 30 seconds
        /// </summary>
        /// <param name="tickerConfiguration"></param>
        /// <param name="timeSpan"></param>
        public static TickerOptionsBuilder SetTimeOutJobChecker(this TickerOptionsBuilder tickerConfiguration, TimeSpan timeSpan)
        {
            tickerConfiguration.TimeOutChecker = timeSpan < TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : timeSpan;

            return tickerConfiguration;
        }


        public static void CancelMissedTickersOnApplicationRestart(this TickerOptionsBuilder tickerConfiguration)
        {
            tickerConfiguration.CancelMissedTickersOnReset = true;
        }


        private static DbContextOptions<TContext> UpdateDbContextOptionsService<TContext, TTimeTickerEntity, TCronTickerEntity>(IServiceProvider serviceProvider, Func<IServiceProvider, object> oldFactory) where TContext : DbContext where TTimeTickerEntity : TimeTickerEntity where TCronTickerEntity : CronTickerEntity
        {
            var factory = (DbContextOptions<TContext>)oldFactory(serviceProvider);

            return new DbContextOptionsBuilder<TContext>(factory)
                        .ReplaceService<IModelCustomizer, TickerModelCustomizer<TTimeTickerEntity, TCronTickerEntity>>()
                        .Options;
        }
    }
}
