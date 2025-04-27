using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal static class MappingExtensions
    {
        internal static TCronTicker ToCronTicker<TCronTicker>(this CronTickerEntity entity)
            where TCronTicker : CronTicker, new()
        {
            return new TCronTicker()
            {
                Id = entity.Id,
                Expression = entity.Expression,
                Function = entity.Function,
                RetryIntervals = entity.RetryIntervals,
                Retries = entity.Retries,
                Description = entity.Description,
                Request = entity.Request,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                InitIdentifier = entity.InitIdentifier
            };
        }

        internal static TTimeTicker ToTimeTicker<TTimeTicker>(this TimeTickerEntity entity)
            where TTimeTicker : TimeTicker, new()
        {
            return new TTimeTicker()
            {
                Id = entity.Id,
                ElapsedTime = entity.ElapsedTime,
                ExecutionTime = entity.ExecutionTime,
                Status = entity.Status,
                Exception = entity.Exception,
                ExecutedAt = entity.ExecutedAt,
                Request = entity.Request,
                LockedAt = entity.LockedAt,
                LockHolder = entity.LockHolder,
                RetryCount = entity.RetryCount,
                Function = entity.Function,
                RetryIntervals = entity.RetryIntervals,
                Retries = entity.Retries,
                Description = entity.Description,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                InitIdentifier = entity.InitIdentifier
            };
        }

        internal static TCronTickerOccurrence ToCronTickerOccurrence<TCronTickerOccurrence, TCronTicker>(
            this CronTickerOccurrenceEntity<CronTickerEntity> entity)
            where TCronTicker : CronTicker, new()
            where TCronTickerOccurrence : CronTickerOccurrence<TCronTicker>, new()
        {
            return new TCronTickerOccurrence
            {
                Id = entity.Id,
                CronTickerId = entity.CronTicker.Id,
                ExecutionTime = entity.ExecutionTime,
                ElapsedTime = entity.ElapsedTime,
                LockedAt = entity.LockedAt,
                Status = entity.Status,
                LockHolder = entity.LockHolder,
                ExecutedAt = entity.ExecutedAt,
                RetryCount = entity.RetryCount,
                Exception = entity.Exception,
                CronTicker = new TCronTicker
                {
                    Id = entity.CronTicker.Id,
                    Expression = entity.CronTicker.Expression,
                    Function = entity.CronTicker.Function,
                    RetryIntervals = entity.CronTicker.RetryIntervals,
                    Retries = entity.CronTicker.Retries,
                    Description = entity.CronTicker.Description,
                    Request = entity.CronTicker.Request,
                    CreatedAt = entity.CronTicker.CreatedAt,
                    UpdatedAt = entity.CronTicker.UpdatedAt,
                    InitIdentifier = entity.CronTicker.InitIdentifier
                }
            };
        }

        internal static CronTickerEntity ToCronTickerEntity(this CronTicker ticker)
        {
            return new CronTickerEntity
            {
                Id = ticker.Id,
                Expression = ticker.Expression,
                Function = ticker.Function,
                RetryIntervals = ticker.RetryIntervals,
                Retries = ticker.Retries,
                Description = ticker.Description,
                Request = ticker.Request,
                CreatedAt = ticker.CreatedAt,
                UpdatedAt = ticker.UpdatedAt,
                InitIdentifier = ticker.InitIdentifier
            };
        }

        internal static TimeTickerEntity ToTimeTickerEntity(this TimeTicker ticker)
        {
            return new TimeTickerEntity
            {
                Id = ticker.Id,
                ElapsedTime = ticker.ElapsedTime,
                ExecutionTime = ticker.ExecutionTime,
                Status = ticker.Status,
                Exception = ticker.Exception,
                ExecutedAt = ticker.ExecutedAt,
                Request = ticker.Request,
                LockedAt = ticker.LockedAt,
                LockHolder = ticker.LockHolder,
                RetryCount = ticker.RetryCount,
                Function = ticker.Function,
                RetryIntervals = ticker.RetryIntervals,
                Retries = ticker.Retries,
                Description = ticker.Description,
                CreatedAt = ticker.CreatedAt,
                UpdatedAt = ticker.UpdatedAt,
                InitIdentifier = ticker.InitIdentifier
            };
        }

        internal static CronTickerOccurrenceEntity<CronTickerEntity> ToCronTickerOccurrenceEntity<TCronTicker, TCronTickerOccurrence>(
            this TCronTickerOccurrence occurrence)
            where TCronTicker : CronTicker
            where TCronTickerOccurrence : CronTickerOccurrence<TCronTicker>
        {
            return new CronTickerOccurrenceEntity<CronTickerEntity>
            {
                Id = occurrence.Id,
                CronTickerId = occurrence.CronTickerId,
                ExecutionTime = occurrence.ExecutionTime,
                ElapsedTime = occurrence.ElapsedTime,
                LockedAt = occurrence.LockedAt,
                Status = occurrence.Status,
                LockHolder = occurrence.LockHolder,
                ExecutedAt = occurrence.ExecutedAt,
                RetryCount = occurrence.RetryCount,
                Exception = occurrence.Exception,
                CronTicker = new CronTickerEntity
                {
                    Id = occurrence.CronTicker.Id,
                    Expression = occurrence.CronTicker.Expression,
                    Function = occurrence.CronTicker.Function,
                    RetryIntervals = occurrence.CronTicker.RetryIntervals,
                    Retries = occurrence.CronTicker.Retries,
                    Description = occurrence.CronTicker.Description,
                    Request = occurrence.CronTicker.Request,
                    CreatedAt = occurrence.CronTicker.CreatedAt,
                    UpdatedAt = occurrence.CronTicker.UpdatedAt,
                    InitIdentifier = occurrence.CronTicker.InitIdentifier
                }
            };
        }

    }
}
