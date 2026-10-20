using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;
using TickerQ.Dashboard;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Instrumentation.OpenTelemetry;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;

// Optional packages are exercised as installed managed assemblies. No listener, database,
// Redis connection, scheduler, monitor loop, or notification sender is started.
var builder = Host.CreateApplicationBuilder();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddTickerQ(options =>
{
    Configure(options);
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard().SetReadOnly());
    options.AddIntegrations();
    options.AddOpenTelemetryInstrumentation();
});
using (var host = builder.Build())
{
    var admin = host.Services.GetRequiredService<ITickerIntegrationAdminService>();
    var snapshot = await admin.GetAsync();
    Check(snapshot.Available && snapshot.Providers.Count > 0, "Installed integrations did not resolve their providers.");
    var instrumentation = builder.Services.Last(item => item.ServiceType.FullName == "TickerQ.Utilities.Instrumentation.ITickerQInstrumentation");
    Check(host.Services.GetRequiredService(instrumentation.ServiceType).GetType().Assembly.GetName().Name == "TickerQ.Instrumentation.OpenTelemetry",
        "Installed OpenTelemetry registration was not selected.");
    _ = host.Services.GetRequiredService<DashboardOptionsBuilder>();
    var resources = typeof(DashboardOptionsBuilder).Assembly.GetManifestResourceNames();
    Check(resources.Any(name => name.EndsWith(".index.html", StringComparison.Ordinal)), "Packed dashboard HTML is missing.");
    Check(resources.Any(name => name.Contains(".assets.", StringComparison.Ordinal) && name.EndsWith(".js", StringComparison.Ordinal)),
        "Packed dashboard JavaScript is missing.");
}

CheckProvider("TickerQ.EntityFrameworkCore", options =>
    TickerQ.EntityFrameworkCore.DependencyInjection.ServiceExtension.AddOperationalStore(options,
        ef => ef.UseTickerQDbContext<TickerQDbContext>(_ => { })));
CheckProvider("TickerQ.MongoDB", options =>
    TickerQ.MongoDB.DependencyInjection.ServiceExtension.AddOperationalStore(options,
        mongo => mongo.UseTickerQMongoClient("mongodb://127.0.0.1:1", "package_smoke")));
CheckProvider("TickerQ.Caching.StackExchangeRedis", options =>
    TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension.AddStackExchangeRedis(options,
        redis => redis.Configuration = "127.0.0.1:1"));
Console.WriteLine("PACKAGE_SMOKE_OPTIONAL_OK");

static void Configure(TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> options)
{
    options.UseApplicationNamespace("installed-optional-package-smoke");
    options.UseReconciliationEpoch(1);
    options.DisableBackgroundServices();
}

static void CheckProvider(string assembly, Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> configure)
{
    var services = new ServiceCollection();
    services.AddTickerQ(options => { Configure(options); configure(options); });
    var registration = services.Last(item => item.ServiceType == typeof(ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>));
    var owner = registration.ImplementationType?.Assembly ?? registration.ImplementationFactory?.Method.DeclaringType?.Assembly;
    Check(owner?.GetName().Name == assembly, $"{assembly} did not register its persistence provider.");
}

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}
