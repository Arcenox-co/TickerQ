using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTicker, new()
    where TCronTicker : CronTicker, new()
    {
        //TimeTicker
        Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, CancellationToken cancellationToken = default);

        Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default);
        Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> RetrieveNextTimeTickers(string lockHolder, DateTime roundedMinDate, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> RetrieveLockedTimeTickers(string lockHolder, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> RetrieveTimedOutTimeTickers(DateTime now, CancellationToken cancellationToken = default);

        Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default);
        Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default);
        Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default);

        //CronTicker
        Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, CancellationToken cancellationToken = default);

        Task<TCronTicker[]> RetrieveNextCronTickers(string[] expressions, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> RetrieveAllExistingInitializedCronTickers(CancellationToken cancellationToken = default);
        Task<string[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default);

        Task InsertCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default);
        Task UpdateCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default);
        Task RemoveCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default);

        //CronTickerOccurrence<CronTicker>
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurencesByIds(Guid[] ids, CancellationToken cancellationToken = default);
        Task<byte[]> GetCronTickerRequestViaOccurence(Guid tickerId, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> RetrieveNextCronTickerOccurences(string lockHolder, Guid[] cronTickerIds, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> RetrieveLockedCronTickerOccurences(string lockHolder, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> RetrieveTimedOutCronTickerOccurrences(DateTime now, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> RetrieveQueuedNextCronOccurrences(Guid tickerId, CancellationToken cancellationToken = default);
        Task InsertCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default);
        Task UpdateCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default);
        Task RemoveCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default);
    }
}
