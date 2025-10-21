using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    public interface ITimeTickerMapper { }

    public interface ITimeTickerMapper<TTimeTicker, TTimeTickerEntity> : ITimeTickerMapper
        where TTimeTicker : TimeTicker, new()
        where TTimeTickerEntity : TimeTickerEntity, new()
    {
        TTimeTicker ToTimeTicker(TTimeTickerEntity entity);
        TTimeTickerEntity ToTimeTickerEntity(TTimeTicker ticker);
    }

    public class DefaultTimeTickerMapper<TTimeTicker, TTimeTickerEntity>
        : ITimeTickerMapper<TTimeTicker, TTimeTickerEntity>
        where TTimeTicker : TimeTicker, new()
        where TTimeTickerEntity : TimeTickerEntity, new()
    {
        public TTimeTicker ToTimeTicker(TTimeTickerEntity entity)
        {
            var timeTicker = new TTimeTicker
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
                InitIdentifier = entity.InitIdentifier,
                BatchParent = entity.BatchParent,
                BatchRunCondition = entity.BatchRunCondition
            };
            MapCustomPropertiesToTimeTicker(entity, timeTicker);

            return timeTicker;
        }

        protected virtual void MapCustomPropertiesToTimeTicker(TTimeTickerEntity entity, TTimeTicker ticker)
        {
        }

        public virtual TTimeTickerEntity ToTimeTickerEntity(TTimeTicker ticker)
        {
            var entity = new TTimeTickerEntity
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
                InitIdentifier = ticker.InitIdentifier,
                BatchRunCondition = ticker.BatchRunCondition,
                BatchParent = ticker.BatchParent
            };
            MapCustomPropertiesToTimeTickerEntity(entity, ticker);

            return entity;
        }

        protected virtual void MapCustomPropertiesToTimeTickerEntity(TTimeTickerEntity entity, TTimeTicker ticker)
        {

        }
    }
}
