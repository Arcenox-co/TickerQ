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

        // Fail fast on an EnablePeriodic<T>() mismatch. The core opt-in (tickerOptions.EnablePeriodic<T>())
        // registers the in-memory IPeriodicTickerPersistenceProvider<TCore>; here we replace it with the EF
        // provider for the EF opt-in type. If the core opt-in is missing, or its T differs from the EF T,
        // RemoveAll(providerInterface) targets the wrong closed generic and the in-memory provider survives
        // silently — periodic data would never persist. Surface it as a startup error instead.
        var existingPeriodicRegistrations = services
            .Where(d => d.ServiceType.IsGenericType
                        && d.ServiceType.GetGenericTypeDefinition() == typeof(IPeriodicTickerPersistenceProvider<>))
            .ToArray();

        if (existingPeriodicRegistrations.Length == 0)
            throw new InvalidOperationException(
                $"EntityFrameworkCore EnablePeriodic<{periodicType.Name}>() was configured but the core " +
                "tickerOptions.EnablePeriodic<T>() opt-in is missing. Add tickerOptions.EnablePeriodic<" +
                $"{periodicType.Name}>() so periodic tickers are registered before the EF provider replaces them.");

        var mismatched = existingPeriodicRegistrations
            .Where(d => d.ServiceType != providerInterface)
            .ToArray();
        if (mismatched.Length > 0)
        {
            var coreType = mismatched[0].ServiceType.GetGenericArguments()[0];
            throw new InvalidOperationException(
                $"Periodic ticker type mismatch: tickerOptions.EnablePeriodic<{coreType.Name}>() does not match " +
                $"EntityFrameworkCore EnablePeriodic<{periodicType.Name}>(). Both opt-ins must use the same periodic " +
                "ticker type, otherwise periodic tickers would silently fall back to the in-memory provider and lose " +
                "their persisted state on restart.");
        }

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