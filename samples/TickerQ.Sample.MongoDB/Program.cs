using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.MongoDB.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

var builder = WebApplication.CreateBuilder(args);

// TickerQ scheduler mode requires MongoDB transaction support (replica set or mongos).
// A standalone server is suitable only for explicitly documented non-transactional operations.
builder.Services.AddTickerQ(options =>
{
    // Physical document/lock/result partition + exact activated deployment epoch.
    options.UseDefinedCronApplicationNamespace("mongodb-sample");
    options.UseReconciliationEpoch(1);
    // Existing verified single-owner replica-set/mongos stores only:
    // options.UseLegacyRuntimePartitionAdoption("mongodb-sample", 1);
    options.AddOperationalStore(mongoOptions =>
    {
        mongoOptions.UseTickerQMongoClient(
            connectionString: builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017",
            databaseName: builder.Configuration["Mongo:Database"] ?? "tickerq");
    });
    options.AddDashboard();
});

var app = builder.Build();

// Activate TickerQ job processor (indexes are provisioned automatically by IHostedService)
app.UseTickerQ();

// Minimal endpoint to schedule the sample job
app.MapPost("/schedule-sample", async (ITimeTickerManager<TimeTickerEntity> manager) =>
{
    var result = await manager.AddAsync(new TimeTickerEntity
    {
        Function = "MongoSample_HelloWorld",
        ExecutionTime = DateTime.UtcNow.AddSeconds(5)
    });

    return Results.Ok(new { result.Result.Id, ScheduledFor = result.Result.ExecutionTime });
});

app.Run();

public class SampleJobs
{
    [TickerFunction("MongoSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Mongo] Hello from TickerQ! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
