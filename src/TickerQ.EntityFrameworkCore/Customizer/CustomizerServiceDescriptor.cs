using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Customizer;

public static class ServiceBuilder
{
    internal static void UseApplicationDbContext<TContext, TTimeTicker, TCronTicker>(TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> builder, ConfigurationType configurationType) 
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        builder.ConfigureServices = (services) =>
        {
            if (configurationType == ConfigurationType.UseModelCustomizer)
            {
                if (builder.PeriodicEnabled)
                {
                    // Use the periodic-aware customizer so the consumer's DbContext also
                    // gets the periodic table mappings without any code changes on their side.
                    var customizerType = typeof(TickerQOptionsConfigurationWithPeriodic<,,,>)
                        .MakeGenericType(typeof(TContext), typeof(TTimeTicker), typeof(TCronTicker), builder.PeriodicTickerType);
                    services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IDbContextOptionsConfiguration<TContext>), customizerType));
                }
                else
                {
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbContextOptionsConfiguration<TContext>, TickerQOptionsConfiguration<TContext, TTimeTicker, TCronTicker>>());
                }
            }

            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerEfCorePersistenceProvider<TContext, TTimeTicker, TCronTicker>>();

            if (builder.PeriodicEnabled)
                RegisterPeriodicProvider<TContext>(services, builder.PeriodicTickerType);
        };
    }

    internal static void UseTickerQDbContext<TContext, TTimeTicker, TCronTicker>(TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> builder, Action<DbContextOptionsBuilder> optionsAction)
        where TContext : TickerQDbContext<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        builder.ConfigureServices = (services) =>
        {
            services.TryAddSingleton<IDbContextFactory<TContext>>(sp =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<TContext>();
                optionsAction.Invoke(optionsBuilder);
                optionsBuilder.UseApplicationServiceProvider(sp);
                return new PooledDbContextFactory<TContext>(optionsBuilder.Options, builder.PoolSize);
            });
            services.TryAddScoped<TContext>(sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerEfCorePersistenceProvider<TContext, TTimeTicker, TCronTicker>>();

            if (builder.PeriodicEnabled)
                RegisterPeriodicProvider<TContext>(services, builder.PeriodicTickerType);
        };
    }

    private static void RegisterPeriodicProvider<TContext>(IServiceCollection services, Type periodicType)
        where TContext : DbContext
    {
        // Reflection-typed registration: we need to construct the closed generic type
        // IPeriodicTickerPersistenceProvider<TPeriodic> -> TickerEfCorePeriodicPersistenceProvider<TContext, TPeriodic>.
        var providerInterface = typeof(IPeriodicTickerPersistenceProvider<>).MakeGenericType(periodicType);
        var providerImpl = typeof(TickerEfCorePeriodicPersistenceProvider<,>).MakeGenericType(typeof(TContext), periodicType);
        // Replace the in-memory fallback registered earlier in TickerQServiceExtensions.RegisterPeriodicServices.
        services.RemoveAll(providerInterface);
        services.AddSingleton(providerInterface, providerImpl);
    }

    public class TickerQOptionsConfiguration<TContext, TTimeTicker, TCronTicker>
        : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .ReplaceService<IModelCustomizer, TickerModelCustomizer<TTimeTicker, TCronTicker>>();
        }
    }

    public class TickerQOptionsConfigurationWithPeriodic<TContext, TTimeTicker, TCronTicker, TPeriodicTicker>
        : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .ReplaceService<IModelCustomizer, TickerModelCustomizerWithPeriodic<TTimeTicker, TCronTicker, TPeriodicTicker>>();
        }
    }
}