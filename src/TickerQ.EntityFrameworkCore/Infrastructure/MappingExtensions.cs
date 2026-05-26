using System;
using System.Linq;
using System.Linq.Expressions;
#if !NET10_0_OR_GREATER
using System.Reflection;
#endif
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
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries
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
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    Expression = e.CronTicker.Expression,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries
                }
            };

#if NET10_0_OR_GREATER
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
                setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
                setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
                setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                setters
                    .SetProperty(x => x.LockHolder, (string)null)
                    .SetProperty(x => x.LockedAt, (DateTime?)null);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutionTime)))
                setters.SetProperty(x => x.ExecutionTime, functionContext.ExecutionTime);
        }

        internal static void UpdateTimeTicker<TTimeTicker>(
            this UpdateSettersBuilder<TTimeTicker> setters,
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
                setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
                setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
                setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                setters
                    .SetProperty(x => x.LockHolder, (string)null)
                    .SetProperty(x => x.LockedAt, (DateTime?)null);
            }

            setters.SetProperty(x => x.UpdatedAt, updatedAt);
        }
#else
        // EF Core 9: ExecuteUpdateAsync wants Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>
        // (statically analyzable expression-tree). We dynamically build such an expression
        // by chaining SetProperty<TProp>(Expression<Func<T,TProp>>, TProp) calls.

        private static MethodInfo GetSetPropertyMethod<TSource>(Type propType)
        {
            return typeof(SetPropertyCalls<TSource>).GetMethods()
                .First(m => m.Name == nameof(SetPropertyCalls<TSource>.SetProperty)
                            && m.IsGenericMethodDefinition
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[1].ParameterType.IsGenericParameter)
                .MakeGenericMethod(propType);
        }

        private sealed class ChainBuilder<TSource>
        {
            public ParameterExpression Param { get; } =
                Expression.Parameter(typeof(SetPropertyCalls<TSource>), "s");
            public Expression Body { get; private set; }

            public ChainBuilder() { Body = Param; }

            public void Append<TProp>(Expression<Func<TSource, TProp>> propExpr, TProp value)
            {
                var mi = GetSetPropertyMethod<TSource>(typeof(TProp));
                Body = Expression.Call(Body, mi, propExpr, Expression.Constant(value, typeof(TProp)));
            }

            public Expression<Func<SetPropertyCalls<TSource>, SetPropertyCalls<TSource>>> Build()
                => Expression.Lambda<Func<SetPropertyCalls<TSource>, SetPropertyCalls<TSource>>>(Body, Param);
        }

        internal static Expression<Func<SetPropertyCalls<CronTickerOccurrenceEntity<TCronTicker>>,
                                       SetPropertyCalls<CronTickerOccurrenceEntity<TCronTicker>>>>
            BuildUpdateCronTickerOccurrence<TCronTicker>(InternalFunctionContext functionContext)
            where TCronTicker : CronTickerEntity, new()
        {
            var b = new ChainBuilder<CronTickerOccurrenceEntity<TCronTicker>>();
            var propsToUpdate = functionContext.GetPropsToUpdate();

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                b.Append(x => x.Status, functionContext.Status);
            }
            else
            {
                b.Append(x => x.Status, functionContext.Status);
                b.Append(x => x.SkippedReason, functionContext.ExceptionDetails);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
                b.Append(x => x.ExecutedAt, functionContext.ExecutedAt);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
                b.Append(x => x.ExceptionMessage, functionContext.ExceptionDetails);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                b.Append(x => x.ElapsedTime, functionContext.ElapsedTime);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
                b.Append(x => x.RetryCount, functionContext.RetryCount);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                b.Append(x => x.LockHolder, (string)null);
                b.Append(x => x.LockedAt, (DateTime?)null);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutionTime)))
                b.Append(x => x.ExecutionTime, functionContext.ExecutionTime);

            return b.Build();
        }

        internal static Expression<Func<SetPropertyCalls<TTimeTicker>, SetPropertyCalls<TTimeTicker>>>
            BuildUpdateTimeTicker<TTimeTicker>(InternalFunctionContext functionContext, DateTime updatedAt)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            var b = new ChainBuilder<TTimeTicker>();
            var propsToUpdate = functionContext.GetPropsToUpdate();

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status != TickerStatus.Skipped)
            {
                b.Append(x => x.Status, functionContext.Status);
            }
            else
            {
                b.Append(x => x.Status, functionContext.Status);
                b.Append(x => x.SkippedReason, functionContext.ExceptionDetails);
            }

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
                b.Append(x => x.ExecutedAt, functionContext.ExecutedAt);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                functionContext.Status != TickerStatus.Skipped)
                b.Append(x => x.ExceptionMessage, functionContext.ExceptionDetails);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                b.Append(x => x.ElapsedTime, functionContext.ElapsedTime);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
                b.Append(x => x.RetryCount, functionContext.RetryCount);

            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                b.Append(x => x.LockHolder, (string)null);
                b.Append(x => x.LockedAt, (DateTime?)null);
            }

            b.Append(x => x.UpdatedAt, updatedAt);

            return b.Build();
        }
#endif
    }
}

