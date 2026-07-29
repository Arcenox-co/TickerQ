using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using System;
using System.Threading;
using System.Threading.Tasks;

using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.DbContextFactory;

public class TickerQDbContext<TTimeTicker, TCronTicker> : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public TickerQDbContext(DbContextOptions<TickerQDbContext<TTimeTicker, TCronTicker>> options) : base(options)
    { }

    protected TickerQDbContext(DbContextOptions options) : base(options)
    { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        string schema;
        AssistantHistoryOptions assistantHistory;
        try
        {
            var optionBuilder = this.GetService<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>();
            schema = optionBuilder?.Schema ?? Constants.DefaultSchema;
            assistantHistory = optionBuilder?.AssistantHistory ?? AssistantHistoryOptions.Current;
        }
        catch (InvalidOperationException)
        {
            schema = Constants.DefaultSchema;
            // Design-time (dotnet ef migrations) fallback — set by AddAssistantHistory().
            assistantHistory = AssistantHistoryOptions.Current;
        }

        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>(schema));
        modelBuilder.ApplyConfiguration(new TimeTickerResultConfigurations<TTimeTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceResultConfigurations<TCronTicker>(schema));
        modelBuilder.ApplyConfiguration(new NodeFinalizationOutboxConfigurations(schema));

        // Assistant chat history tables — mapped only when opted in via
        // AddAssistantHistory(), so headless setups get no schema changes.
        if (assistantHistory != null)
        {
            modelBuilder.ApplyConfiguration(new AssistantConversationConfigurations(schema));
            modelBuilder.ApplyConfiguration(new AssistantMessageConfigurations(schema));
        }

        base.OnModelCreating(modelBuilder);
    }
}

public class TickerQDbContext : TickerQDbContext<TimeTickerEntity, CronTickerEntity>
{
    public TickerQDbContext(DbContextOptions<TickerQDbContext> options) : base(options)
    {
    }
}

internal readonly struct DbContextLease<TContext> : IDisposable
    where TContext : DbContext
{
    private readonly IServiceScope _scope;

    public TContext Context { get; }

    private DbContextLease(IServiceScope scope, TContext context)
    {
        _scope = scope;
        Context = context;
    }

    internal static async Task<DbContextLease<TContext>> CreateAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var factory = sp.GetService<IDbContextFactory<TContext>>();

        if (factory != null)
            return new DbContextLease<TContext>(null, await factory.CreateDbContextAsync(cancellationToken));

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var scope = scopeFactory.CreateScope();
        return new DbContextLease<TContext>(scope, scope.ServiceProvider.GetRequiredService<TContext>());
    }

    internal static DbContextLease<TContext> Create(IServiceProvider sp)
    {
        var factory = sp.GetService<IDbContextFactory<TContext>>();

        if (factory != null)
            return new DbContextLease<TContext>(null, factory.CreateDbContext());

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var scope = scopeFactory.CreateScope();
        return new DbContextLease<TContext>(scope, scope.ServiceProvider.GetRequiredService<TContext>());
    }

    public void Dispose()
    {
        if (_scope != null)
            _scope.Dispose();
        else
            Context.Dispose();
    }
}
