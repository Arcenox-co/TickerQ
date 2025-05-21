# TickerQ

[![NuGet](https://img.shields.io/nuget/dt/tickerq.svg)](https://www.nuget.org/packages/tickerq) 
[![NuGet](https://img.shields.io/nuget/vpre/tickerq.svg)](https://www.nuget.org/packages/tickerq)
[![Documentation](https://img.shields.io/badge/docs%20-official%20web-blue)](https://tickerq.arcenox.com)

**Robust. Adaptive. Precise.**  
TickerQ is a fast, reflection-free background task scheduler for .NET — built with source generators, EF Core integration, cron + time-based execution, and a real-time dashboard.
## 📚 Full Docs

👉 [https://tickerq.arcenox.com](https://tickerq.arcenox.com)

---
Dashboard:
![TickerQ Dashboard](https://tickerq.arcenox.com/Screenshot_14-4-2025_155111_localhost.jpeg?v=2)


## ✨ Features

- **Time and Cron Scheduling**
- **Stateless Core** with source generator
- **EF Core Persistence** (optional)
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
    options.AddOperationalStore<MyDbContext>(); // Enables EF-backed storage
    options.CancelMissedTickersOnApplicationRestart(); // Useful in distributed mode
    options.AddDashboard(basePath: "/tickerq-dashboard"); // Dashboard path
    options.AddDashboardBasicAuth(); // Enables simple auth
});

app.UseTickerQ(); // Activates job processor
```

---

##  Job Definition

### 1. **Cron Job (Recurring)**

```csharp
public class CleanupJobs
{
    [TickerFunction(FunctionName = "CleanupLogs", CronExpression = "0 0 * * *")]
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
    [TickerFunction(FunctionName = "SendWelcome")]
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
    RetryIntervals = new[] { 30, 60, 120 } // Retry after 30s, 60s, then 2min
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

    [TickerFunction(FunctionName = "GenerateDailyReport", CronExpression = "0 6 * * *")]
    public async Task GenerateDailyReport()
    {
        await _reportService.GenerateAsync();
    }
}
```

---

## Dashboard UI

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

---

## TickerQ vs Hangfire vs Quartz.NET
| Feature                              | TickerQ                           | Hangfire                          | Quartz.NET                         |
|--------------------------------------|-----------------------------------|-----------------------------------|-------------------------------------|
| Cron scheduling                      | ✅ Yes                            | ✅ Yes                            | ✅ Yes                              |
| Time-based one-time jobs             | ✅ Yes (TimeTicker)               | ⚠️ Simulated via delay            | ✅ Yes                              |
| Persistent job store                 | ✅ With EF Core                   | ✅ Yes                            | ✅ Yes                              |
| In-memory mode                       | ✅ Built-in                       | ✅ Built-in                       | ✅ Built-in                         |
| Retry/cooldown logic                 | ✅ Advanced & configurable        | ⚠️ Basic retries only            | ⚠️ Manual                           |
| Dashboard UI                         | ✅ First-party + real-time        | ✅ Basic                          | ⚠️ Third-party required             |
| DI support                           | ✅ Native and seamless            | 🟠 Partial – type-based only      | ⚠️ Requires extra config            |
| Reflection-free job discovery        | ✅ Roslyn-based, compile-time     | ❌ Uses reflection                | ❌ Uses reflection                  |
| Multi-node/distributed support       | ✅ Native with EF Core            | ⚠️ Depends on storage             | ✅ Yes                              |
| Custom tickers (plugin model)        | ✅ Fully extensible               | ❌ Not extensible                 | ⚠️ Limited                          |
| Parallelism & concurrency control    | ✅ Built-in scheduler threadpool  | ✅ Queues/ServerCount             | ✅ ThreadPools                      |
| Performance under high load          | ✅ Optimized, no overhead         | ⚠️ Depends on storage/db         | ⚠️ Thread blocking possible         |
| Async/await support                  | ✅ Yes                            | ⚠️ Limited – wrapped sync methods| ✅ Yes                              |
| CancellationToken support            | ✅ Propagated & honored           | ❌ Not natively supported         | 🟠 Optional – must check manually   |
---

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
    CronExpression = "0 */6 * * *", // Every 6 hours
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

## 🤝 Contribution

PRs, ideas, and issues are welcome!

1. Fork & branch
2. Code your change
3. Submit a Pull Request

---

## 💖 Sponsors
Want to support this project? [Become a sponsor](https://github.com/sponsors/Arcenox-co/)

## 📄 License

**MIT OR Apache 2.0** © [Arcenox](https://arcenox.com)  
You may choose either license to use this software.
---