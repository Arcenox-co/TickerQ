using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ;

internal partial class TickerExecutionTaskHandler
{
    private sealed class ChainStep(InternalFunctionContext context, ChainStep parent = null)
    {
        public readonly InternalFunctionContext Context = context;
        public readonly ChainStep Parent = parent;
        public int PendingChildren;
        public bool Admitted;
        public bool OwnWorkFinished;
        public bool DeferredStarted;
        public bool ParallelGroupFinalized;
        public bool HasOutcome;
        public TickerStatus Outcome;
        public Exception Error;

        public void Observe(TickerStatus status)
        {
            Outcome = HasOutcome ? TickerChainInfrastructure.Combine(Outcome, status) : status;
            HasOutcome = true;
        }
    }

    private readonly record struct ChainStepEvent(ChainStep Step, bool Admission, Exception Error = null);

    // Only live step tasks own async state, cancellation registrations and retry timers. Waiting
    // branches stay as graph references. Never hold an execution permit while awaiting descendants.
    private async Task ExecuteTreeAsync(InternalFunctionContext context, bool isDue,
        CancellationTokenSource registeredSource, CancellationToken cancellationToken, bool isChild,
        ExecutionMode mode = ExecutionMode.Scheduler)
    {
        context.ChainRootId ??= context.TickerId;
        if (context.Type == TickerType.CronTickerOccurrence || context.TimeTickerChildren.Count == 0)
        {
            await RunChainStepAsync(context, isDue, registeredSource, cancellationToken, isChild, null, mode)
                .ConfigureAwait(false);
            return;
        }

        // Descendants keep running after the root's own terminal write, so that write keeps the
        // root's lease as the chain's ownership record until the whole tree has finished.
        var retainsChainLease = mode == ExecutionMode.Scheduler && !isChild && context.ParentId == null;
        context.RetainsChainLease = retainsChainLease;
        try
        {
            await ExecuteChainAsync(context, isDue, registeredSource, cancellationToken, mode).ConfigureAwait(false);
        }
        finally
        {
            if (retainsChainLease)
                await ReleaseChainLeaseAsync(context).ConfigureAwait(false);
        }
    }

    private async Task ReleaseChainLeaseAsync(InternalFunctionContext root)
    {
        try
        {
            await _internalTickerManager.ReleaseChainLeaseAsync(root, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Not fatal: the retained lease expires and stale recovery ends it, finding nothing
            // left running under the generation.
            _logger.LogWarning(ex, "TickerQ could not release the chain lease of {TickerType} {TickerId}",
                root.Type, root.TickerId);
        }
    }

    private async Task ExecuteChainAsync(InternalFunctionContext context, bool isDue,
        CancellationTokenSource registeredSource, CancellationToken cancellationToken, ExecutionMode mode)
    {
        var root = new ChainStep(context);
        var ready = new Queue<ChainStep>();
        var advancing = new Queue<ChainStep>();
        var events = Channel.CreateUnbounded<ChainStepEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        var running = 0;
        var finished = false;
        ready.Enqueue(root);

        void AddChild(ChainStep parent, InternalFunctionContext child)
        {
            child.ChainRootId = context.ChainRootId;
            child.AcquisitionToken = context.AcquisitionToken;
            child.ParentResultEnvelope = TickerChainInfrastructure.IsParallelGroup(parent.Context.FunctionName)
                ? parent.Context.ParentResultEnvelope
                : parent.Context.ResultEnvelope;
            parent.PendingChildren++;
            ready.Enqueue(new ChainStep(child, parent));
        }

        async Task RunAsync(ChainStep step)
        {
            Exception error = null;
            try
            {
                await RunChainStepAsync(step.Context, isDue,
                    step == root ? registeredSource : null, cancellationToken, step != root,
                    () => events.Writer.TryWrite(new ChainStepEvent(step, Admission: true)), mode)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                events.Writer.TryWrite(new ChainStepEvent(step, Admission: false, error));
            }
        }

        while (!finished)
        {
            while (ready.Count > 0 && running < _schedulerOptions.MaxConcurrentChainSteps)
            {
                var next = ready.Dequeue();
                running++;
                _ = RunAsync(next); // Both admission and completion are observed by this coordinator.
            }

            // There can be at most two unread events per live task. Creating another task requires
            // consuming its predecessor's completion, so this channel cannot accumulate with history.
            var change = await events.Reader.ReadAsync().ConfigureAwait(false);
            var current = change.Step;
            if (change.Admission)
            {
                current.Admitted = true;
                var isParallelGroup = TickerChainInfrastructure.IsParallelGroup(current.Context.FunctionName);
                if (isParallelGroup &&
                    current.Context.ParentId.HasValue)
                {
                    try
                    {
                        current.Context.ParentResultEnvelope = mode == ExecutionMode.TestChain
                            ? current.Parent?.Context.ResultEnvelope ?? current.Parent?.Context.ParentResultEnvelope
                            : await _internalTickerManager.GetParentResultAsync(
                                    current.Context.ParentId.Value,
                                    current.Context.Type,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        current.Error ??= exception;
                    }
                }
                if (current.Error == null && current.Context.Type != TickerType.CronTickerOccurrence)
                {
                    try
                    {
                        List<InternalFunctionContext> skipped = null;
                        foreach (var child in current.Context.TimeTickerChildren)
                        {
                            if (child.CachedDelegate == null)
                                continue;

                            var isParallelBranch = isParallelGroup
                                ? TickerChainInfrastructure.IsParallelBranchCondition(child.RunCondition)
                                : child.RunCondition == RunCondition.InProgress;
                            if (!isParallelBranch)
                                continue;

                            var condition = TickerChainInfrastructure.GetParallelBranchPredicate(child.RunCondition);
                            var parentStatus = current.Parent?.Context.ChainRetryPreviousStatus
                                ?? current.Parent?.Context.Status
                                ?? TickerStatus.InProgress;
                            if (condition == RunCondition.InProgress || ShouldRunCondition(condition, parentStatus))
                            {
                                AddChild(current, child);
                                continue;
                            }

                            _tickerQInstrumentation.LogJobSkipped(child.TickerId, child.FunctionName,
                                $"Grouped condition {condition} not met (Upstream status: {parentStatus})");
                            child.ParentId = current.Context.TickerId;
                            child.ChainRootId = context.ChainRootId;
                            child.ChainGeneration = current.Context.ChainGeneration;
                            (skipped ??= new()).Add(child);
                            GatherDescendantsToSkip(child, context.ChainRootId.Value,
                                current.Context.ChainGeneration, skipped);
                        }

                        if (skipped != null && mode == ExecutionMode.Scheduler)
                            await _internalTickerManager.UpdateSkipTimeTickersWithUnifiedContextAsync(
                                skipped.ToArray(), CancellationToken.None).ConfigureAwait(false);
                        else if (skipped != null)
                            MarkTestStepsSkipped(skipped, current.Context.Status,
                                "Grouped condition was not met.");
                    }
                    catch (Exception exception)
                    {
                        // The coordinator still owns any already-started group jobs. Record the
                        // admission failure and let the normal drain path release them safely.
                        current.Error ??= exception;
                    }
                }
            }
            else
            {
                running--;
                current.OwnWorkFinished = true;
                current.Error ??= change.Error;
                if (!TickerChainInfrastructure.IsParallelGroup(current.Context.FunctionName) &&
                    !current.Context.IsChainRetryPassThrough)
                    current.Observe(current.Context.Status);
                if (current.PendingChildren == 0)
                    advancing.Enqueue(current);
            }

            while (advancing.TryDequeue(out var step))
            {
                if (TickerChainInfrastructure.IsParallelGroup(step.Context.FunctionName) &&
                    !step.Context.IsChainRetryPassThrough &&
                    !step.ParallelGroupFinalized && step.PendingChildren == 0 && step.Error == null)
                {
                    step.ParallelGroupFinalized = true;
                    // If no grouped condition matched, preserve the upstream outcome. This lets
                    // failure/cancellation final jobs run without pretending an empty group succeeded.
                    var outcome = step.HasOutcome
                        ? step.Outcome
                        : step.Parent?.Context.ChainRetryPreviousStatus
                          ?? step.Parent?.Context.Status
                          ?? TickerStatus.Done;
                    // Keep the group's aggregate outcome in the coordinator's propagated result.
                    // A successful failure-handler is downstream work; it must not erase the
                    // failed/cancelled/skipped branch status seen by the enclosing chain retry.
                    step.Observe(outcome);
                    step.Context.SetStatus(outcome)
                        .SetExecutedAt(_clock.UtcNow)
                        .SetElapsedTime(0L)
                        .SetResultEnvelope(null);
                    step.Context.SetExceptionDetails(outcome switch
                    {
                        TickerStatus.Failed => "One or more parallel branches failed.",
                        TickerStatus.Cancelled => "One or more parallel branches were cancelled.",
                        TickerStatus.Skipped => "One or more parallel branches were skipped.",
                        _ => null
                    });
                    try
                    {
                        if (mode == ExecutionMode.Scheduler)
                            await _internalTickerManager.UpdateTickerAsync(step.Context, CancellationToken.None)
                                .ConfigureAwait(false);
                        else
                            _lifecycle?.Committed(step.Context);
                    }
                    catch (Exception exception)
                    {
                        step.Error ??= exception;
                    }
                }

                // Deferred steps wait for both their parent and all grouped subtrees.
                if (!step.DeferredStarted && step.Admitted && step.Error == null &&
                    step.Context.Type != TickerType.CronTickerOccurrence)
                {
                    step.DeferredStarted = true;
                    try
                    {
                        List<InternalFunctionContext> skipped = null;
                        foreach (var child in step.Context.TimeTickerChildren)
                        {
                            var isParallelBranch =
                                TickerChainInfrastructure.IsParallelGroup(step.Context.FunctionName) &&
                                TickerChainInfrastructure.IsParallelBranchCondition(child.RunCondition);
                            if (child.CachedDelegate == null || child.RunCondition == RunCondition.InProgress ||
                                isParallelBranch)
                                continue;
                            if (ShouldRunChild(child, step.Context.Status))
                                AddChild(step, child);
                            else
                            {
                                _tickerQInstrumentation.LogJobSkipped(child.TickerId, child.FunctionName,
                                    $"Condition {child.RunCondition} not met (Parent status: {step.Context.Status})");
                                child.ParentId = step.Context.TickerId;
                                child.ChainRootId = context.ChainRootId;
                                child.ChainGeneration = step.Context.ChainGeneration;
                                (skipped ??= new()).Add(child);
                                GatherDescendantsToSkip(child, context.ChainRootId.Value,
                                    step.Context.ChainGeneration, skipped);
                            }
                        }
                        if (skipped != null && mode == ExecutionMode.Scheduler)
                            await _internalTickerManager.UpdateSkipTimeTickersWithUnifiedContextAsync(
                                skipped.ToArray(), CancellationToken.None).ConfigureAwait(false);
                        else if (skipped != null)
                            MarkTestStepsSkipped(skipped, step.Context.Status,
                                "Condition was not met.");
                    }
                    catch (Exception exception)
                    {
                        // Instrumentation and traversal can fail too. Preserve the old
                        // WhenAll ownership: drain already accepted branches before the
                        // root propagates an error and releases its acquired resources.
                        step.Error ??= exception;
                    }
                }
                if (step.PendingChildren != 0)
                    continue;
                if (step.Parent is { } parent)
                {
                    parent.Error ??= step.Error;
                    if (step.HasOutcome)
                        parent.Observe(step.Outcome);
                    if (--parent.PendingChildren == 0 && parent.OwnWorkFinished)
                        advancing.Enqueue(parent);
                }
                else
                {
                    if (step.Context.IsChainRetryCoordinator && step.Error == null)
                    {
                        var outcome = step.HasOutcome ? step.Outcome : TickerStatus.Failed;
                        step.Context.SetStatus(outcome)
                            .SetExecutedAt(_clock.UtcNow)
                            .SetElapsedTime(0L)
                            .SetExceptionDetails(outcome switch
                            {
                                TickerStatus.Failed => "The retried chain path failed.",
                                TickerStatus.Cancelled => "The retried chain path was cancelled.",
                                TickerStatus.Skipped => "The retried chain path was skipped.",
                                _ => null
                            });
                        if (mode == ExecutionMode.Scheduler)
                            await _internalTickerManager.UpdateTickerAsync(
                                step.Context, CancellationToken.None).ConfigureAwait(false);
                        else
                            _lifecycle?.Committed(step.Context);
                    }
                    finished = true;
                }
            }
        }

        if (root.Error != null)
            ExceptionDispatchInfo.Capture(root.Error).Throw();
    }

    private async Task RunChainStepAsync(InternalFunctionContext context, bool isDue,
        CancellationTokenSource registeredSource, CancellationToken cancellationToken,
        bool isChild, Action admitted, ExecutionMode mode)
    {
        if (context.IsChainRetryPassThrough || context.IsChainRetryReusedTerminal)
        {
            admitted?.Invoke();
            return;
        }
        if (isChild)
        {
            if (!context.ChainGeneration.HasValue)
                return;
            // Fence admission before releasing descendants: the snapshot may contain cancelled jobs.
            context.SetStatus(TickerStatus.InProgress);
            try
            {
                if (mode == ExecutionMode.Scheduler)
                    await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TickerResultNotAcknowledgedException)
            {
                return;
            }
        }
        admitted?.Invoke();

        // A parallel group is a persisted orchestration node, not user code. Its InProgress
        // branch children are released by the coordinator above; once they settle, the
        // coordinator persists the group's aggregate terminal status and releases joins.
        if (TickerChainInfrastructure.IsParallelGroup(context.FunctionName))
        {
            if (mode == ExecutionMode.TestChain)
                _lifecycle?.Started(context, 0);
            return;
        }

        if (mode == ExecutionMode.Scheduler && context.Type == TickerType.CronTickerOccurrence &&
            await _internalTickerManager.TryDispatchCronChainAsync(context, isDue, cancellationToken).ConfigureAwait(false))
        {
            _serviceProvider.GetService<ITickerQHostScheduler>()?.Restart();
            return;
        }
        await RunContextFunctionAsync(context, isDue, cancellationToken, isChild,
            preRegisteredSource: registeredSource, mode: mode).ConfigureAwait(false);
        if (mode == ExecutionMode.TestChain)
            _lifecycle?.Committed(context);
    }

    private void MarkTestStepsSkipped(IEnumerable<InternalFunctionContext> steps,
        TickerStatus parentStatus, string prefix)
    {
        foreach (var step in steps)
        {
            var reason = $"{prefix} Required {step.RunCondition}; upstream status was {parentStatus}.";
            step.SetStatus(TickerStatus.Skipped)
                .SetExecutedAt(_clock.UtcNow)
                .SetElapsedTime(0L)
                .SetExceptionDetails(reason)
                .SetResultEnvelope(null);
            _lifecycle?.Committed(step);
        }
    }
}
