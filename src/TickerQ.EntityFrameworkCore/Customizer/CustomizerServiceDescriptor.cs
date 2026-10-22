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
using TickerQ.Utilities.Interfaces.Managers;

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
            RegisterTransactionalScheduling<TContext, TTimeTicker, TCronTicker>(services);
            RegisterAssistantHistory<TContext, TTimeTicker, TCronTicker>(builder, services);
            if (builder.ExecutionLogs?.PersistToDatabase == true)
                services.AddSingleton<ITickerExecutionLogPersistence, EfExecutionLogPersistence<TContext>>();
            // Bootstrapper enumeration preserves registration order: optional schema migration and
            // mandatory data upgrade/validation both complete before readiness or scheduler access.
            RegisterStoreUpgrade<TContext, TTimeTicker, TCronTicker>(builder, services);
            RegisterNodeFinalizationReadiness<TContext>(services);
        };
    }

    private static void RegisterTransactionalScheduling<TContext, TTimeTicker, TCronTicker>(IServiceCollection services)
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var descriptor = services.Last(x => x.ServiceType == typeof(ITimeTickerManager<TTimeTicker>));
        services.Remove(descriptor);
        services.AddSingleton(sp => new EfOriginalTimeTickerManager<TTimeTicker>(
            (ITimeTickerManager<TTimeTicker>)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!))));
        var cronDescriptor = services.Last(x => x.ServiceType == typeof(ICronTickerManager<TCronTicker>));
        services.Remove(cronDescriptor);
        services.AddSingleton(sp => new EfOriginalCronTickerManager<TCronTicker>(
            (ICronTickerManager<TCronTicker>)(cronDescriptor.ImplementationInstance
                ?? cronDescriptor.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, cronDescriptor.ImplementationType!))));
        services.TryAddSingleton(sp => new EfStagingRootProvider(sp));
        services.AddLogging();
        services.AddSingleton<EfTransactionalScheduling<TContext, TTimeTicker, TCronTicker>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbContextOptionsConfiguration<TContext>,
            StagingOptionsConfiguration<TContext, TTimeTicker, TCronTicker>>());
        services.AddTransient<ITimeTickerManager<TTimeTicker>>(sp =>
            new EfStagingTimeTickerManager<TTimeTicker>(
                sp.GetRequiredService<EfOriginalTimeTickerManager<TTimeTicker>>().Manager,
                (entity, ct) => sp.GetRequiredService<EfTransactionalScheduling<TContext, TTimeTicker, TCronTicker>>()
                    .StageAsync(GetScopedContext(sp), entity, ct)));
        services.AddTransient<ICronTickerManager<TCronTicker>>(sp =>
            new EfStagingCronTickerManager<TCronTicker>(
                sp.GetRequiredService<EfOriginalCronTickerManager<TCronTicker>>().Manager,
                (entity, ct) => sp.GetRequiredService<EfTransactionalScheduling<TContext, TTimeTicker, TCronTicker>>()
                    .StageCronAsync(GetScopedContext(sp), entity, ct)));

        TContext GetScopedContext(IServiceProvider sp)
        {
            if (ReferenceEquals(sp, sp.GetRequiredService<EfStagingRootProvider>().Provider))
                throw new InvalidOperationException(
                    "StageAsync requires a manager resolved inside a DI scope with the application DbContext. " +
                    "Resolve the ticker manager from a request scope or IServiceScopeFactory.CreateScope().");
            if (services.LastOrDefault(x => x.ServiceType == typeof(TContext))?.Lifetime != ServiceLifetime.Scoped)
                throw new InvalidOperationException(
                    "StageAsync requires the application DbContext to be registered with scoped lifetime.");
            return sp.GetRequiredService<TContext>();
        }
    }

    public sealed class StagingOptionsConfiguration<TContext, TTimeTicker, TCronTicker>
        : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.AddInterceptors(serviceProvider
                .GetRequiredService<EfTransactionalScheduling<TContext, TTimeTicker, TCronTicker>>());
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
                TickerModelOptionsExtension.Configure(optionsBuilder, builder);
                optionsBuilder.UseApplicationServiceProvider(sp);
                return new PooledDbContextFactory<TContext>(optionsBuilder.Options, builder.PoolSize);
            });
            services.TryAddScoped<TContext>(sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerEfCorePersistenceProvider<TContext, TTimeTicker, TCronTicker>>();
            RegisterAssistantHistory<TContext, TTimeTicker, TCronTicker>(builder, services);
            if (builder.ExecutionLogs?.PersistToDatabase == true)
                services.AddSingleton<ITickerExecutionLogPersistence, EfExecutionLogPersistence<TContext>>();
            // Bootstrapper enumeration preserves registration order: optional schema migration and
            // mandatory data upgrade/validation both complete before readiness or scheduler access.
            RegisterStoreUpgrade<TContext, TTimeTicker, TCronTicker>(builder, services);
            RegisterNodeFinalizationReadiness<TContext>(services);
        };
    }

    private static void RegisterStoreUpgrade<TContext, TTimeTicker, TCronTicker>(
        TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> builder,
        IServiceCollection services)
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (builder.AutoMigrate)
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ITickerQPersistencePrerequisiteBootstrapper,
                EfCoreAutoMigrateBootstrapper<TContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITickerQPersistenceBootstrapper,
            EfCoreStoreUpgradeBootstrapper<TContext, TTimeTicker, TCronTicker>>());
    }

    private static void RegisterNodeFinalizationReadiness<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddSingleton<EfCoreNodeFinalizationOutboxReadiness>();
        services.TryAddSingleton<IEfCoreNodeFinalizationOutboxReadiness>(sp =>
            sp.GetRequiredService<EfCoreNodeFinalizationOutboxReadiness>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITickerQPersistenceReadinessProbe,
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
        services.AddScoped<EfAssistantHistoryStore<TContext>>();
        services.AddScoped<IAssistantHistoryStore>(provider =>
            provider.GetRequiredService<EfAssistantHistoryStore<TContext>>());
        services.AddScoped<IAssistantProposalHistoryStore>(provider =>
            provider.GetRequiredService<EfAssistantHistoryStore<TContext>>());
        services.AddScoped<EfAssistantInvestigationStore<TContext>>();
        services.AddScoped<IAssistantInvestigationStore>(provider =>
            provider.GetRequiredService<EfAssistantInvestigationStore<TContext>>());
        services.AddScoped<IAssistantInvestigationCoordinator>(provider =>
            provider.GetRequiredService<EfAssistantInvestigationStore<TContext>>());
    }

    public class TickerQOptionsConfiguration<TContext, TTimeTicker, TCronTicker>
        : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
        {
            TickerModelOptionsExtension.Configure(optionsBuilder,
                serviceProvider.GetRequiredService<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>());
            optionsBuilder
                .UseApplicationServiceProvider(serviceProvider)
                .ReplaceService<IModelCustomizer, TickerModelCustomizer<TTimeTicker, TCronTicker>>();
        }
    }
}
