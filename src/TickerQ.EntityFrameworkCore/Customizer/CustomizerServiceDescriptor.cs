using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Customizer;

public static class CustomizerServiceDescriptor
{
    internal static void UseDbContext<TContext, TTimeTicker, TCronTicker>(EfCoreOptionBuilder<TTimeTicker, TCronTicker> builder, ConfigurationType configurationType) 
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity, new()
        where TCronTicker : CronTickerEntity, new()
    {
        builder.ConfigureServices += (services) =>
        {
            if (configurationType == ConfigurationType.UseModelCustomizer)
            {
                var originalDescriptor = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>));

                if (originalDescriptor == null)
                    throw new Exception($"Ticker: Cannot use UseModelCustomizer with empty {typeof(TContext).Name} configurations");

                var newDescriptor = new ServiceDescriptor(
                    typeof(DbContextOptions<TContext>),
                    provider => UpdateDbContextOptionsService<TContext, TTimeTicker, TCronTicker>(provider, originalDescriptor.ImplementationFactory),
                    originalDescriptor.Lifetime
                );

                services.Remove(originalDescriptor);
                services.Add(newDescriptor);
            }
              
            services.TryAddSingleton<ITickerQDbContextFactory<TContext>>(provider =>
            {
                var serviceDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<TContext>));

                if (serviceDescriptor?.ImplementationFactory == null)
                    throw new InvalidOperationException($"Cannot resolve DbContextOptions<{typeof(TContext).Name}>");
                
                var options = (DbContextOptions<TContext>)serviceDescriptor.ImplementationFactory(provider);
                return new TickerQDbContextFactory<TContext>(options, builder.PoolSize);
            });

            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerEfCorePersistenceProvider<TContext, TTimeTicker, TCronTicker>>();
        };
    }
    
    private static DbContextOptions<TContext> UpdateDbContextOptionsService<TContext, TTimeTicker, TCronTicker>(IServiceProvider serviceProvider, Func<IServiceProvider, object> oldFactory) 
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity, new()
        where TCronTicker : CronTickerEntity, new()
    
    {
        var factory = (DbContextOptions<TContext>)oldFactory(serviceProvider);

        return new DbContextOptionsBuilder<TContext>(factory)
            .ReplaceService<IModelCustomizer, TickerModelCustomizer<TTimeTicker, TCronTicker>>()
            .Options;
    }
}