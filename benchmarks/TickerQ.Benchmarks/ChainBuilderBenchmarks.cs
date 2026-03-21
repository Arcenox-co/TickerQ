using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Managers;

namespace TickerQ.Benchmarks;

/// <summary>
/// Benchmarks for FluentChainTickerBuilder — building job chains with parent/child/grandchild relationships.
/// Measures allocation and speed of constructing complex job DAGs.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class ChainBuilderBenchmarks
{
    [Benchmark(Description = "Build: Single job (no chain)")]
    public TimeTickerEntity Build_SingleJob() =>
        FluentChainTickerBuilder<TimeTickerEntity>.BeginWith(p => p
            .SetFunction("SendEmail")
            .SetExecutionTime(DateTime.UtcNow.AddMinutes(5))
            .SetRequest(new { To = "user@example.com", Subject = "Hello" })
        ).Build();

    [Benchmark(Description = "Build: Parent + 2 children")]
    public TimeTickerEntity Build_ParentWith2Children() =>
        FluentChainTickerBuilder<TimeTickerEntity>.BeginWith(p => p
            .SetFunction("ProcessOrder")
            .SetExecutionTime(DateTime.UtcNow.AddMinutes(1))
            .SetRequest(new { OrderId = 123 })
        )
        .WithFirstChild(c => c
            .SetFunction("SendConfirmation")
            .SetRunCondition(RunCondition.OnSuccess)
            .SetRequest(new { OrderId = 123, Email = "user@test.com" })
        )
        .WithSecondChild(c => c
            .SetFunction("NotifyAdmin")
            .SetRunCondition(RunCondition.OnFailure)
            .SetRequest(new { OrderId = 123, Reason = "Processing failed" })
        )
        .Build();

    [Benchmark(Description = "Build: Parent + 5 children (max width)")]
    public TimeTickerEntity Build_ParentWith5Children() =>
        FluentChainTickerBuilder<TimeTickerEntity>.BeginWith(p => p
            .SetFunction("BatchProcess")
            .SetExecutionTime(DateTime.UtcNow)
        )
        .WithFirstChild(c => c.SetFunction("Step1").SetRunCondition(RunCondition.OnSuccess))
        .WithSecondChild(c => c.SetFunction("Step2").SetRunCondition(RunCondition.OnSuccess))
        .WithThirdChild(c => c.SetFunction("Step3").SetRunCondition(RunCondition.OnSuccess))
        .WithFourthChild(c => c.SetFunction("Cleanup").SetRunCondition(RunCondition.OnAnyCompletedStatus))
        .WithFifthChild(c => c.SetFunction("Alert").SetRunCondition(RunCondition.OnFailure))
        .Build();

    [Benchmark(Description = "Build: 3-level deep chain (parent → child → grandchild)")]
    public TimeTickerEntity Build_ThreeLevelChain() =>
        FluentChainTickerBuilder<TimeTickerEntity>.BeginWith(p => p
            .SetFunction("IngestData")
            .SetExecutionTime(DateTime.UtcNow)
            .SetRetries(3, 1000, 5000, 30000)
        )
        .WithFirstChild(c => c
            .SetFunction("TransformData")
            .SetRunCondition(RunCondition.OnSuccess)
        )
            .WithFirstGrandChild(gc => gc
                .SetFunction("LoadToWarehouse")
                .SetRunCondition(RunCondition.OnSuccess)
            )
            .WithSecondGrandChild(gc => gc
                .SetFunction("NotifyDataTeam")
                .SetRunCondition(RunCondition.OnAnyCompletedStatus)
            )
        .WithSecondChild(c => c
            .SetFunction("LogFailure")
            .SetRunCondition(RunCondition.OnFailure)
        )
        .Build();
}
