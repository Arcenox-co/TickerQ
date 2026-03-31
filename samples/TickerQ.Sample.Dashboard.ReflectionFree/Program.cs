using TickerQ.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Utilities.Base;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerQ(options =>
{
    options.AddDashboard();
});

var app = builder.Build();

app.UseTickerQ();

// Approach 2: Interface-based registration
app.MapTicker<TickerQ.Sample.Dashboard.ReflectionFree.CleanupJob>()
   .WithCron("0 0 * * * *");

app.MapTicker<TickerQ.Sample.Dashboard.ReflectionFree.ProcessOrderJob>();

// Approach 3: Lambda-based registration
app.MapTimeTicker("InlinePing", async (ctx, ct) =>
{
    Console.WriteLine($"[{DateTime.UtcNow}] Ping! Id={ctx.Id}");
});

app.Run();
