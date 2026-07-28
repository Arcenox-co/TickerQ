using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;
using System.Text.Json.Serialization;

namespace TickerQ.Sample.Dashboard.ReflectionFree;

// Approach 1: Attribute-based (existing)
public class SampleJobs
{
    [TickerFunction("ReflectionFree_CronJob", "0 */5 * * * *")]
    public Task CronJobAsync(TickerFunctionContext context)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Cron job executed! Id={context.Id}");
        return Task.CompletedTask;
    }

    [TickerFunction("ReflectionFree_TimeJob")]
    public Task TimeJobAsync(TickerFunctionContext<OrderRequest> context)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Time job executed! Id={context.Id}");
        return Task.CompletedTask;
    }

    [TickerFunction("ReflectionFree_IncludedAccessorJob")]
    public Task IncludedAccessorJobAsync(TickerFunctionContext<IncludedAccessorRequest> context)
        => Task.CompletedTask;
}

// Approach 2: Interface-based (new) — registered via app.MapTicker<T>()
public class CleanupJob : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Cleanup executed! Id={context.Id}");
        return Task.CompletedTask;
    }
}

public record OrderRequest(string OrderId, decimal Amount, OrderCustomer? Customer = null);

public record OrderCustomer(string Email, string? DisplayName = null);

public class IncludedAccessorRequest
{
    [JsonInclude]
    internal string InternalValue { get; set; } = string.Empty;

    [JsonInclude]
    protected internal string ProtectedInternalValue { get; set; } = string.Empty;

    internal bool Matches(string value)
        => InternalValue == value && ProtectedInternalValue == value;
}

[JsonSerializable(typeof(OrderRequest))]
[JsonSerializable(typeof(OrderCustomer))]
[JsonSerializable(typeof(IncludedAccessorRequest))]
internal partial class TickerQRequestJsonContext : JsonSerializerContext;

public class ProcessOrderJob : ITickerFunction<OrderRequest>
{
    public Task ExecuteAsync(TickerFunctionContext<OrderRequest> context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Processing order {context.Request?.OrderId} for ${context.Request?.Amount}");
        return Task.CompletedTask;
    }
}
