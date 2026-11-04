using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Managers;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
/// <summary>
/// A single time ticker with no children is validated without the chain traversal. That fast path must
/// accept, reject and stamp exactly as the graph validator does for the same job.
/// </summary>
public sealed class StandaloneTimeTickerValidationTests : IDisposable
{
    public StandaloneTimeTickerValidationTests()
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                ["Standalone"] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
        TickerFunctionProvider.Build();
    }

    public void Dispose() => TickerFunctionProvider.Build();

    public static TheoryData<string> Cases =>
    [
        "valid",
        "unknown-function",
        "payload-not-allowed",
        "unknown-delete-behavior",
        "unknown-missed-execution",
        "skip-without-lateness",
        "negative-lateness",
        "parent-with-delete-behavior",
        "parallel-group-root",
        "null-children"
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Standalone_fast_path_matches_the_graph_validator(string shape)
    {
        var fast = Create(shape);
        var graph = Create(shape);

        var fastError = TickerManager<TimeTickerEntity, CronTickerEntity>.ValidateAndStampTimeTicker(fast);
        var graphError = TickerManager<TimeTickerEntity, CronTickerEntity>.ValidateAndStampTimeTickerGraph([graph]);

        Assert.Equal(graphError?.Message, fastError?.Message);
        Assert.Equal(graph.RequestContractVersion, fast.RequestContractVersion);
        Assert.Equal(graph.RequestContractFingerprint, fast.RequestContractFingerprint);
        if (shape == "valid")
        {
            Assert.Null(fastError);
            Assert.NotNull(fast.RequestContractVersion);
        }
        else
        {
            Assert.NotNull(fastError);
        }
    }

    private static TimeTickerEntity Create(string shape)
    {
        var ticker = new TimeTickerEntity { Function = "Standalone", ExecutionTime = DateTime.UtcNow.AddHours(1) };
        switch (shape)
        {
            case "unknown-function": ticker.Function = "NotRegistered"; break;
            case "payload-not-allowed": ticker.Request = [1, 2, 3]; break;
            case "unknown-delete-behavior": ticker.DeleteBehavior = (ChainDeleteBehavior)99; break;
            case "unknown-missed-execution": ticker.MissedExecutionAction = (MissedExecutionAction)99; break;
            case "skip-without-lateness": ticker.MissedExecutionAction = MissedExecutionAction.Skip; break;
            case "negative-lateness": ticker.MaximumLateness = TimeSpan.FromSeconds(-1); break;
            case "parent-with-delete-behavior":
                ticker.ParentId = Guid.NewGuid();
                ticker.DeleteBehavior = Enum.GetValues<ChainDeleteBehavior>().First(x => x != ChainDeleteBehavior.Cascade);
                break;
            case "parallel-group-root": ticker.Function = TickerChainInfrastructure.ParallelGroupFunction; break;
            case "null-children": ticker.Children = null; break;
        }
        return ticker;
    }
}
