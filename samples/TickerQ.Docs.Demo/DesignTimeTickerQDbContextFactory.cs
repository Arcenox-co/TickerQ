using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TickerQ.EntityFrameworkCore.DbContextFactory;

namespace TickerQ.Docs.Demo;

public sealed class DesignTimeTickerQDbContextFactory : IDesignTimeDbContextFactory<TickerQDbContext>
{
    public TickerQDbContext CreateDbContext(string[] args)
    {
        var databasePath = Environment.GetEnvironmentVariable("DOCS_DEMO_DB")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "tickerq-docs-demo.db");
        var options = new DbContextOptionsBuilder<TickerQDbContext>()
            .UseSqlite($"Data Source={databasePath}",
                sqlite => sqlite.MigrationsAssembly(typeof(DesignTimeTickerQDbContextFactory).Assembly.FullName))
            .Options;

        return new TickerQDbContext(options);
    }
}
