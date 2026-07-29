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
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbContextOptionsConfiguration<TContext>, TickerQOptionsConfiguration<TContext, TTimeTicker, TCronTicker>>());
            }

            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerEfCorePersistenceProvider<TContext, TTimeTicker, TCronTicker>>();
            RegisterAssistantHistory<TContext, TTimeTicker, TCronTicker>(builder, services);
            // Bootstrapper enumeration preserves registration order: migrate before probing.
            RegisterAutoMigrate<TContext, TTimeTicker, TCronTicker>(builder, services);
            RegisterNodeFinalizationReadiness<TContext>(services);
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
            RegisterAssistantHistory<TContext, TTimeTicker, TCronTicker>(builder, services);
            // Bootstrapper enumeration preserves registration order: migrate before probing.
            RegisterAutoMigrate<TContext, TTimeTicker, TCronTicker>(builder, services);
            RegisterNodeFinalizationReadiness<TContext>(services);
        };
    }

    // Auto-migration is opt-in via builder.AutoMigrateDatabase(); registered inside
    // ConfigureServices so it applies regardless of builder method call order.
    private static void RegisterAutoMigrate<TContext, TTimeTicker, TCronTicker>(
        TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> builder, IServiceCollection services)
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (!builder.AutoMigrate) return;

        services.AddSingleton<ITickerQPersistenceBootstrapper, EfCoreAutoMigrateBootstrapper<TContext>>();
    }

    private static void RegisterNodeFinalizationReadiness<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddSingleton<EfCoreNodeFinalizationOutboxReadiness>();
        services.TryAddSingleton<IEfCoreNodeFinalizationOutboxReadiness>(sp =>
            sp.GetRequiredService<EfCoreNodeFinalizationOutboxReadiness>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITickerQPersistenceBootstrapper,
            EfCoreNodeFinalizationOutboxReadinessProbe<TContext>>());
    }

    // Assistant chat history is strictly opt-in: nothing is registered (and
    // no tables are mapped) unless AddAssistantHistory() was called on the
    // builder. Runs inside ConfigureServices so it works regardless of the
    // order builder methods were called in.
    private static void RegisterAssistantHistory<TContext, TTimeTicker, TCronTicker>(
        TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> builder, IServiceCollection services)
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (builder.AssistantHistory == null) return;

        services.AddSingleton(builder.AssistantHistory);
        services.AddScoped<IAssistantHistoryStore, EfAssistantHistoryStore<TContext>>();
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
}