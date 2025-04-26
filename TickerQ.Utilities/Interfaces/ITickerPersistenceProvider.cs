using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTicker, new()
    where TCronTicker : CronTicker, new()
    {
        #region Time Ticker Operations

        Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetLockedTimeTickers(string lockHolder, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now, CancellationToken cancellationToken = default);

        Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default);
        Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default);

        Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default);
        Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default);
        Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default);

        #endregion

        #region Cron Ticker Operations

        Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetNextCronTickers(string[] expressions, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetAllExistingInitializedCronTickers(CancellationToken cancellationToken = default);

        Task<string[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default);

        Task InsertCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default);
        Task UpdateCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default);
        Task RemoveCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default);

        #endregion

        #region Cron Ticker Occurrence Operations

        Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurenceById(Guid id, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurencesByIds(Guid[] ids, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurences(string lockHolder, Guid[] cronTickerIds, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetLockedCronTickerOccurences(string lockHolder, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId, CancellationToken cancellationToken = default);

        Task<byte[]> GetCronTickerRequestViaOccurence(Guid tickerId, CancellationToken cancellationToken = default);

        Task InsertCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default);
        Task UpdateCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default);
        Task RemoveCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default);

        #endregion
    }
}
