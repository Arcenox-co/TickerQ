### Choosing Your Setup

TickerQ supports two main job types:
- **Timer jobs**: Run at a specific time or after a delay
- **Cron jobs**: Run on a schedule (e.g., every minute)

You can use either in-memory or EF Core mode:
- **In-memory**: Good for local tools and simple cron jobs; no persistence or retry logic
- **EF Core**: Recommended for production; supports job persistence, retries, and distributed task management

**Recommended packages:**
- `TickerQ` (core scheduling)
- `TickerQ.EntityFrameworkCore` (persistence)
- `TickerQ.Dashboard` (real-time monitoring)

Configure your database context and connection string in `appsettings.json` for EF Core mode. For dashboard access, set the path and credentials in your configuration.
# Getting Started

## 5‑Minute Quick Start

1. Install packages:
```bash
dotnet add package TickerQ
dotnet add package TickerQ.EntityFrameworkCore
dotnet add package TickerQ.Dashboard
```
2. Add DbContext + migrations
```bash
dotnet ef migrations add TickerQInitialCreate
dotnet ef database update
```
3. Register TickerQ
```csharp
builder.Services.AddTickerQ(o =>
{
    o.SetMaxConcurrency(0); // 0 -> Environment.ProcessorCount
    o.AddOperationalStore<MyDbContext>(ef =>
    {
        ef.UseModelCustomizerForMigrations();
        ef.CancelMissedTickersOnAppStart();
    });
    o.AddDashboard(d => d.EnableBasicAuth = true);
});
```
4. Create a job
```csharp
class Jobs {
    [TickerFunction("Hello", cronExpression: "* * * * *")] // every minute
    public void Hello() => Console.WriteLine("Hello TickerQ");
}
```
5. Activate
```csharp
app.UseTickerQ();
```
6. Visit dashboard: https://localhost:<port>/tickerq-dashboard (login if basic auth enabled)

You now have a running recurring job.

## Installation

### Core (required)
```bash
dotnet add package TickerQ
```

### Entity Framework Integration (optional)
```bash
dotnet add package TickerQ.EntityFrameworkCore
```

### Dashboard UI (optional)
```bash
dotnet add package TickerQ.Dashboard
```

## Basic Setup

In `Program.cs` or `Startup.cs`:
```csharp
builder.Services.AddTickerQ(options =>
{
    options.SetMaxConcurrency(10);
    options.AddOperationalStore<MyDbContext>(efOpt => 
    {
        efOpt.SetExceptionHandler<MyExceptionHandlerClass>();
        efOpt.UseModelCustomizerForMigrations();
    });
    options.AddDashboard(uiopt =>                                                
    {
        uiopt.BasePath = "/tickerq-dashboard"; 
        uiopt.AddDashboardBasicAuth();
    });
});

app.UseTickerQ(); // Activates job processor
```

See [TickerQ Intro](https://tickerq.arcenox.com/intro/what-is-tickerq.html) for more details.

### Why Modular Packages?

TickerQ is split into modular packages so you can choose only what you need:
- **Core**: Fast, stateless scheduling engine.
- **Dashboard**: Real-time UI for job management and monitoring.
- **Entity Framework Core**: Persistence, job state tracking, and robust recovery after restarts.

For most production scenarios, using all three is recommended for scalability, monitoring, and reliability.

See [TickerQ Intro](https://tickerq.arcenox.com/intro/what-is-tickerq.html) for more details.
