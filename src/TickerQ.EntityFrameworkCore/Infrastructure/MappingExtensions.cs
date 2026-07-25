using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal static class MappingExtensions
    {
        public static Expression<Func<TCronTicker, CronTickerEntity>> ForCronTickerExpressions<TCronTicker>()
            where TCronTicker : CronTickerEntity, new()
            => e => new CronTickerEntity
            {
                Id = e.Id,
                Expression = e.Expression,
                Function = e.Function,
                RetryIntervals = e.RetryIntervals,
                Retries = e.Retries,
                TimeoutSeconds = e.TimeoutSeconds,
                IsEnabled = e.IsEnabled
            };

        internal static Expression<Func<TTimeTicker, TimeTickerEntity>> ForQueueTimeTickers<TTimeTicker>()
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            => e => new TimeTickerEntity
            {
                Id = e.Id,
                Function = e.Function,
                Retries = e.Retries,
                RetryIntervals = e.RetryIntervals,
                TimeoutSeconds = e.TimeoutSeconds,
                UpdatedAt = e.UpdatedAt,
                ParentId = e.ParentId,
                ExecutionTime = e.ExecutionTime,
                Status = e.Status,
                LockHolder = e.LockHolder,
                LockedAt = e.LockedAt,
                LeaseUntil = e.LeaseUntil,
                AcquisitionToken = e.AcquisitionToken,
                RetryCount = e.RetryCount,
                ExceptionMessage = e.ExceptionMessage,
                SkippedReason = e.SkippedReason,
                StaleRestartCount = e.StaleRestartCount,
                ExecutedAt = e.ExecutedAt,
                ElapsedTime = e.ElapsedTime,
                Children = e.Children.Select(ch => new TimeTickerEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    TimeoutSeconds = ch.TimeoutSeconds,
                    ParentId = ch.ParentId,
                    RunCondition = ch.RunCondition,
                    Children = ch.Children.Select(gch => new TimeTickerEntity
                    {
                        Function = gch.Function,
                        Retries = gch.Retries,
                        RetryIntervals = gch.RetryIntervals,
                        TimeoutSeconds = gch.TimeoutSeconds,
                        ParentId = gch.ParentId,
                        Id = gch.Id,
                        RunCondition = gch.RunCondition
                    }).ToArray()
                }).ToArray()
            };

        internal static Expression<Func<TCronTickerOccurrence, CronTickerOccurrenceEntity<TCronTicker>>>
            ForQueueCronTickerOccurrence<TCronTickerOccurrence, TCronTicker>()
            where TCronTicker : CronTickerEntity, new()
            where TCronTickerOccurrence : CronTickerOccurrenceEntity<TCronTicker>, new()
            => e => new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = e.Id,
                UpdatedAt = e.UpdatedAt,
                CronTickerId = e.CronTickerId,
                ExecutionTime = e.ExecutionTime,
                AcquisitionToken = e.AcquisitionToken,
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries,
                    TimeoutSeconds = e.CronTicker.TimeoutSeconds
                }
            };

        internal static Expression<Func<TCronTickerOccurrence, CronTickerOccurrenceEntity<TCronTicker>>>
            ForLatestQueuedCronTickerOccurrence<TCronTickerOccurrence, TCronTicker>()
            where TCronTicker : CronTickerEntity, new()
            where TCronTickerOccurrence : CronTickerOccurrenceEntity<TCronTicker>, new()
            => e => new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = e.Id,
                CreatedAt = e.CreatedAt,
                CronTickerId = e.CronTickerId,
                ExecutionTime = e.ExecutionTime,
                AcquisitionToken = e.AcquisitionToken,
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    Expression = e.CronTicker.Expression,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries,
                    TimeoutSeconds = e.CronTicker.TimeoutSeconds
                }
            };

        internal static void UpdateCronTickerOccurrence<TCronTicker>(
            this UpdateSettersBuilder<CronTickerOccurrenceEntity<TCronTicker>> setters,
            InternalFunctionContext functionContext, DateTime? leaseUntil = null)
            where TCronTicker : CronTickerEntity, new()
        {
            var propsToUpdate = functionContext.GetPropsToUpdate();

            // LEASE — stamp on the InProgress transition so a node dying before the
            // first renewal still leaves a detectable (expired) lease behind.
            if (leaseUntil != null &&
                propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status == TickerStatus.InProgress)
            {
                setters.SetProperty(x => x.LeaseUntil, leaseUntil);
            }

            // ACQUISITION TOKEN (generation) — mint a fresh generation on the InProgress
            // transition (the caller supplies it on the context), and clear it on any
            // terminal write or lock release so the next acquisition starts a new generation
            // and a stale owner's fenced write/renewal can no longer match.
            ApplyAcquisitionToken(propsToUpdate, functionContext,
                (setter, token) => setter.SetProperty(x => x.AcquisitionToken, token), setters);

            // STATUS / SKIPPED — touch status/skipped reason ONLY when Status is part of the
            // update, and write SkippedReason ONLY for the Skipped transition. A status-free
            // write (e.g. a retry-count bump) must leave Status and SkippedReason untouched
            // rather than resetting them to the context's defaults.
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
            {
                setters.SetProperty(x => x.Status, functionContext.Status);

                if (functionContext.Status == TickerStatus.Skipped)
                    setters.SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
            }

            // EXECUTED_AT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
            }

            // EXCEPTION DETAILS
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
            }

            // ELAPSED_TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
            }

            // RETRY COUNT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
            }

            // RELEASE LOCK
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                setters
                    .SetProperty(x => x.LockHolder, (string)null)
                    .SetProperty(x => x.LockedAt, (DateTime?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTime?)null);
            }

            // EXECUTION TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutionTime)))
            {
                setters.SetProperty(x => x.ExecutionTime, functionContext.ExecutionTime);
            }
        }

        /// <summary>
        /// True when the context writes a terminal status — the writes after which a row's
        /// acquisition generation must be cleared (it is no longer InProgress under any owner).
        /// </summary>
        internal static bool WritesTerminalStatus(InternalFunctionContext functionContext, System.Collections.Generic.HashSet<string> propsToUpdate)
            => propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
               functionContext.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                   or TickerStatus.Cancelled or TickerStatus.Skipped;

        /// <summary>
        /// Shared stamp/clear decision for the acquisition generation token, applied by both
        /// the time-ticker and cron-occurrence setters (and mirrored by the Mongo builders).
        /// </summary>
        private static void ApplyAcquisitionToken<TSetter>(
            System.Collections.Generic.HashSet<string> propsToUpdate,
            InternalFunctionContext functionContext,
            Action<TSetter, Guid?> apply,
            TSetter setter)
        {
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.AcquisitionToken)) &&
                propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status == TickerStatus.InProgress)
            {
                apply(setter, functionContext.AcquisitionToken);
            }
            else if (WritesTerminalStatus(functionContext, propsToUpdate) ||
                     propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                apply(setter, null);
            }
        }

        internal static void UpdateTimeTicker<TTimeTicker>(this UpdateSettersBuilder<TTimeTicker> setters,
            InternalFunctionContext functionContext, DateTime updatedAt, DateTime? leaseUntil = null)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            var propsToUpdate = functionContext.GetPropsToUpdate();

            // LEASE — stamp on the InProgress transition so a node dying before the
            // first renewal still leaves a detectable (expired) lease behind.
            if (leaseUntil != null &&
                propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status == TickerStatus.InProgress)
            {
                setters.SetProperty(x => x.LeaseUntil, leaseUntil);
            }

            // ACQUISITION TOKEN (generation) — stamp on the InProgress transition, clear on
            // any terminal write or lock release. See UpdateCronTickerOccurrence for rationale.
            ApplyAcquisitionToken(propsToUpdate, functionContext,
                (setter, token) => setter.SetProperty(x => x.AcquisitionToken, token), setters);

            // STATUS / SKIPPED — touch status/skipped reason ONLY when Status is part of the
            // update, and write SkippedReason ONLY for the Skipped transition. A status-free
            // write (e.g. a retry-count bump) must leave Status and SkippedReason untouched
            // rather than resetting them to the context's defaults.
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
            {
                setters.SetProperty(x => x.Status, functionContext.Status);

                if (functionContext.Status == TickerStatus.Skipped)
                    setters.SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
            }

            // EXECUTED_AT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
            }

            // EXCEPTION DETAILS
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
            }

            // ELAPSED_TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
            }

            // RETRY COUNT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
            }

            // RELEASE LOCK
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                setters
                    .SetProperty(x => x.LockHolder, (string)null)
                    .SetProperty(x => x.LockedAt, (DateTime?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTime?)null);
            }

            // UPDATED_AT ALWAYS
            setters.SetProperty(x => x.UpdatedAt, updatedAt);
        }
    }
}