using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TickerQ.EntityFrameworkCore.DbContextFactory;

internal class TickerQDbContextFactory<TContext> : ITickerQDbContextFactory<TContext> 
    where TContext : DbContext
{
    private readonly PooledDbContextFactory<TContext> _innerFactory;

    public TickerQDbContextFactory(DbContextOptions<TContext> options, int poolSize)
    {
        _innerFactory = new PooledDbContextFactory<TContext>(options, poolSize);
    }

    public TContext CreateDbContext()
    {
        return _innerFactory.CreateDbContext();
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return _innerFactory.CreateDbContextAsync(cancellationToken);
    }
}