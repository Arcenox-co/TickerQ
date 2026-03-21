using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Benchmarks;

/// <summary>
/// Benchmarks for function registration and FrozenDictionary creation.
/// Measures the one-time startup cost of building the function registry.
/// TickerQ pays this cost once at startup to get O(1) lookups at runtime.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class FunctionRegistrationBenchmarks
{
    private Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> _functions = null!;

    [Params(10, 50, 200)]
    public int FunctionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        TickerFunctionDelegate noopDelegate = (_, _, _) => Task.CompletedTask;

        _functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>();
        for (int i = 0; i < FunctionCount; i++)
        {
            _functions[$"MyApp.Jobs.Function_{i}"] = ($"*/5 * * * * *", TickerTaskPriority.Normal, noopDelegate, 0);
        }
    }

    [Benchmark(Description = "Build FrozenDictionary from registrations")]
    public FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> BuildFrozenDictionary() =>
        _functions.ToFrozenDictionary();

    [Benchmark(Baseline = true, Description = "Build Dictionary (baseline)")]
    public Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> BuildDictionary() =>
        new(_functions);
}
