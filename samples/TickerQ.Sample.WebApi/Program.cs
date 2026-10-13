using Microsoft.EntityFrameworkCore;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

var builder = WebApplication.CreateBuilder(args);
var licensePath = Environment.GetEnvironmentVariable("TICKERQ_LICENSE_PATH");

// TickerQ setup with SQLite operational store (file-based)
builder.Services.AddTickerQ(options =>
{
    if (!string.IsNullOrWhiteSpace(licensePath))
        options.UseLicense(licensePath);

    // Physical runtime partition + exact epoch. The host-owned
    // ApplicationRuntimePartitioning migration installs the EF ownership shape.
    options.UseDefinedCronApplicationNamespace("web-api-sample");
    options.UseReconciliationEpoch(1);
    // Existing verified single-owner stores only, for one reviewed rollout:
    // options.UseLegacyRuntimePartitionAdoption("web-api-sample", 1);
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
        {
            dbOptions.UseSqlite(
                "Data Source=tickerq-webapi.db",
                b => b.MigrationsAssembly("TickerQ.Sample.WebApi"));
        });
    });
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard());
});

var app = builder.Build();

// Apply the host-owned migration history, including ApplicationRuntimePartitioning.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    db.Database.Migrate();
}

// Activate TickerQ job processor (mirrors docs' minimal setup)
app.UseTickerQ();

// Minimal endpoint to schedule the sample job
app.MapPost("/schedule-sample", async (ITimeTickerManager<TimeTickerEntity> manager) =>
{
    var result = await manager.AddAsync(new TimeTickerEntity
    {
        Function = "WebApiSample_HelloWorld",
        ExecutionTime = DateTime.UtcNow.AddSeconds(5)
    });

    return Results.Ok(new { result.Result.Id, ScheduledFor = result.Result.ExecutionTime });
});

app.Run();

// Simple sample job
public class SampleJobs
{
    [TickerFunction("WebApiSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[WebApi] Hello from TickerQ! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
