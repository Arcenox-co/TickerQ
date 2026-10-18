using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class ExecutionLogRegistrationTests
{
    [Fact]
    public void Application_tenant_cache_keys_survive_TickerQ_registration_and_feature_changes()
    {
        foreach (var enabled in new[] { false, true, false })
        {
            var services = new ServiceCollection().AddLogging();
            services.AddDbContext<TenantContext>(db => db.UseSqlite("Data Source=:memory:")
                .ReplaceService<IModelCacheKeyFactory, TenantCacheKeyFactory>());
            services.AddTickerQ(options =>
            {
                options.UseDefinedCronApplicationNamespace("tenant-logs-test"); options.UseReconciliationEpoch(1);
                options.AddOperationalStore(ef => ef.UseApplicationDbContext<TenantContext>(ConfigurationType.UseModelCustomizer));
                options.ConfigureExecutionLogs(logs => logs.PersistToDatabase = enabled);
            });
            using var provider = services.BuildServiceProvider();
            foreach (var tenant in new[] { "alpha", "beta", "alpha" })
            {
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TenantContext>();
                db.Tenant = tenant;
                Assert.IsType<TenantCacheKeyFactory>(db.GetService<IModelCacheKeyFactory>());
                Assert.Equal("Records_" + tenant, db.Model.FindEntityType(typeof(TenantRecord))!.GetTableName());
                Assert.Equal(enabled, db.Model.FindEntityType(typeof(ExecutionLogArchive)) != null);
            }
        }
    }

    public sealed class TenantRecord { public int Id { get; set; } }
    public sealed class TenantContext(DbContextOptions<TenantContext> options) : DbContext(options)
    {
        public string Tenant { get; set; } = "unset";
        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<TenantRecord>().ToTable("Records_" + Tenant);
    }
    public sealed class TenantCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((TenantContext)context).Tenant, designTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Application_context_maps_logs_only_when_enabled(bool enabled)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<ApplicationContext>(db => db.UseSqlite("Data Source=:memory:"));
        services.AddTickerQ(options =>
        {
            options.UseDefinedCronApplicationNamespace("application-logs-test"); options.UseReconciliationEpoch(1);
            options.AddOperationalStore(ef => ef.UseApplicationDbContext<ApplicationContext>(ConfigurationType.UseModelCustomizer));
            options.ConfigureExecutionLogs(logs => logs.PersistToDatabase = enabled);
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.Equal(enabled, db.Model.FindEntityType(typeof(ExecutionLogArchive)) != null);
        Assert.Equal(enabled, provider.GetService<ITickerExecutionLogPersistence>() != null);
    }

    public sealed class ApplicationContext(DbContextOptions<ApplicationContext> options) : DbContext(options);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Storage_and_schema_are_opt_in_and_configuration_order_does_not_matter(bool enabled)
    {
        foreach (var persist in new[] { enabled, !enabled, enabled })
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddTickerQ(options =>
            {
                options.UseDefinedCronApplicationNamespace("logs-test"); options.UseReconciliationEpoch(1);
                options.AddOperationalStore(ef => ef.UseTickerQDbContext<TickerQDbContext>(db => db.UseSqlite("Data Source=:memory:")));
                options.ConfigureExecutionLogs(logs => logs.PersistToDatabase = persist);
            });
            using var provider = services.BuildServiceProvider();
            using var db = provider.GetRequiredService<IDbContextFactory<TickerQDbContext>>().CreateDbContext();
            Assert.Equal(persist, db.Model.FindEntityType(typeof(ExecutionLogArchive)) != null);
            Assert.Equal(persist, provider.GetService<ITickerExecutionLogPersistence>() != null);
        }
    }
}
