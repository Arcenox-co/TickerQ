using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Benchmarks;

/// <summary>
/// Benchmarks for InternalFunctionContext property updates.
/// SetProperty uses compiled expression trees (cached) vs direct assignment.
/// Shows the cost of the expression-based SetProperty pattern used during job execution.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class InternalFunctionContextBenchmarks
{
    private InternalFunctionContext _context = null!;

    [IterationSetup]
    public void Setup()
    {
        _context = new InternalFunctionContext
        {
            FunctionName = "TestFunction",
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            Status = TickerStatus.Queued
        };
    }

    [Benchmark(Baseline = true, Description = "Direct property assignment")]
    public InternalFunctionContext DirectAssignment()
    {
        _context.Status = TickerStatus.InProgress;
        _context.ElapsedTime = 1234;
        _context.ExceptionDetails = null!;
        return _context;
    }

    [Benchmark(Description = "SetProperty (compiled expression, cached)")]
    public InternalFunctionContext SetPropertyCached()
    {
        _context.SetProperty(x => x.Status, TickerStatus.InProgress);
        _context.SetProperty(x => x.ElapsedTime, 1234);
        _context.SetProperty(x => x.ExceptionDetails, null!);
        return _context;
    }

    [Benchmark(Description = "SetProperty: Single update")]
    public InternalFunctionContext SetProperty_Single() =>
        _context.SetProperty(x => x.Status, TickerStatus.Done);

    [Benchmark(Description = "SetProperty: Chain 5 updates")]
    public InternalFunctionContext SetProperty_Chain5() =>
        _context
            .SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ElapsedTime, 5000)
            .SetProperty(x => x.RetryCount, 2)
            .SetProperty(x => x.ExceptionDetails, "timeout")
            .SetProperty(x => x.ExecutedAt, DateTime.UtcNow);
}
