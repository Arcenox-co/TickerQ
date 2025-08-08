# TickerQ


[![Discord Community](https://img.shields.io/badge/Discord-TickerQ-5865F2?logo=discord&logoColor=white&style=for-the-badge)](https://discord.gg/ZJemWvp9MK)


[![NuGet](https://img.shields.io/nuget/dt/tickerq.svg)](https://www.nuget.org/packages/tickerq) 
[![NuGet](https://img.shields.io/nuget/vpre/tickerq.svg)](https://www.nuget.org/packages/tickerq)
[![Build NuGet Packages](https://github.com/Arcenox-co/TickerQ/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/Arcenox-co/TickerQ/actions/workflows/build.yml)
[![Documentation](https://img.shields.io/badge/docs%20-official%20web-blue)](https://tickerq.arcenox.com)
[![](https://opencollective.com/tickerq/tiers/badge.svg)](https://opencollective.com/tickerq)


**Robust. Adaptive. Precise.**  
TickerQ is a fast, reflection-free background task scheduler for .NET — built with source generators, EF Core integration, cron + time-based execution, and a real-time dashboard.

### 📚 Full Docs: [https://tickerq.arcenox.com](https://tickerq.arcenox.com)
> **Note:**
As of v2.2.0, all TickerQ packages are versioned together — even if a package has no changes — to keep the ecosystem in sync. Always update all packages to the same version.

---
## ✨ Features

- **Time and Cron Scheduling**
- **Stateless Core** with source generator
- **EF Core Persistence**
- **Live Dashboard UI**
- **Retry Policies & Throttling**
- **Dependency Injection support**
- **Multi-node distributed coordination**
---

## 📦 Installation

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

---

## ⚙️ Basic Setup

### In `Program.cs` or `Startup.cs`

```csharp
builder.Services.AddTickerQ(options =>
{
    options.SetMaxConcurrency(4); // Optional
    options.SetExceptionHandler<MyExceptionHandler>(); // Optional
    options.AddOperationalStore<MyDbContext>(efOpt => 
    {
        efOpt.UseModelCustomizerForMigrations(); // Applies custom model customization only during EF Core migrations
        efOpt.CancelMissedTickersOnApplicationRestart(); // Useful in distributed mode
    }); // Enables EF-backed storage
    options.AddDashboard(basePath: "/tickerq-dashboard"); // Dashboard path
    options.AddDashboardBasicAuth(); // Enables simple auth
});

app.UseTickerQ(); // Activates job processor
```
---

## ❗️If Not Using `UseModelCustomizerForMigrations()`

### You must apply TickerQ configurations manually in your `DbContext`:

```csharp
public class MyDbContext : DbContext
{
    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Apply TickerQ entity configurations explicitly
        builder.ApplyConfiguration(new TimeTickerConfigurations());
        builder.ApplyConfiguration(new CronTickerConfigurations());
        builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations());

        // Alternatively, apply all configurations from assembly:
        // builder.ApplyConfigurationsFromAssembly(typeof(TimeTickerConfigurations).Assembly);
    }
}
```

> 💡 **Recommendation:**  
Use `UseModelCustomizerForMigrations()` to cleanly separate infrastructure concerns from your core domain model, especially during design-time operations like migrations.  
**Note:** If you're using third-party libraries (e.g., OpenIddict) that also override `IModelCustomizer`, you must either merge customizations or fall back to manual configuration inside `OnModelCreating()` to avoid conflicts.

##  Job Definition

### 1. **Cron Job (Recurring)**

```csharp
public class CleanupJobs
{
    [TickerFunction(functionName: "CleanupLogs", cronExpression: "0 0 * * *" )]
    public void CleanupLogs()
    {
        // Runs every midnight
    }
}
```

> This uses a cron expression to run daily at midnight.

---

### 2. **One-Time Job (TimeTicker)**

```csharp
public class NotificationJobs
{
    [TickerFunction(functionName: "SendWelcome")]
    public Task SendWelcome(TickerFunctionContext<string> tickerContext ,CancellationToken ct)
    {
        Console.WriteLine(tickerContext.Request); // Output: User123
        return Task.CompletedTask;
    }
}
```

Then schedule it:

```csharp
await _timeTickerManager.AddAsync(new TimeTicker
{
    Function = "SendWelcome",
    ExecutionTime = DateTime.UtcNow.AddMinutes(1),
    Request = TickerHelper.CreateTickerRequest<string>("User123"),
    Retries = 3,
    RetryIntervals = new[] { 30, 60, 120 }, // Retry after 30s, 60s, then 2min

    // Optional batching
    BatchParent = Guid.Parse("...."),
    BatchRunCondition = BatchRunCondition.OnSuccess
});
```

---

### 3. **Injecting Services in Jobs (Fully DI Support)**

```csharp
public class ReportJobs
{
    private readonly IReportService _reportService;

    public ReportJobs(IReportService reportService)
    {
        _reportService = reportService;
    }

    [TickerFunction(functionName: "GenerateDailyReport", cronExpression: "0 6 * * *")]
    public async Task GenerateDailyReport()
    {
        await _reportService.GenerateAsync();
    }
}
```

---

## Dashboard UI

### Check out Dashboard Overview:  [TickerQ-Dashboard-Examples](https://tickerq.arcenox.com/intro/dashboard-overview.html)

Enabled by adding:

```csharp
options.AddDashboard(basePath: "/tickerq-dashboard");
options.AddDashboardBasicAuth(); // Optional
```

Accessible at `/tickerq-dashboard`, it shows:

- System status
- Active tickers
- Job queue state
- Cron ticker stats
- Execution history
- Trigger/cancel/edit jobs live

Auth config (optional):

```json
"TickerQBasicAuth": {
  "Username": "admin",
  "Password": "admin"
}
```

## 🔐 Retry & Locking

TickerQ supports:

- Retries per job
- Retry intervals (`RetryIntervals`)
- Distributed locking (EF mode only)
- Job ownership tracking across instances
- Cooldown on job failure

---

## 🧪 Advanced: Manual CronTicker Scheduling

```csharp
await _cronTickerManager.AddAsync(new CronTicker
{
    Function = "CleanupLogs",
    Expression = "0 */6 * * *", // Every 6 hours
    Retries = 2,
    RetryIntervals = new[] { 60, 300 }
});
```

---

## 🛠️ Developer Tips

- Use `[TickerFunction]` to register jobs
- Use `FunctionName` consistently across schedule and handler
- Use `CancellationToken` for graceful cancellation
- Use `Request` to pass dynamic data to jobs
---

## 💖 Sponsors & Backers

We want to acknowledge the individuals and organizations who support the development of TickerQ through [OpenCollective](https://opencollective.com/tickerq). Your contributions help us maintain and grow this project. If you'd like to support, check out the tiers below and join the community!


[Become a Sponsor or Backer on OpenCollective](https://opencollective.com/tickerq)

---

### 🏆 Gold Sponsors
*Become a gold sponsor and get your logo here with a link to your site.*

---

### 🥈 Silver Sponsors
*Become a silver sponsor and get your logo here with a link to your site.*

---

### 🥉 Bronze Sponsors
*Become a bronze sponsor and get your logo here with a link to your site.*

---

### 🙌 Backers
[Become a backer](https://opencollective.com/tickerq#backer) and get your image on our README on GitHub with a link to your site.

<a href="https://opencollective.com/tickerq/backer/0/website?requireActive=false" target="_blank"><img width="30" src="https://opencollective.com/tickerq/backer/0/avatar.svg?requireActive=false"></a>
---

## 🤝 Contribution

PRs, ideas, and issues are welcome!

1. Fork & branch
2. Code your change
3. Submit a Pull Request

---

## 📄 License

**MIT OR Apache 2.0** © [Arcenox](https://arcenox.com)  
You may choose either license to use this software.
