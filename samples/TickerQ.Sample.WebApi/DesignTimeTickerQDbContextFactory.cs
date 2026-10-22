using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TickerQ.EntityFrameworkCore.DbContextFactory;

namespace TickerQ.Sample.WebApi;

public sealed class DesignTimeTickerQDbContextFactory : IDesignTimeDbContextFactory<TickerQDbContext>
{
    public TickerQDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TickerQDbContext>()
            .UseSqlite("Data Source=tickerq-webapi.db",
                builder => builder.MigrationsAssembly("TickerQ.Sample.WebApi"))
            .Options;
        return new TickerQDbContext(options);
    }
}
