using TickerQ.Utilities.Base;

namespace TickerQ.Sample.Dashboard.ReflectionFree;

public class SampleJobs
{
    [TickerFunction("ReflectionFree_CronJob", "0 */5 * * * *")]
    public Task CronJobAsync(TickerFunctionContext context)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Cron job executed! Id={context.Id}");
        return Task.CompletedTask;
    }

    [TickerFunction("ReflectionFree_TimeJob")]
    public Task TimeJobAsync(TickerFunctionContext context)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Time job executed! Id={context.Id}");
        return Task.CompletedTask;
    }
}
