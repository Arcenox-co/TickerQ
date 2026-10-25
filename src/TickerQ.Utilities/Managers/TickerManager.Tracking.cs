using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers;

// Reading outcomes, waiting for completion, rescheduling, bulk cancellation, and cron run-time previews.
// Everything goes through the persistence provider, so it behaves the same on every store and replica.
internal partial class TickerManager<TTimeTicker, TCronTicker>
{
    private static readonly TimeSpan FirstOutcomePoll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxOutcomePoll = TimeSpan.FromSeconds(1);
    private const int MaxListedOccurrences = 1000;

    async Task<TickerOutcome> ITimeTickerManager<TTimeTicker>.GetOutcomeAsync(Guid id, CancellationToken cancellationToken)
    {
        var ticker = await _persistenceProvider.GetTimeTickerById(id, cancellationToken).ConfigureAwait(false);
        if (ticker is null)
            return null;
        // The result commits atomically with the successful status, so it is only read once that is stored.
        var result = ticker.Status is TickerStatus.Done or TickerStatus.DueDone
            ? await _persistenceProvider.GetTimeTickerResultAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
        return new TickerOutcome(ticker.Id, ticker.Status, ticker.ExecutedAt, ticker.ExceptionMessage,
            ticker.SkippedReason, result);
    }

    async Task<TickerOutcome> ICronTickerManager<TCronTicker>.GetOutcomeAsync(Guid occurrenceId, CancellationToken cancellationToken)
    {
        var occurrence = (await _persistenceProvider
                .GetAllCronTickerOccurrences(x => x.Id == occurrenceId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault();
        if (occurrence is null)
            return null;
        var result = occurrence.Status is TickerStatus.Done or TickerStatus.DueDone
            ? await _persistenceProvider.GetCronTickerOccurrenceResultAsync(occurrenceId, cancellationToken).ConfigureAwait(false)
            : null;
        return new TickerOutcome(occurrence.Id, occurrence.Status, occurrence.ExecutedAt, occurrence.ExceptionMessage,
            occurrence.SkippedReason, result);
    }

    Task<TickerOutcome> ITimeTickerManager<TTimeTicker>.WaitForCompletionAsync(Guid id, TimeSpan timeout,
        CancellationToken cancellationToken)
        => WaitForOutcomeAsync(ct => ((ITimeTickerManager<TTimeTicker>)this).GetOutcomeAsync(id, ct), timeout, cancellationToken);

    Task<TickerOutcome> ICronTickerManager<TCronTicker>.WaitForCompletionAsync(Guid occurrenceId, TimeSpan timeout,
        CancellationToken cancellationToken)
        => WaitForOutcomeAsync(ct => ((ICronTickerManager<TCronTicker>)this).GetOutcomeAsync(occurrenceId, ct), timeout,
            cancellationToken);

    // Polls with a growing delay. Completion can be written by any replica, so a local signal would not be
    // enough on its own; the store is the source of truth.
    private static async Task<TickerOutcome> WaitForOutcomeAsync(Func<CancellationToken, Task<TickerOutcome>> read,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must not be negative.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
            deadline.CancelAfter(timeout);
        var delay = FirstOutcomePoll;
        while (true)
        {
            var outcome = await read(cancellationToken).ConfigureAwait(false);
            if (outcome is null || outcome.IsCompleted)
                return outcome;
            try
            {
                await Task.Delay(delay, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out: report the latest stored state rather than the one read before the wait.
                return await read(cancellationToken).ConfigureAwait(false);
            }
            delay = delay * 2 < MaxOutcomePoll ? delay * 2 : MaxOutcomePoll;
        }
    }

    async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.RescheduleAsync(Guid id, DateTime executionTime,
        CancellationToken cancellationToken)
    {
        TTimeTicker ticker;
        try
        {
            ticker = await _persistenceProvider.GetTimeTickerById(id, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new TickerResult<TTimeTicker>(exception); }

        if (ticker is null)
            return new TickerResult<TTimeTicker>(new TickerValidatorException("The time ticker was not found."));
        if (ticker.ParentId.HasValue)
            return new TickerResult<TTimeTicker>(new TickerValidatorException(
                "Chain steps run after their parent. Reschedule the chain root instead."));
        if (ticker.Status != TickerStatus.Idle)
            return new TickerResult<TTimeTicker>(new TickerValidatorException(
                $"The time ticker is {ticker.Status} and can no longer be rescheduled."));

        // The update only commits if the stored execution state still matches what was read here, so a
        // ticker that starts in the meantime is reported as changed instead of being moved.
        ticker.ExecutionTime = executionTime;
        return await UpdateTimeTickerAsync(ticker, cancellationToken).ConfigureAwait(false);
    }

    async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.CancelPendingAsync<TFunction>(
        CancellationToken cancellationToken)
    {
        await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
        var function = TickerFunctionProvider.GetFunctionName<TFunction>();
        var cancelled = new List<Guid>();
        var affected = 0;
        Exception failure = null;
        try
        {
            // Providers return chain roots (with their steps) from GetTimeTickers, so this finds standalone
            // jobs and chain roots of the function; cancelling a root also cancels its pending steps.
            var pending = await _persistenceProvider.GetTimeTickers(
                x => x.Function == function && (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued),
                cancellationToken).ConfigureAwait(false);
            foreach (var id in pending.Select(x => x.Id))
            {
                // Zero means it started or was cancelled after it was read.
                var changed = await _persistenceProvider.CancelPendingTimeTickerAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                if (changed <= 0) continue;
                affected += changed;
                cancelled.Add(id);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { failure = exception; }

        if (cancelled.Count > 0)
        {
            // Persistence already fenced execution; refresh hints must not turn committed work into a failure.
            try { _tickerQHostScheduler.Restart(); } catch { }
            foreach (var id in cancelled)
                try { await _notificationHubSender.UpdateTimeTickerNotifyAsync(id).ConfigureAwait(false); } catch { }
        }
        return failure is null ? new TickerResult<TTimeTicker>(affected) : new TickerResult<TTimeTicker>(failure);
    }

    async Task<TickerResult<IReadOnlyList<DateTime>>> ICronTickerManager<TCronTicker>.GetNextOccurrencesAsync(
        Guid cronTickerId, int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > MaxListedOccurrences)
            return new TickerResult<IReadOnlyList<DateTime>>(new ArgumentOutOfRangeException(nameof(count), count,
                $"Count must be between 1 and {MaxListedOccurrences}."));
        try
        {
            var cron = await _persistenceProvider.GetCronTickerById(cronTickerId, cancellationToken).ConfigureAwait(false);
            if (cron is null)
                return new TickerResult<IReadOnlyList<DateTime>>(new TickerValidatorException("The cron ticker was not found."));

            var times = new List<DateTime>(count);
            if (!cron.IsEnabled || cron.IsSystemPaused)
                return new TickerResult<IReadOnlyList<DateTime>>(times);

            Func<DateTime, DateTime?> next;
            if (string.IsNullOrWhiteSpace(cron.SchedulePolicyName))
            {
                next = after => CronScheduleCache.GetNextOccurrenceOrDefault(cron.Expression, after, cron.TimeZoneId);
            }
            else
            {
                if (_schedulePolicyStore == null)
                    return new TickerResult<IReadOnlyList<DateTime>>(new TickerValidatorException(
                        "The configured persistence provider does not support schedule policies."));
                // Read the policies once; every candidate is then evaluated against the same snapshot.
                var policies = await _schedulePolicyStore.ListSchedulePoliciesAsync(cancellationToken).ConfigureAwait(false);
                next = after => _schedulePolicyEvaluator.GetNextAllowedCronOccurrence(
                    cron.Expression, after, cron.TimeZoneId, cron.SchedulePolicyName, policies);
            }

            var cursor = _clock.UtcNow;
            while (times.Count < count && next(cursor) is { } occurrence && occurrence > cursor)
            {
                times.Add(occurrence);
                cursor = occurrence;
            }
            return new TickerResult<IReadOnlyList<DateTime>>(times);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new TickerResult<IReadOnlyList<DateTime>>(exception); }
    }
}
