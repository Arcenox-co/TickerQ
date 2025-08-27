using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.WithDashboard.Interfaces;
using TickerQ.WithDashboard.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

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
    options.AddDashboard(dashboard =>
    {
        dashboard.BasePath = "/tickerq-dashboard";
        dashboard.EnableBasicAuth = true;
    });
});

builder.Services.AddScoped<IHelloWorldService, HelloWorldService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseTickerQ();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
