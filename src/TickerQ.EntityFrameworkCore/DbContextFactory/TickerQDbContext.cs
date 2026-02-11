using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using System;

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
        var schema = this.GetService<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>().Schema;

        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>(schema)); 
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>(schema)); 
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>(schema)); 
        base.OnModelCreating(modelBuilder);
    }
}

public class TickerQDbContext : TickerQDbContext<TimeTickerEntity, CronTickerEntity>
{
    public TickerQDbContext(DbContextOptions<TickerQDbContext> options) : base(options)
    {
    }
}

internal interface ITickerDbContextFactory<TContext>
    where TContext : DbContext
{
    ITickerDbContextSession<TContext> CreateSession();
}

internal class TickerDbContextFactory<TContext> : ITickerDbContextFactory<TContext>
    where TContext : DbContext
{
    private readonly IServiceProvider _sp;

    public TickerDbContextFactory(IServiceProvider sp)
    {
        _sp = sp;
    }

    public ITickerDbContextSession<TContext> CreateSession()
        => new TickerDbContextSession<TContext>(_sp);
}

internal interface ITickerDbContextSession<TContext> : IDisposable
    where TContext : DbContext
{
    TContext Context { get; }
}

internal class TickerDbContextSession<TContext> : ITickerDbContextSession<TContext>
    where TContext : DbContext
{
    private readonly IServiceScope _scope;
    private readonly TContext _context;

    public TickerDbContextSession(IServiceProvider sp)
    {
        // 1. Try to get the Factory (Highest Priority)
        var factory = sp.GetService<IDbContextFactory<TContext>>();

        if (factory != null)
        {
            _context = factory.CreateDbContext();
            _scope = null;
        }
        else
        {
            // 2. Fallback: Create a manual scope (Standard AddDbContext)
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            _scope = scopeFactory.CreateScope();
            _context = _scope.ServiceProvider.GetRequiredService<TContext>();
        }
    }

    public TContext Context => _context;

    public void Dispose()
    {
        if (_scope != null)
        {
            _scope.Dispose();
        }
        else
        {
            _context.Dispose();
        }
    }
}