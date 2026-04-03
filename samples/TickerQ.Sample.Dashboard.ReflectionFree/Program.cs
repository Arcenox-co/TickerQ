using TickerQ.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Sample.Dashboard.ReflectionFree;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerQ(options =>
{
    options.AddDashboard();
});

// Style 1: Inline group with callback
builder.Services.MapTickerGroup("Background", group =>
{
    group.MapTicker<CleanupJob>()
        .WithCron("0 0 * * * *")
        .WithMaxConcurrency(1);
});

// Style 2: Variable group with shared defaults
var orderJobs = builder.Services.MapTickerGroup("Orders")
    .WithMaxConcurrency(5);

orderJobs.MapTicker<ProcessOrderJob, OrderRequest>();

// No group — lambda-based
builder.Services.MapTicker("InlinePing", (ctx, ct) =>
{
    Console.WriteLine($"[{DateTime.UtcNow}] Ping! Id={ctx.Id}");
    return Task.CompletedTask;
});

builder.Services.MapTickerGroup("gr",gr =>
{
    gr.WithMaxConcurrency(2);
});

var app = builder.Build();

app.UseTickerQ();

app.Run();
