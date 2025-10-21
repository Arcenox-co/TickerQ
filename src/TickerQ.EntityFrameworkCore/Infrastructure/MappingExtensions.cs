using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal static class MappingExtensions
    {
        internal static TCronTickerOccurrence ToCronTickerOccurrence<TCronTickerOccurrence, TCronTicker, TCronTickerEntity>(
            this CronTickerOccurrenceEntity<TCronTickerEntity> entity, ICronTickerMapper<TCronTicker, TCronTickerEntity> mapper)
            where TCronTicker : CronTicker, new()
            where TCronTickerOccurrence : CronTickerOccurrence<TCronTicker>, new()
            where TCronTickerEntity : CronTickerEntity, new()
        {
            var cronTickerOccurrence = new TCronTickerOccurrence
            {
                Id = entity.Id,
                CronTickerId = entity.CronTickerId,
                ExecutionTime = entity.ExecutionTime,
                ElapsedTime = entity.ElapsedTime,
                LockedAt = entity.LockedAt,
                Status = entity.Status,
                LockHolder = entity.LockHolder,
                ExecutedAt = entity.ExecutedAt,
                RetryCount = entity.RetryCount,
                Exception = entity.Exception
            };

            var cronTicker = entity.CronTicker;
            if (cronTicker != null)
                cronTickerOccurrence.CronTicker = mapper.ToCronTicker(cronTicker);

            return cronTickerOccurrence;
        }

        internal static CronTickerOccurrenceEntity<TCronTickerEntity> ToCronTickerOccurrenceEntity<TCronTicker,
            TCronTickerOccurrence, TCronTickerEntity>(
            this TCronTickerOccurrence occurrence, ICronTickerMapper<TCronTicker, TCronTickerEntity> mapper)
            where TCronTicker : CronTicker, new()
            where TCronTickerOccurrence : CronTickerOccurrence<TCronTicker>
            where TCronTickerEntity : CronTickerEntity, new()
        {
            var cronTickerOccurrenceEntity = new CronTickerOccurrenceEntity<TCronTickerEntity>
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
            };

            var cronTicker = occurrence.CronTicker;
            if (cronTicker != null)
                cronTickerOccurrenceEntity.CronTicker = mapper.ToCronTickerEntity(cronTicker);

            return cronTickerOccurrenceEntity;
        }
    }
}
