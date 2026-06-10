using System;
using System.Linq;
using System.Linq.Expressions;
using TickerQ.Utilities.Entities;

namespace TickerQ.Utilities.Infrastructure
{
    public static class MappingExtensions
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

        public static Expression<Func<TTimeTicker, TimeTickerEntity>> ForQueueTimeTickers<TTimeTicker>()
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

        public static Expression<Func<TCronTickerOccurrence, CronTickerOccurrenceEntity<TCronTicker>>>
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

        public static Expression<Func<TCronTickerOccurrence, CronTickerOccurrenceEntity<TCronTicker>>>
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
    }
}
