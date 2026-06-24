using MongoDB.Driver;
using TickerQ.Utilities.Entities;

namespace TickerQ.MongoDB.Infrastructure
{
    internal interface ITickerMongoContext<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        IMongoDatabase Database { get; }
        IMongoCollection<TTimeTicker> TimeTickers { get; }
        IMongoCollection<TCronTicker> CronTickers { get; }
        IMongoCollection<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrences { get; }
    }
}
