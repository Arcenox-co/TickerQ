
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using TickerQ.EntityFrameworkCore.Configurations.Base;
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
        {
            tickerConfiguration.EfCoreConfigAction = (IServiceCollection services) =>
            {
                var originalDescriptor = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>));

                if (originalDescriptor == default)
                    throw new Exception($"Ticker: Cannot add OperationalStore with empty {typeof(TContext).Name} configurations");

                var newDescriptor = new ServiceDescriptor(
                        typeof(DbContextOptions<TContext>),
                        provider => UpdateDbContextOptionsService<TContext>(provider, originalDescriptor.ImplementationFactory),
                        originalDescriptor.Lifetime
                    );

                services.Remove(originalDescriptor);
                services.Add(newDescriptor);
                services.AddScoped<ITickerManager, TickerManager<TContext>>();
                services.AddScoped<IInternalTickerManager, TickerManager<TContext>>();
            };

            return tickerConfiguration;
        }

        public static TickerOptionsBuilder SetInstanceIdentifier(this TickerOptionsBuilder tickerConfiguration, string identifierName)
        {
            tickerConfiguration.InstanceIdentifier = identifierName;

            return tickerConfiguration;
        }

        /// <summary>
        /// Timeout checker default is 1 minute, cannot set less than 30 seconds
        /// </summary>
        /// <param name="timeSpan"></param>
        public static TickerOptionsBuilder SetTimeOutJobChecker(this TickerOptionsBuilder tickerConfiguration, TimeSpan timeSpan)
        {
            if (timeSpan < TimeSpan.FromSeconds(30))
                tickerConfiguration.TimeOutChecker = TimeSpan.FromSeconds(30);
            else
                tickerConfiguration.TimeOutChecker = timeSpan;

            return tickerConfiguration;
        }

        private static DbContextOptions<TContext> UpdateDbContextOptionsService<TContext>(IServiceProvider serviceProvider, Func<IServiceProvider, object> oldFactory) where TContext : DbContext
        {
            var factory = (DbContextOptions<TContext>)oldFactory(serviceProvider);

            return new DbContextOptionsBuilder<TContext>(factory)
                        .ReplaceService<IModelCustomizer, TickerModelCostumizer>()
                        .Options;
        }
    }
}
