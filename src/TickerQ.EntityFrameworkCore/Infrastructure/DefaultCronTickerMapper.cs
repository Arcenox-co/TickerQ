using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    public interface ICronTickerMapper { }

    public interface ICronTickerMapper<TCronTicker, TCronTickerEntity> : ICronTickerMapper
        where TCronTicker : CronTicker, new()
        where TCronTickerEntity : CronTickerEntity, new()
    {
        TCronTicker ToCronTicker(TCronTickerEntity entity);
        TCronTickerEntity ToCronTickerEntity(TCronTicker ticker);
    }

    public class DefaultCronTickerMapper<TCronTicker, TCronTickerEntity>
        : ICronTickerMapper<TCronTicker, TCronTickerEntity>
        where TCronTicker : CronTicker, new()
        where TCronTickerEntity : CronTickerEntity, new()
    {
        public TCronTicker ToCronTicker(TCronTickerEntity entity)
        {
            var ticker = new TCronTicker
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
            MapCustomPropertiesToCronTicker(entity, ticker);

            return ticker;
        }

        protected virtual void MapCustomPropertiesToCronTicker(TCronTickerEntity entity, TCronTicker ticker)
        {
        }

        public TCronTickerEntity ToCronTickerEntity(TCronTicker ticker)
        {
            var entity = new TCronTickerEntity
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
            MapCustomPropertiesToCronTickerEntity(entity, ticker);

            return entity;
        }

        protected virtual void MapCustomPropertiesToCronTickerEntity(TCronTickerEntity entity, TCronTicker ticker)
        {
        }
    }
}
