## Timer & Cron Job Examples

```csharp
// Timer job: runs every 30 minutes
[TickerFunction(functionName: "Cleanup", cronExpression: "*/30 * * * *")]
public void Cleanup() {
    Console.WriteLine("Cleanup job executed.");
}

// Cron job: runs every minute
[TickerFunction(functionName: "Report", cronExpression: "* * * * *")]
public void Report() {
    // ...report logic...
}
```

## Dependency Injection Example

```csharp
public interface IReportService {
    Task GenerateReport();
}

public class ReportService : IReportService {
    public async Task GenerateReport() {
        await Task.Delay(1000);
        Console.WriteLine("Report generated.");
    }
}

builder.Services.AddScoped<IReportService, ReportService>();

public class ReportJob {
    private readonly IReportService _service;
    public ReportJob(IReportService service) {
        _service = service;
    }
    [TickerFunction(functionName: "Report", cronExpression: "* * * * *")]
    public async Task RunReport() {
        await _service.GenerateReport();
    }
}
```

## Programmatic Scheduling via API

```csharp
[ApiController]
[Route("api/notification")]
public class NotificationController : ControllerBase {
    private readonly ITimeTickerManager _manager;
    public NotificationController(ITimeTickerManager manager) {
        _manager = manager;
    }
    [HttpPost("send-email")]
    public IActionResult SendEmail([FromBody] EmailRequest request) {
        _manager.CreateTimeTicker(
            executionTime: DateTime.Now.AddSeconds(5),
            functionName: "WelcomeEmail",
            request: request,
            description: "Welcome email",
            retries: 3,
            retryIntervals: new[] { TimeSpan.FromMinutes(1) }
        );
        return Ok();
    }
}
```

## Task Priorities

You can assign a priority to a function using `TickerTaskPriority` via the attribute:
```csharp
[TickerFunction(functionName: "HeavyJob", cronExpression: "*/5 * * * *", taskPriority: TickerTaskPriority.High)]
public async Task HeavyJobAsync() { /* ... */ }
```
Priorities:
- `High` – executed before normal tasks
- `Normal` – default ordering
- `Low` – executed after higher priority tasks
- `LongRunning` – scheduled on the default .NET TaskScheduler using a dedicated thread

TickerQ internally stages tasks and flushes them to its custom scheduler, ensuring high-priority tasks are dequeued first.

## Cancellation & Control

You can cancel a scheduled ticker programmatically:
```csharp
var cancelled = TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);
```
Use `TickerFunctionContext` inside jobs to:
- `context.RetryCount` – current retry attempt
- `context.IsDue` – if execution was due on startup catch-up
- `context.Cancel()` – cancel future execution for that ticker

## Multi-Node Identification
Set an instance identifier for diagnostics and node-aware operations:
```csharp
builder.Services.AddTickerQ(o => o.SetInstanceIdentifier("node-eu-1"));
```
# Usage

## Defining Jobs

Use `[TickerFunction]` to register jobs:
```csharp
class BackgroundService
{
    [TickerFunction(functionName: nameof(HelloWorld), cronExpression: "* * * * *")]
    public void HelloWorld()
    {
        Console.WriteLine("Hello World!");
    }
}
```

## Dependency Injection Example
```csharp
builder.Services.AddScoped<IHelloWorldService, HelloWorldService>();

class BackgroundService
{
    private readonly IHelloWorldService _helloWorldService;
    public BackgroundService(IHelloWorldService helloWorldService)
    {
        _helloWorldService = helloWorldService;
    }
    [TickerFunction(functionName: nameof(HelloWorld), cronExpression: "* * * * *")]
    public void HelloWorld()
    {
        _helloWorldService.SayHello();
    }
}
```

## Running the Host
```csharp
await host.RunAsync();
```

See [TickerQ Usage](https://tickerq.arcenox.com/intro/usage.html) for more.

## Programmatic Scheduling

You can schedule jobs programmatically via endpoints or service calls. For example:

```csharp
// Example endpoint for scheduling a job
[HttpPost("/schedule")]
public IActionResult ScheduleJob([FromBody] Point point)
{
    var manager = ... // Get ITimeTickerManager
    var request = new TickerRequest<Point>(point);
    manager.CreateTimeTicker(
        executionTime: DateTime.Now.AddSeconds(10),
        functionName: nameof(MyJobs.WithObject),
        request: request,
        description: "Test with object",
        retries: 3,
        retryIntervals: new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2) }
    );
    return Ok();
}
```

### Time vs Cron Jobs
- **Time jobs**: Run at a specific future time
- **Cron jobs**: Run on a schedule (e.g., every minute)

Both can be scheduled via code or the dashboard, with full support for parameters and custom logic.
