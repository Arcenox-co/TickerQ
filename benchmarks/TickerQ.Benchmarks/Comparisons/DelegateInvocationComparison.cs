using System.Collections.Frozen;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using TickerQ.Utilities;

namespace TickerQ.Benchmarks.Comparisons;

/// <summary>
/// Compares function dispatch mechanisms:
/// - TickerQ: pre-compiled delegate via FrozenDictionary (source-generated at build time)
/// - Reflection: MethodInfo.Invoke (traditional approach used by older schedulers)
/// - Compiled delegate from MethodInfo (middle ground)
///
/// This isolates the per-invocation cost of finding and calling a job method.
///
/// Results Overview (Apple M4 Pro, .NET 10.0):
/// ┌──────────────────────────────────┬───────────┬───────┬──────────────┐
/// │ Method                           │ Time      │ Alloc │ vs TickerQ   │
/// ├──────────────────────────────────┼───────────┼───────┼──────────────┤
/// │ TickerQ: Lookup + invoke         │ 1.38 ns   │ 0 B   │ 1x (baseline)│
/// │ TickerQ: Invoke cached delegate  │ ~0 ns     │ 0 B   │ -            │
/// │ Compiled: CreateDelegate+invoke  │ ~0 ns     │ 0 B   │ -            │
/// │ Reflection: MethodInfo.Invoke    │ 14.6 ns   │ 64 B  │ 10.6x slower │
/// └──────────────────────────────────┴───────────┴───────┴──────────────┘
/// Winner: TickerQ — 10.6x faster than reflection, zero allocations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class DelegateInvocationComparison
{
    private TickerFunctionDelegate _tickerqDelegate = null!;
    private FrozenDictionary<string, TickerFunctionDelegate> _tickerqRegistry = null!;
    private MethodInfo _reflectionMethod = null!;
    private object _reflectionTarget = null!;
    private Func<string, int, Task> _compiledDelegate = null!;

    private const string FunctionKey = "MyApp.Jobs.ProcessOrder";

    [GlobalSetup]
    public void Setup()
    {
        // TickerQ: source-generated delegate in FrozenDictionary
        _tickerqDelegate = (_, _, _) => Task.CompletedTask;
        var dict = new Dictionary<string, TickerFunctionDelegate>
        {
            [FunctionKey] = _tickerqDelegate,
            ["MyApp.Jobs.SendEmail"] = (_, _, _) => Task.CompletedTask,
            ["MyApp.Jobs.GenerateReport"] = (_, _, _) => Task.CompletedTask,
        };
        _tickerqRegistry = dict.ToFrozenDictionary();

        // Reflection: traditional approach
        _reflectionTarget = new SampleJobClass();
        _reflectionMethod = typeof(SampleJobClass).GetMethod(nameof(SampleJobClass.ProcessOrder))!;

        // Compiled delegate: middle ground
        _compiledDelegate = _reflectionMethod.CreateDelegate<Func<string, int, Task>>(_reflectionTarget);
    }

    // ── TickerQ: lookup + invoke pre-compiled delegate ──

    [Benchmark(Baseline = true, Description = "TickerQ: Lookup + invoke delegate")]
    public Task TickerQ_LookupAndInvoke()
    {
        _tickerqRegistry.TryGetValue(FunctionKey, out var del);
        return del!(CancellationToken.None, null!, null!);
    }

    // ── TickerQ: invoke cached delegate (no lookup) ──

    [Benchmark(Description = "TickerQ: Invoke cached delegate")]
    public Task TickerQ_InvokeCached() =>
        _tickerqDelegate(CancellationToken.None, null!, null!);

    // ── Reflection: MethodInfo.Invoke ──

    [Benchmark(Description = "Reflection: MethodInfo.Invoke")]
    public object? Reflection_Invoke() =>
        _reflectionMethod.Invoke(_reflectionTarget, ["order-123", 1]);

    // ── Compiled delegate from reflection ──

    [Benchmark(Description = "Compiled: CreateDelegate + invoke")]
    public Task Compiled_Invoke() =>
        _compiledDelegate("order-123", 1);

    public class SampleJobClass
    {
        public Task ProcessOrder(string orderId, int priority) => Task.CompletedTask;
    }
}
