using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Benchmarks;

/// <summary>
/// Benchmarks for TickerFunctionProvider's FrozenDictionary-based function lookup.
/// Compares FrozenDictionary (TickerQ's approach) vs standard Dictionary lookup performance.
/// This demonstrates why source-generated + FrozenDictionary is faster than reflection-based lookup.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class FunctionLookupBenchmarks
{
    private FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> _frozenDict = null!;
    private Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> _regularDict = null!;

    private string _existingKey = null!;
    private string _missingKey = "NonExistentFunction";

    [Params(10, 50, 200)]
    public int FunctionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        TickerFunctionDelegate noopDelegate = (_, _, _) => Task.CompletedTask;

        var dict = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>();
        for (int i = 0; i < FunctionCount; i++)
        {
            dict[$"MyApp.Jobs.Function_{i}"] = ($"*/5 * * * * *", TickerTaskPriority.Normal, noopDelegate, 0);
        }

        _regularDict = dict;
        _frozenDict = dict.ToFrozenDictionary();
        _existingKey = $"MyApp.Jobs.Function_{FunctionCount / 2}";
    }

    [Benchmark(Baseline = true, Description = "Dictionary: TryGetValue (hit)")]
    public bool Dictionary_Lookup_Hit() =>
        _regularDict.TryGetValue(_existingKey, out _);

    [Benchmark(Description = "FrozenDictionary: TryGetValue (hit)")]
    public bool FrozenDictionary_Lookup_Hit() =>
        _frozenDict.TryGetValue(_existingKey, out _);

    [Benchmark(Description = "Dictionary: TryGetValue (miss)")]
    public bool Dictionary_Lookup_Miss() =>
        _regularDict.TryGetValue(_missingKey, out _);

    [Benchmark(Description = "FrozenDictionary: TryGetValue (miss)")]
    public bool FrozenDictionary_Lookup_Miss() =>
        _frozenDict.TryGetValue(_missingKey, out _);

    [Benchmark(Description = "Dictionary: ContainsKey")]
    public bool Dictionary_ContainsKey() =>
        _regularDict.ContainsKey(_existingKey);

    [Benchmark(Description = "FrozenDictionary: ContainsKey")]
    public bool FrozenDictionary_ContainsKey() =>
        _frozenDict.ContainsKey(_existingKey);
}
