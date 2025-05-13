using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Utilities.Interfaces
{
    internal interface ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTicker, new()
    where TCronTicker : CronTicker, new()
    {
        #region Time Ticker Operations

        Task<TTimeTicker> GetTimeTickerById(Guid id, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetLockedTimeTickers(string lockHolder, TickerStatus[] tickerStatuses, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetAllTimeTickers(Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetAllLockedTimeTickers(Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimeTickersWithin(DateTime startDate, DateTime endDate, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<byte[]> GetTimeTickerRequest(Guid tickerId, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, TickerStatus[] tickerStatuses, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);

        #endregion

        #region Cron Ticker Operations

        Task<TCronTicker> GetCronTickerById(Guid id, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetNextCronTickers(string[] expressions, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetAllExistingInitializedCronTickers(Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<TCronTicker[]> GetAllCronTickers(Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<Tuple<Guid, string>[]> GetAllCronTickerExpressions(Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task InsertCronTickers(IEnumerable<TCronTicker> tickers, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task UpdateCronTickers(IEnumerable<TCronTicker> tickers, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task RemoveCronTickers(IEnumerable<TCronTicker> tickers, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);

        #endregion

        #region Cron Ticker Occurrence Operations

        Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurrenceById(Guid id, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByIds(Guid[] ids, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIds(Guid[] ids, int? takeLimit, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurrences(string lockHolder, Guid[] cronTickerIds, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetLockedCronTickerOccurrences(string lockHolder, TickerStatus[] tickerStatuses, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronOccurrencesByCronTickerIdAndStatusFlag(Guid tickerId, TickerStatus[] tickerStatuses, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetAllCronTickerOccurrences(Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetAllLockedCronTickerOccurrences(Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerId(Guid cronTickerId, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesWithin(DateTime startDate, DateTime endDate, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIdWithin(Guid cronTickerId, DateTime startDate, DateTime endDate, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetPastCronTickerOccurrencesByCronTickerId(Guid cronTickerId, DateTime today, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetTodayCronTickerOccurrencesByCronTickerId(Guid cronTickerId, DateTime today, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrence<TCronTicker>[]> GetFutureCronTickerOccurrencesByCronTickerId(Guid cronTickerId, DateTime today, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<byte[]> GetCronTickerRequestViaOccurrence(Guid tickerId, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task<DateTime> GetEarliestCronTickerOccurrenceById(Guid id, TickerStatus[] tickerStatuses, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task InsertCronTickerOccurrences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task UpdateCronTickerOccurrences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);
        Task RemoveCronTickerOccurrences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, Action<ProviderOptions> options = null, CancellationToken cancellationToken = default);

        #endregion
    }
}