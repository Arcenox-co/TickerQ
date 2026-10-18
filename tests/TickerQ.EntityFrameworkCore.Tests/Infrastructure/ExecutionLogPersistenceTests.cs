using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class ExecutionLogPersistenceTests : ExecutionLogPersistenceContract, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _services = null!;
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LogContext>().UseSqlite(_connection).Options;
        _services = new ServiceCollection().AddSingleton<IDbContextFactory<LogContext>>(new PooledDbContextFactory<LogContext>(options)).BuildServiceProvider();
        await using var db = new LogContext(options);
        await db.Database.EnsureCreatedAsync();
    }
    protected override ITickerExecutionLogPersistence NewStore() => new EfExecutionLogPersistence<LogContext>(_services);
    public async Task DisposeAsync() { await _services.DisposeAsync(); await _connection.DisposeAsync(); }
    public sealed class LogContext(DbContextOptions<LogContext> options) : DbContext(options)
    { protected override void OnModelCreating(ModelBuilder builder) => builder.ApplyConfiguration(new ExecutionLogArchiveConfiguration()); }
}
