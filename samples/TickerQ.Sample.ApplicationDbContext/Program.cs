using Microsoft.EntityFrameworkCore;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Sample.ApplicationDbContext.Data;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(dbOptions =>
{
    dbOptions.UseSqlite(
        "Data Source=tickerq-webapi.db",
        b => b.MigrationsAssembly("TickerQ.Sample.ApplicationDbContext"));
});

// TickerQ setup with SQLite operational store (file-based)
builder.Services.AddTickerQ(options =>
{
    // Physical runtime partition + exact epoch. The generated host-owned
    // ApplicationRuntimePartitioning migration creates the corresponding EF ownership shape.
    options.UseDefinedCronApplicationNamespace("application-db-context-sample");
    options.UseReconciliationEpoch(1);
    // Existing verified single-owner stores only, for one reviewed rollout:
    // options.UseLegacyRuntimePartitionAdoption("application-db-context-sample", 1);
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseApplicationDbContext<AppDbContext>(TickerQ.EntityFrameworkCore.Customizer.ConfigurationType.UseModelCustomizer);
    });
});

var app = builder.Build();

// Apply the host-owned migration history, including ApplicationRuntimePartitioning.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
