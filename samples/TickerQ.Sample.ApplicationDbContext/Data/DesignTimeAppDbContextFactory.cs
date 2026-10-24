using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.DependencyInjection;

namespace TickerQ.Sample.ApplicationDbContext.Data;

public sealed class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(
            "Data Source=tickerq-webapi.db",
            builder => builder.MigrationsAssembly("TickerQ.Sample.ApplicationDbContext")));
        services.AddTickerQ(options =>
        {
            options.UseApplicationNamespace("application-db-context-sample");
            options.AddOperationalStore(store =>
                store.UseApplicationDbContext<AppDbContext>(ConfigurationType.UseModelCustomizer));
        });

        return services.BuildServiceProvider().GetRequiredService<AppDbContext>();
    }
}
