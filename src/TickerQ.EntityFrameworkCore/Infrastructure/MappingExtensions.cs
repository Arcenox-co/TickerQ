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
        public static Expression<Func<TCronTicker, CronTickerEntity>> ForCronTickerExpressions<TCronTicker>() where TCronTicker : CronTickerEntity, new()
            => e => new CronTickerEntity
            {
                Id = e.Id,
                Expression = e.Expression,
                Function = e.Function,
                RetryIntervals = e.RetryIntervals,
                Retries = e.Retries
            };
        
        internal static Expression<Func<TTimeTicker, TimeTickerEntity>> ForQueueTimeTickers<TTimeTicker>() where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            => e => new TimeTickerEntity
            {
                Id = e.Id,
                Function = e.Function,
                Retries = e.Retries,
                RetryIntervals = e.RetryIntervals,
                UpdatedAt = e.UpdatedAt,
                ParentId = e.ParentId,
                ExecutionTime = e.ExecutionTime,
                Children = e.Children.Select(ch => new TimeTickerEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    RunCondition = ch.RunCondition,
                    Children = ch.Children.Select(gch => new TimeTickerEntity
                    {
                        Function = gch.Function,
                        Retries = gch.Retries,
                        RetryIntervals = gch.RetryIntervals,
                        Id = gch.Id,
                        RunCondition = gch.RunCondition
                    }).ToArray()
                }).ToArray()
            };

        internal static Expression<Func<TCronTickerOccurrence, CronTickerOccurrenceEntity<TCronTicker>>> ForQueueCronTickerOccurrence<TCronTickerOccurrence, TCronTicker>()
            where TCronTicker : CronTickerEntity, new()
            where TCronTickerOccurrence : CronTickerOccurrenceEntity<TCronTicker>, new()
            => e => new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = e.Id,
                UpdatedAt = e.UpdatedAt,
                CronTickerId = e.CronTickerId,
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries
                }
            };
        
        internal static Expression<Func<TCronTickerOccurrence, CronTickerOccurrenceEntity<TCronTicker>>> ForLatestQueuedCronTickerOccurrence<TCronTickerOccurrence, TCronTicker>()
            where TCronTicker : CronTickerEntity, new()
            where TCronTickerOccurrence : CronTickerOccurrenceEntity<TCronTicker>, new()
            => e => new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = e.Id,
                CreatedAt = e.CreatedAt,
                CronTickerId = e.CronTickerId,
                ExecutionTime = e.ExecutionTime,
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    Expression = e.CronTicker.Expression,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries
                }
            };

        internal static Expression<Func<SetPropertyCalls<CronTickerOccurrenceEntity<TCronTicker>>, SetPropertyCalls<CronTickerOccurrenceEntity<TCronTicker>>>> UpdateCronTickerOccurrence<TCronTicker>(InternalFunctionContext functionContext)
            where TCronTicker : CronTickerEntity, new()
        {
            var propsToUpdate = functionContext.GetPropsToUpdate();

            Expression<Func<SetPropertyCalls<CronTickerOccurrenceEntity<TCronTicker>>, SetPropertyCalls<CronTickerOccurrenceEntity<TCronTicker>>>> setExpression = 
                calls => calls;
            
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) && functionContext.Status != TickerStatus.Skipped)
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.Status, functionContext.Status));
            else
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.Status, functionContext.Status)
                        .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails));
            
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) && functionContext.Status != TickerStatus.Skipped)
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.RetryCount, functionContext.RetryCount));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.LockHolder, (string)null)
                        .SetProperty(x => x.LockedAt, (DateTime?)null));
            
            // EXECUTION TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutionTime)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.ExecutionTime, functionContext.ExecutionTime));
            
            return setExpression;
        }
        
        internal static Expression<Func<SetPropertyCalls<TTimeTicker>, SetPropertyCalls<TTimeTicker>>> UpdateTimeTicker<TTimeTicker>(InternalFunctionContext functionContext, DateTime updatedAt)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            var propsToUpdate = functionContext.GetPropsToUpdate();

            Expression<Func<SetPropertyCalls<TTimeTicker>, SetPropertyCalls<TTimeTicker>>> setExpression = 
                calls => calls;
            
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) && functionContext.Status != TickerStatus.Skipped)
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.Status, functionContext.Status));
            else
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.Status, functionContext.Status)
                        .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) && functionContext.Status != TickerStatus.Skipped)
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.RetryCount, functionContext.RetryCount));

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
                setExpression = ExpressionHelper.CombineSetters(setExpression,
                    s => s.SetProperty(x => x.LockHolder, (string)null)
                        .SetProperty(x => x.LockedAt, (DateTime?)null));
            
            setExpression = ExpressionHelper.CombineSetters(setExpression,
                s => s.SetProperty(x => x.UpdatedAt, updatedAt));
            
            return setExpression;
        }
    }
}