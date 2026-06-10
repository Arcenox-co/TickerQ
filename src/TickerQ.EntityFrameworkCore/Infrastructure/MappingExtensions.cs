using System;
using Microsoft.EntityFrameworkCore.Query;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal static class EfUpdateExtensions
    {
        internal static void UpdateCronTickerOccurrence<TCronTicker>(
            this UpdateSettersBuilder<CronTickerOccurrenceEntity<TCronTicker>> setters,
            InternalFunctionContext functionContext)
            where TCronTicker : CronTickerEntity, new()
        {
            var propsToUpdate = functionContext.GetPropsToUpdate();

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                setters.SetProperty(x => x.Status, functionContext.Status);
            }
            else
            {
                setters
                    .SetProperty(x => x.Status, functionContext.Status)
                    .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                setters
                    .SetProperty(x => x.LockHolder, (string)null)
                    .SetProperty(x => x.LockedAt, (DateTime?)null);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutionTime)))
            {
                setters.SetProperty(x => x.ExecutionTime, functionContext.ExecutionTime);
            }
        }

        internal static void UpdateTimeTicker<TTimeTicker>(this UpdateSettersBuilder<TTimeTicker> setters,
            InternalFunctionContext functionContext, DateTime updatedAt)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            var propsToUpdate = functionContext.GetPropsToUpdate();

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                setters.SetProperty(x => x.Status, functionContext.Status);
            }
            else
            {
                setters
                    .SetProperty(x => x.Status, functionContext.Status)
                    .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                setters
                    .SetProperty(x => x.LockHolder, (string)null)
                    .SetProperty(x => x.LockedAt, (DateTime?)null);
            }

            setters.SetProperty(x => x.UpdatedAt, updatedAt);
        }
    }
}
