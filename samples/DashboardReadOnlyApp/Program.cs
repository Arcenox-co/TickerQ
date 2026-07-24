using Microsoft.EntityFrameworkCore;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;

var builder = WebApplication.CreateBuilder(args);

// Read-only companion to DashboardTestApp. Verifies observability-only mode:
//  - "Read-only" badge in the top bar, all create/edit/delete/run UI hidden
//  - server rejects every mutation endpoint with 403 (try:
//      curl -X POST http://localhost:5211/tickerq/dashboard/api/dashboard/host/stop)
// No auth on purpose so the read-only behaviour is isolated from login flows.
builder.Services.AddTickerQ(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
            dbOptions.UseSqlite("Data Source=dashboard-readonly.db"));
    });

    options.AddDashboard(dash =>
    {
        dash.SetTitle("Acme Jobs — Read-only Viewer");
        dash.SetReadOnly();
        dash.WithNoAuth();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    db.Database.EnsureCreated();
}

app.UseTickerQ();

app.MapGet("/", () => Results.Redirect("/tickerq/dashboard"));

app.Run("http://localhost:5211");

// Same cron shapes as the main test app so the viewer has live data to show.
public class ViewerJobs
{
    [TickerFunction("Heartbeat", "* * * * *")]
    public Task Heartbeat(TickerFunctionContext context, CancellationToken ct)
    {
        Console.WriteLine($"[Heartbeat] {DateTime.UtcNow:O}");
        return Task.CompletedTask;
    }

    [TickerFunction("AlwaysFails", "*/2 * * * *")]
    public Task AlwaysFails(TickerFunctionContext context, CancellationToken ct)
    {
        throw new InvalidOperationException("AlwaysFails failed on purpose.");
    }
}
