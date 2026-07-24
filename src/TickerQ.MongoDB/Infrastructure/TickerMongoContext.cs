using MongoDB.Driver;
using TickerQ.Utilities.Entities;

namespace TickerQ.MongoDB.Infrastructure
{
    internal sealed class TickerMongoContext<TTimeTicker, TCronTicker> : ITickerMongoContext<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public TickerMongoContext(IMongoDatabase database, string collectionPrefix)
        {
            Database = database;
            var prefix = collectionPrefix ?? string.Empty;
            TimeTickers = database.GetCollection<TTimeTicker>(prefix + "TimeTickers");
            CronTickers = database.GetCollection<TCronTicker>(prefix + "CronTickers");
            CronTickerOccurrences = database.GetCollection<CronTickerOccurrenceEntity<TCronTicker>>(prefix + "CronTickerOccurrences");
        }

        public IMongoDatabase Database { get; }
        public IMongoCollection<TTimeTicker> TimeTickers { get; }
        public IMongoCollection<TCronTicker> CronTickers { get; }
        public IMongoCollection<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrences { get; }
    }
}
