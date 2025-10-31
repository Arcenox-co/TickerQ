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
> **Repo:** https://github.com/Arcenox-co/TickerQ-UI (docs are open-source and anyone can help us improving.)

> **Note:**
As of v2.2.0, all TickerQ packages are versioned together — even if a package has no changes — to keep the ecosystem in sync. Always update all packages to the same version.

---

## ✨ Features

- **Time and Cron Scheduling**
- **Stateless Core** with source generator
- **EF Core Persistence**
- **Live Dashboard UI**
- **Live Dashboard UI** - [View Screenshots](https://tickerq.arcenox.com/intro/dashboard-overview.html)
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
    opt.SetMaxConcurrency(10);
    options.AddOperationalStore<MyDbContext>(efOpt => 
    {
        efOpt.SetExceptionHandler<MyExceptionHandlerClass>();
        efOpt.UseModelCustomizerForMigrations();
    });
    options.AddDashboard(uiopt =>                                                
    {
        uiopt.BasePath = "/tickerq-dashboard"; 
        uiopt.AddDashboardBasicAuth();
    }
});

app.UseTickerQ(); // Activates job processor
```
> 💡 **Recommendation:**  
Use `UseModelCustomizerForMigrations()` to cleanly separate infrastructure concerns from your core domain model, especially during design-time operations like migrations.  
**Note:** If you're using third-party libraries (e.g., OpenIddict) that also override `IModelCustomizer`, you must either merge customizations or fall back to manual configuration inside `OnModelCreating()` to avoid conflicts.


❗️If Not Using `UseModelCustomizerForMigrations()` You must apply TickerQ configurations manually in your `DbContext`:

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

## Add Migrations

Migrations would be created for `Context` that is declared at `AddOperationalStore`.

```PM
PM> add-migration "TickerQInitialCreate" -c MyDbContext
```

##  Job Definition

### 1. **Cron Job (Recurring)**

```csharp
public class CleanupJobs(ICleanUpService cleanUpService)
{
    private readonly ICleanUpService _cleanUpService = cleanUpService;

    [TickerFunction(functionName: "CleanupLogs", cronExpression: "0 0 * * *" )]
    public asynt Task CleanupLogs(TickerFunctionContext<string> tickerContext, CancellationToken cancellationToken)
    {
        var logFileName = tickerContext.Request; // output cleanup_example_file.txt
        await _cleanUpService.CleanOldLogsAsync(logFileName, cancellationToken);
    }
}
```

> This uses a cron expression to run daily at midnight.

#### 📅 Cron Expression Formats

TickerQ supports both **5-part** and **6-part** cron expressions:

**5-part format (standard):**
```
minute hour day month day-of-week
```
Examples:
- `"0 0 * * *"` - Daily at midnight
- `"0 */6 * * *"` - Every 6 hours
- `"30 14 * * 1"` - Every Monday at 2:30 PM

**6-part format (with seconds):**
```
second minute hour day month day-of-week
```
Examples:
- `"0 0 0 * * *"` - Daily at midnight (00:00:00)
- `"30 0 0 * * *"` - Daily at 00:00:30 (30 seconds past midnight)
- `"0 0 */2 * * *"` - Every 2 hours on the hour
- `"*/10 * * * * *"` - Every 10 seconds

---

Schedule Time Ticker:

```csharp
//Schedule on-time job using ITimeTickerManager<TimeTicker>.
await _timeTickerManager.AddAsync(new TimeTicker
{
    Function = "CleanupLogs",
    ExecutionTime = DateTime.UtcNow.AddMinutes(1),
    Request = TickerHelper.CreateTickerRequest<string>("cleanup_example_file.txt"),
    Retries = 3,
    RetryIntervals = new[] { 30, 60, 120 }, // Retry after 30s, 60s, then 2min

    // Optional batching
    BatchParent = Guid.Parse("...."),
    BatchRunCondition = BatchRunCondition.OnSuccess
});
```
Schedule Cron Ticker:

```csharp
//Schedule cron job using ICronTickerManager<CronTicker>.
await _cronTickerManager.AddAsync(new CronTicker
{
    Function = "CleanupLogs",
    Expression = "0 */6 * * *", // Every 6 hours (5-part format)
    // Or use 6-part format with seconds:
    // Expression = "0 0 */6 * * *", // Every 6 hours at :00:00
    // Expression = "*/30 * * * * *", // Every 30 seconds
    Request = TickerHelper.CreateTickerRequest<string>("cleanup_example_file.txt"),
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
- If you are getting random 403 responses, make sure that you don't have any filter in some endpoint that might be triggering it, thus causing issues with TickerQ's dashboard. Check this [issue](https://github.com/Arcenox-co/TickerQ/issues/155#issuecomment-3175214745) for more details.
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
