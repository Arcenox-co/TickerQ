using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>
/// Reserved durable nodes used by the chain runtime. They are stored through the normal
/// provider model so every built-in provider gets identical behavior without extra schema.
/// </summary>
internal static class TickerChainInfrastructure
{
    internal const string ParallelGroupFunction = "$tickerq.parallel-group";
    internal const string ParallelGroupDescription = "Parallel group";

    internal static bool IsParallelGroup(string functionName) =>
        string.Equals(functionName, ParallelGroupFunction, StringComparison.Ordinal);

    internal static readonly TickerFunctionDelegate ParallelGroupDelegate =
        static (_, _, _) => Task.CompletedTask;

    internal static bool IsParallelBranchCondition(RunCondition condition) =>
        condition is RunCondition.InProgress or
            RunCondition.GroupOnSuccess or
            RunCondition.GroupOnFailure or
            RunCondition.GroupOnCancelled or
            RunCondition.GroupOnFailureOrCancelled or
            RunCondition.GroupOnAnyCompletedStatus or
            RunCondition.GroupOnAnyTerminalStatus;

    internal static RunCondition ToParallelBranchCondition(RunCondition condition) => condition switch
    {
        RunCondition.InProgress => RunCondition.InProgress,
        RunCondition.OnSuccess or RunCondition.GroupOnSuccess => RunCondition.GroupOnSuccess,
        RunCondition.OnFailure or RunCondition.GroupOnFailure => RunCondition.GroupOnFailure,
        RunCondition.OnCancelled or RunCondition.GroupOnCancelled => RunCondition.GroupOnCancelled,
        RunCondition.OnFailureOrCancelled or RunCondition.GroupOnFailureOrCancelled =>
            RunCondition.GroupOnFailureOrCancelled,
        RunCondition.OnAnyCompletedStatus or RunCondition.GroupOnAnyCompletedStatus =>
            RunCondition.GroupOnAnyCompletedStatus,
        RunCondition.OnAnyTerminalStatus or RunCondition.GroupOnAnyTerminalStatus =>
            RunCondition.GroupOnAnyTerminalStatus,
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unsupported group condition.")
    };

    internal static RunCondition GetParallelBranchPredicate(RunCondition condition) => condition switch
    {
        RunCondition.GroupOnSuccess => RunCondition.OnSuccess,
        RunCondition.GroupOnFailure => RunCondition.OnFailure,
        RunCondition.GroupOnCancelled => RunCondition.OnCancelled,
        RunCondition.GroupOnFailureOrCancelled => RunCondition.OnFailureOrCancelled,
        RunCondition.GroupOnAnyCompletedStatus => RunCondition.OnAnyCompletedStatus,
        RunCondition.GroupOnAnyTerminalStatus => RunCondition.OnAnyTerminalStatus,
        _ => condition
    };

    internal static TickerStatus Combine(TickerStatus current, TickerStatus next)
    {
        if (current == TickerStatus.Failed || next == TickerStatus.Failed)
            return TickerStatus.Failed;
        if (current == TickerStatus.Cancelled || next == TickerStatus.Cancelled)
            return TickerStatus.Cancelled;
        if (current == TickerStatus.Skipped || next == TickerStatus.Skipped)
            return TickerStatus.Skipped;
        if (current == TickerStatus.DueDone || next == TickerStatus.DueDone)
            return TickerStatus.DueDone;
        return TickerStatus.Done;
    }
}
