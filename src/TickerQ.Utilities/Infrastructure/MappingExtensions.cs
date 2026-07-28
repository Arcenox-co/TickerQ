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
                RequestContractVersion = e.RequestContractVersion,
                RequestContractFingerprint = e.RequestContractFingerprint,
                RetryIntervals = e.RetryIntervals,
                Retries = e.Retries,
                TimeoutSeconds = e.TimeoutSeconds,
                IsEnabled = e.IsEnabled
            };

        public static Expression<Func<TTimeTicker, TimeTickerEntity>> ForQueueTimeTickers<TTimeTicker>()
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            => e => new TimeTickerEntity
            {
                Id = e.Id,
                Function = e.Function,
                RequestContractVersion = e.RequestContractVersion,
                RequestContractFingerprint = e.RequestContractFingerprint,
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
                    RequestContractVersion = ch.RequestContractVersion,
                    RequestContractFingerprint = ch.RequestContractFingerprint,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    TimeoutSeconds = ch.TimeoutSeconds,
                    RunCondition = ch.RunCondition,
                    ParentId = ch.ParentId,
                    Children = ch.Children.Select(gch => new TimeTickerEntity
                    {
                        Function = gch.Function,
                        RequestContractVersion = gch.RequestContractVersion,
                        RequestContractFingerprint = gch.RequestContractFingerprint,
                        Retries = gch.Retries,
                        RetryIntervals = gch.RetryIntervals,
                        TimeoutSeconds = gch.TimeoutSeconds,
                        Id = gch.Id,
                        RunCondition = gch.RunCondition,
                        ParentId = gch.ParentId
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
                AcquisitionToken = e.AcquisitionToken,
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    RequestContractVersion = e.CronTicker.RequestContractVersion,
                    RequestContractFingerprint = e.CronTicker.RequestContractFingerprint,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries,
                    TimeoutSeconds = e.CronTicker.TimeoutSeconds
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
                AcquisitionToken = e.AcquisitionToken,
                CronTicker = new TCronTicker
                {
                    Id = e.CronTicker.Id,
                    Function = e.CronTicker.Function,
                    RequestContractVersion = e.CronTicker.RequestContractVersion,
                    RequestContractFingerprint = e.CronTicker.RequestContractFingerprint,
                    Expression = e.CronTicker.Expression,
                    RetryIntervals = e.CronTicker.RetryIntervals,
                    Retries = e.CronTicker.Retries,
                    TimeoutSeconds = e.CronTicker.TimeoutSeconds
                }
            };
    }
}
