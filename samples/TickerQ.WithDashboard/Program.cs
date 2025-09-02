using Microsoft.EntityFrameworkCore;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.WithDashboard.Interfaces;
using TickerQ.WithDashboard.Services;
using TickerQ.WithDashboard.Jobs;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.WithDashboard.Data;
using TickerQ.Utilities.Models.Ticker;
using TickerQ.Utilities;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure Entity Framework with PostgreSQL
builder.Services.AddDbContext<TickerQDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddTickerQ(options =>
{
    /*
     * Access at:
     * - https://localhost:7045/tickerq-dashboard/
     * - http://localhost:5149/tickerq-dashboard/
     */
    options.SetMaxConcurrency(10);
    options.AddDashboard(config =>
    {
        config.BasePath = "/tickerq-dashboard";
        config.EnableBasicAuth = true;
    });
    // Configure PostgreSQL persistence to test issue #195 - duplicate key violation on CronTickerOccurrences
    options.AddOperationalStore<TickerQDbContext>(ef =>
    {
        // Using manual configuration in TickerQDbContext.OnModelCreating instead of UseModelCustomizerForMigrations
        // to ensure migrations work properly at design-time
        
        // @alikleit's exact reproduction method - UseTickerSeeder with CleanupLogs2
        ef.UseTickerSeeder(
            // TIME TICKER
            async timeTicker => {
                await timeTicker.AddAsync(new TimeTicker()
                {
                    Id = Guid.NewGuid(),
                    Function = "CleanupLogs2",
                    ExecutionTime = DateTime.UtcNow.AddSeconds(5),
                    Request = TickerHelper.CreateTickerRequest<string>("cleanup_example_file.txt"),
                    Retries = 3,
                    RetryIntervals = [30, 60, 120], // Retry after 30s, 60s, then 2min
                });
            },
            // CRON TICKER  
            async cronTicker => {
                await cronTicker.AddAsync(new CronTicker()
                {
                    Id = Guid.NewGuid(),
                    Function = "CleanupLogs2",
                    Expression = "* * * * *",  // every minute - IDENTICAL to JobA/JobB
                    Request = TickerHelper.CreateTickerRequest<string>("cleanup_example_file.txt"),
                    Retries = 3,
                    RetryIntervals = [30, 60, 120], // Retry after 30s, 60s, then 2min
                });
            });
    });
});

builder.Services.AddScoped<IHelloWorldService, HelloWorldService>();
builder.Services.AddScoped<SimpleJobA>();
builder.Services.AddScoped<SimpleJobB>();
builder.Services.AddScoped<MyFirstExample>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Apply database migrations for TickerQ FIRST (needed to reproduce issue #195)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    dbContext.Database.Migrate(); // Apply migrations to create TickerQ schema
}

app.UseTickerQ();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
