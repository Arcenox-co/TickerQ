
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure.Dashboard;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;

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
            => AddOperationalStore<TContext, TimeTicker, CronTicker>(tickerConfiguration);

        /// <summary>
        /// Add the operational store for Ticker, OperationalStore will consume the DbContextOptions from the original configuration.
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <typeparam name="TTimeTicker"></typeparam>
        /// <typeparam name="TCronTicker"></typeparam>
        /// <param name="tickerConfiguration"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static TickerOptionsBuilder AddOperationalStore<TContext, TTimeTicker, TCronTicker>(this TickerOptionsBuilder tickerConfiguration) where TContext : DbContext where TTimeTicker : TimeTicker, new() where TCronTicker : CronTicker, new()
        {
            tickerConfiguration.EfCoreConfigServiceAction = (services) =>
            {
                var originalDescriptor = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>));

                if (originalDescriptor == null)
                    throw new Exception($"Ticker: Cannot add OperationalStore with empty {typeof(TContext).Name} configurations");

                var newDescriptor = new ServiceDescriptor(
                        typeof(DbContextOptions<TContext>),
                        provider => UpdateDbContextOptionsService<TContext, TTimeTicker, TCronTicker>(provider, originalDescriptor.ImplementationFactory),
                        originalDescriptor.Lifetime
                    );

                services.Remove(originalDescriptor);
                services.Add(newDescriptor);
                services.AddScoped<ICronTickerManager<TCronTicker>, TickerManager<TContext, TTimeTicker, TCronTicker>>();
                services.AddScoped<ITimeTickerManager<TTimeTicker>, TickerManager<TContext, TTimeTicker, TCronTicker>>();
                services.AddScoped<IInternalTickerManager, TickerManager<TContext, TTimeTicker, TCronTicker>>();
                services
                    .AddScoped<ITickerDashboardRepository,
                        TickerTickerDashboardRepository<TContext, TTimeTicker, TCronTicker>>();
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


        private static DbContextOptions<TContext> UpdateDbContextOptionsService<TContext, TTimeTicker, TCronTicker>(IServiceProvider serviceProvider, Func<IServiceProvider, object> oldFactory) where TContext : DbContext where TTimeTicker : TimeTicker where TCronTicker : CronTicker
        {
            var factory = (DbContextOptions<TContext>)oldFactory(serviceProvider);

            return new DbContextOptionsBuilder<TContext>(factory)
                        .ReplaceService<IModelCustomizer, TickerModelCustomizer<TTimeTicker, TCronTicker>>()
                        .Options;
        }
    }
}
