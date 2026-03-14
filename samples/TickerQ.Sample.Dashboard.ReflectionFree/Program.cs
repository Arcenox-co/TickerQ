using Microsoft.EntityFrameworkCore;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Utilities.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerQ(options =>
{
    options.AddOperationalStore(ef =>
    {
        ef.UseTickerQDbContext<TickerQDbContext>(db =>
            db.UseSqlite("Data Source=tickerq-reflection-free.db"));
    });

    options.AddDashboard();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TickerQDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

app.UseTickerQ();

app.Run();
