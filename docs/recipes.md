# Recipes

Practical, copy‑paste task patterns.

## 1. One-Off Delayed Job With Payload
```csharp
public record EmailRequest(string To, string Subject);

[HttpPost("/email/welcome")]
public IActionResult ScheduleEmail([FromServices] ITimeTickerManager<TimeTicker> tm)
{
    tm.AddAsync(new TimeTicker
    {
        FunctionName = "SendWelcomeEmail",
        ExecuteAt = DateTime.UtcNow.AddMinutes(5),
        Request = new EmailRequest("user@example.com", "Welcome")
    });
    return Accepted();
}

[TickerFunction("SendWelcomeEmail")]
public void SendWelcomeEmail(TickerFunctionContext ctx, EmailRequest req)
{
    Console.WriteLine($"Sending to {req.To} : {req.Subject}");
}
```

## 2. Cron Job With Retries & Backoff
```csharp
[TickerFunction("NightlyReport", cronExpression: "0 2 * * *")]
public async Task NightlyReportAsync()
{
    // work
}
```
Define retry policy in dashboard when scheduling or via operational seeding.

## 3. Seeding Initial Tickers
```csharp
builder.Services.AddTickerQ(o =>
{
    o.AddOperationalStore<AppDbContext>(ef =>
    {
        ef.UseTickerSeeder(
            timeTickerAsync: async tm =>
            {
                await tm.AddAsync(new TimeTicker
                {
                    FunctionName = "InitialSync",
                    ExecuteAt = DateTime.UtcNow.AddSeconds(30)
                });
            },
            cronTickerAsync: async cm =>
            {
                await cm.AddAsync(new CronTicker
                {
                    FunctionName = "Heartbeat",
                    CronExpression = "*/5 * * * *"
                });
            });
    });
});
```

## 4. Global Exception Handling
```csharp
public class MyHandler : ITickerExceptionHandler
{
    public Task HandleExceptionAsync(Exception ex, Guid id, TickerType type)
    { /* log + alert */ return Task.CompletedTask; }
    public Task HandleCanceledExceptionAsync(Exception ex, Guid id, TickerType type)
    { /* optional */ return Task.CompletedTask; }
}

builder.Services.AddTickerQ(o => o.SetExceptionHandler<MyHandler>());
```

## 5. Cancel a Scheduled Job
```csharp
var cancelled = TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);
```

## 6. Multi-Node Tagging
```csharp
builder.Services.AddTickerQ(o => o.SetInstanceIdentifier(Environment.MachineName));
```

## 7. Adjust Missed Job Scan Interval
```csharp
builder.Services.AddTickerQ(o => o.UpdateMissedJobCheckDelay(TimeSpan.FromSeconds(45)));
```

## 8. Priority Execution
```csharp
[TickerFunction("CriticalCleanup", cronExpression: "*/2 * * * *", taskPriority: TickerTaskPriority.High)]
public Task CriticalCleanup() => Task.CompletedTask;
```

## 9. Basic Auth Config
```json
"TickerQ:BasicAuth": { "Username": "admin", "Password": "admin" }
```

## 10. Request Payload Retrieval (advanced)
```csharp
var payload = await TickerRequestProvider.GetRequestAsync<MyDto>(sp, tickerId, TickerType.Timer);
```

More patterns welcome—contribute via PR.
