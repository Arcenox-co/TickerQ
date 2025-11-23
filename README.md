
<h1 align="center">TickerQ</h1>

<p align="center">
  <img src="https://tickerq.net/tickerq-logo.png" alt="TickerQ Logo" width="200" />
</p>


[![Discord Community](https://img.shields.io/badge/Discord-TickerQ-5865F2?logo=discord&logoColor=white&style=for-the-badge)](https://discord.gg/ZJemWvp9MK)


[![NuGet](https://img.shields.io/nuget/dt/tickerq.svg)](https://www.nuget.org/packages/tickerq) 
[![NuGet](https://img.shields.io/nuget/vpre/tickerq.svg)](https://www.nuget.org/packages/tickerq)
[![Build NuGet Packages](https://github.com/Arcenox-co/TickerQ/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/Arcenox-co/TickerQ/actions/workflows/build.yml)
[![Documentation](https://img.shields.io/badge/docs%20-official%20web-blue)](https://tickerq.arcenox.com)
[![](https://opencollective.com/tickerq/tiers/badge.svg)](https://opencollective.com/tickerq)


**Robust. Adaptive. Precise.**  
TickerQ is a fast, reflection-free background task scheduler for .NET — built with source generators, EF Core integration, cron + time-based execution, and a real-time dashboard.

### 📚 [Full Docs: https://tickerq.net](https://tickerq.net)
(Docs are open-source and anyone can help us improving [Documentation Repository](https://github.com/Arcenox-co/TickerQ-UI)

> **Note:**
All TickerQ packages are versioned together — even if a package has no changes — to keep the ecosystem in sync. Always update all packages to the same version.

> **Important:** The entire 2.* package line is deprecated, considered legacy, and is no longer maintained. For all current and future development, use the .NET 8+ versions of TickerQ.
---

## ✨ Features

- Time and cron scheduling for one-off and recurring jobs
- Reflection-free core with source-generated job handlers
- EF Core persistence for jobs, state, and history
- Live Dashboard UI - [View Screenshots](https://tickerq.net/features/dashboard.html#dashboard-screenshots)
- Retry policies & throttling for robust execution
- First-class dependency injection support
- Multi-node distributed coordination (via Redis heartbeats and dead-node cleanup)
---

# Quick Start

Get up and running with TickerQ in under 5 minutes.

## Prerequisites

- .NET 8.0 or later
- A .NET project (Console, Web API, or ASP.NET Core)

## Step 1: Install TickerQ

```bash
dotnet add package TickerQ
```

## Step 2: Register Services

Add TickerQ to your `Program.cs`:

```csharp
using TickerQ.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerQ();

var app = builder.Build();
app.UseTickerQ(); // Activate job processor
app.Run();
```

## Step 3: Create a Job Function

Create a job function with the `[TickerFunction]` attribute:

```csharp
using TickerQ.Utilities.Base;

public class MyJobs
{
    [TickerFunction("HelloWorld")]
    public async Task HelloWorld(
        TickerFunctionContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello from TickerQ! Job ID: {context.Id}");
        Console.WriteLine($"Scheduled at: {DateTime.UtcNow:HH:mm:ss}");
    }
}
```

## Step 4: Schedule the Job

Inject the manager and schedule your job:

```csharp
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

public class MyService
{
    private readonly ITimeTickerManager<TimeTickerEntity> _timeTickerManager;
    
    public MyService(ITimeTickerManager<TimeTickerEntity> timeTickerManager)
    {
        _timeTickerManager = timeTickerManager;
    }
    
    public async Task ScheduleJob()
    {
        var result = await _timeTickerManager.AddAsync(new TimeTickerEntity
        {
            Function = "HelloWorld",
            ExecutionTime = DateTime.UtcNow.AddSeconds(10) // Run in 10 seconds
        });
        
        if (result.IsSucceeded)
        {
            Console.WriteLine($"Job scheduled! ID: {result.Result.Id}");
        }
    }
}
```

## Step 5: Run Your Application

```bash
dotnet run
```

Wait 10 seconds and you should see:
```
Job scheduled! ID: {guid}
Hello from TickerQ! Job ID: {guid}
Scheduled at: {time}
```

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
