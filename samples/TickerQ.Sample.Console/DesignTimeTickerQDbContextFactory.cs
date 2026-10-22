using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TickerQ.EntityFrameworkCore.DbContextFactory;

namespace TickerQ.Sample.Console;

public sealed class DesignTimeTickerQDbContextFactory : IDesignTimeDbContextFactory<TickerQDbContext>
{
    public TickerQDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TickerQDbContext>()
            .UseSqlite("Data Source=tickerq-console.db",
                builder => builder.MigrationsAssembly("TickerQ.Sample.Console"))
            .Options;
        return new TickerQDbContext(options);
    }
}
