using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace TickerQ.EntityFrameworkCore.DbContextFactory;

public interface ITickerQDbContextFactory<TContext> where TContext : DbContext
{
    TContext CreateDbContext();
    Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}