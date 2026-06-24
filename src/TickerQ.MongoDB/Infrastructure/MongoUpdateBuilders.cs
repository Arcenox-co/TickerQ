using System;
using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Infrastructure
{
    /// <summary>
    /// Builds Mongo UpdateDefinition from InternalFunctionContext. Mirrors the EF
    /// EfUpdateExtensions.UpdateTimeTicker / UpdateCronTickerOccurrence helpers
    /// — same conditional-property-write pattern, just emitting $set operations.
    /// </summary>
    internal static class MongoUpdateBuilders
    {
        public static UpdateDefinition<TTimeTicker> BuildTimeTickerUpdate<TTimeTicker>(
            InternalFunctionContext ctx, DateTime updatedAt)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            var props = ctx.GetPropsToUpdate();
            var defs = Builders<TTimeTicker>.Update;
            UpdateDefinition<TTimeTicker> u = null;

            if (props.Contains(nameof(InternalFunctionContext.Status)) && ctx.Status != TickerStatus.Skipped)
            {
                u = Combine(u, defs.Set(x => x.Status, ctx.Status));
            }
            else if (props.Contains(nameof(InternalFunctionContext.Status)))
            {
                u = Combine(u, defs.Set(x => x.Status, ctx.Status));
                u = Combine(u, defs.Set(x => x.SkippedReason, ctx.ExceptionDetails));
            }

            if (props.Contains(nameof(InternalFunctionContext.ExecutedAt)))
                u = Combine(u, defs.Set(x => x.ExecutedAt, ctx.ExecutedAt));

            if (props.Contains(nameof(InternalFunctionContext.ExceptionDetails)) && ctx.Status != TickerStatus.Skipped)
                u = Combine(u, defs.Set(x => x.ExceptionMessage, ctx.ExceptionDetails));

            if (props.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                u = Combine(u, defs.Set(x => x.ElapsedTime, ctx.ElapsedTime));

            if (props.Contains(nameof(InternalFunctionContext.RetryCount)))
                u = Combine(u, defs.Set(x => x.RetryCount, ctx.RetryCount));

            if (props.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                u = Combine(u, defs.Set(x => x.LockHolder, (string)null));
                u = Combine(u, defs.Set(x => x.LockedAt, (DateTime?)null));
            }

            u = Combine(u, defs.Set(x => x.UpdatedAt, updatedAt));
            return u;
        }

        public static UpdateDefinition<CronTickerOccurrenceEntity<TCronTicker>> BuildCronOccurrenceUpdate<TCronTicker>(
            InternalFunctionContext ctx, DateTime updatedAt)
            where TCronTicker : CronTickerEntity, new()
        {
            var props = ctx.GetPropsToUpdate();
            var defs = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update;
            UpdateDefinition<CronTickerOccurrenceEntity<TCronTicker>> u = null;

            if (props.Contains(nameof(InternalFunctionContext.Status)) && ctx.Status != TickerStatus.Skipped)
            {
                u = Combine(u, defs.Set(x => x.Status, ctx.Status));
            }
            else if (props.Contains(nameof(InternalFunctionContext.Status)))
            {
                u = Combine(u, defs.Set(x => x.Status, ctx.Status));
                u = Combine(u, defs.Set(x => x.SkippedReason, ctx.ExceptionDetails));
            }

            if (props.Contains(nameof(InternalFunctionContext.ExecutedAt)))
                u = Combine(u, defs.Set(x => x.ExecutedAt, ctx.ExecutedAt));

            if (props.Contains(nameof(InternalFunctionContext.ExceptionDetails)) && ctx.Status != TickerStatus.Skipped)
                u = Combine(u, defs.Set(x => x.ExceptionMessage, ctx.ExceptionDetails));

            if (props.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                u = Combine(u, defs.Set(x => x.ElapsedTime, ctx.ElapsedTime));

            if (props.Contains(nameof(InternalFunctionContext.RetryCount)))
                u = Combine(u, defs.Set(x => x.RetryCount, ctx.RetryCount));

            if (props.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                u = Combine(u, defs.Set(x => x.LockHolder, (string)null));
                u = Combine(u, defs.Set(x => x.LockedAt, (DateTime?)null));
            }

            if (props.Contains(nameof(InternalFunctionContext.ExecutionTime)))
                u = Combine(u, defs.Set(x => x.ExecutionTime, ctx.ExecutionTime));

            u = Combine(u, defs.Set(x => x.UpdatedAt, updatedAt));
            return u;
        }

        public static FilterDefinition<TTimeTicker> CanAcquireTimeTicker<TTimeTicker>(string lockHolder)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            var fb = Builders<TTimeTicker>.Filter;
            var statusOk = fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued });
            var ownerOrFree = fb.Or(
                fb.Eq(x => x.LockHolder, lockHolder),
                fb.Eq(x => x.LockedAt, (DateTime?)null));
            return fb.And(statusOk, ownerOrFree);
        }

        public static FilterDefinition<CronTickerOccurrenceEntity<TCronTicker>> CanAcquireCronOccurrence<TCronTicker>(string lockHolder)
            where TCronTicker : CronTickerEntity, new()
        {
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var statusOk = fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued });
            var ownerOrFree = fb.Or(
                fb.Eq(x => x.LockHolder, lockHolder),
                fb.Eq(x => x.LockedAt, (DateTime?)null));
            return fb.And(statusOk, ownerOrFree);
        }

        private static UpdateDefinition<T> Combine<T>(UpdateDefinition<T> a, UpdateDefinition<T> b)
            => a is null ? b : Builders<T>.Update.Combine(a, b);
    }
}
