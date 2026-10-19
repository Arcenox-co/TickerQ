using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
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
        public Exception Error;
    }

    private readonly record struct ChainStepEvent(ChainStep Step, bool Admission, Exception Error = null);

    // Only live step tasks own async state, cancellation registrations and retry timers. Waiting
    // branches stay as graph references. Never hold an execution permit while awaiting descendants.
    private async Task ExecuteTreeAsync(InternalFunctionContext context, bool isDue,
        CancellationTokenSource registeredSource, CancellationToken cancellationToken, bool isChild)
    {
        context.ChainRootId ??= context.TickerId;
        if (context.Type == TickerType.CronTickerOccurrence || context.TimeTickerChildren.Count == 0)
        {
            await RunChainStepAsync(context, isDue, registeredSource, cancellationToken, isChild, null)
                .ConfigureAwait(false);
            return;
        }

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
                    () => events.Writer.TryWrite(new ChainStepEvent(step, Admission: true)))
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
                if (current.Context.Type != TickerType.CronTickerOccurrence)
                    foreach (var child in current.Context.TimeTickerChildren)
                        if (child.CachedDelegate != null && child.RunCondition == RunCondition.InProgress)
                            AddChild(current, child);
            }
            else
            {
                running--;
                current.OwnWorkFinished = true;
                current.Error ??= change.Error;
                if (current.PendingChildren == 0)
                    advancing.Enqueue(current);
            }

            while (advancing.TryDequeue(out var step))
            {
                // As before, deferred steps wait for both their parent and its InProgress subtrees.
                if (!step.DeferredStarted && step.Admitted && step.Error == null &&
                    step.Context.Type != TickerType.CronTickerOccurrence)
                {
                    step.DeferredStarted = true;
                    try
                    {
                        List<InternalFunctionContext> skipped = null;
                        foreach (var child in step.Context.TimeTickerChildren)
                        {
                            if (child.CachedDelegate == null || child.RunCondition == RunCondition.InProgress)
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
                        if (skipped != null)
                            await _internalTickerManager.UpdateSkipTimeTickersWithUnifiedContextAsync(
                                skipped.ToArray(), cancellationToken).ConfigureAwait(false);
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
                    if (--parent.PendingChildren == 0 && parent.OwnWorkFinished)
                        advancing.Enqueue(parent);
                }
                else
                    finished = true;
            }
        }

        if (root.Error != null)
            ExceptionDispatchInfo.Capture(root.Error).Throw();
    }

    private async Task RunChainStepAsync(InternalFunctionContext context, bool isDue,
        CancellationTokenSource registeredSource, CancellationToken cancellationToken,
        bool isChild, Action admitted)
    {
        if (isChild)
        {
            if (!context.ChainGeneration.HasValue)
                return;
            // Fence admission before releasing descendants: the snapshot may contain cancelled jobs.
            context.SetProperty(x => x.Status, TickerStatus.InProgress);
            try
            {
                await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TickerResultNotAcknowledgedException)
            {
                return;
            }
        }
        admitted?.Invoke();

        if (context.Type == TickerType.CronTickerOccurrence &&
            await _internalTickerManager.TryDispatchCronChainAsync(context, isDue, cancellationToken).ConfigureAwait(false))
        {
            _serviceProvider.GetService<ITickerQHostScheduler>()?.Restart();
            return;
        }
        await RunContextFunctionAsync(context, isDue, cancellationToken, isChild,
            preRegisteredSource: registeredSource).ConfigureAwait(false);
    }
}
