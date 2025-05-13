using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Src.Provider
{
    internal class
        TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPersistenceProvider<TTimeTicker,
        TCronTicker>
        where TTimeTicker : TimeTicker, new()
        where TCronTicker : CronTicker, new()
    {
        private static readonly ConcurrentDictionary<Guid, TTimeTicker> TimeTickers =
            new ConcurrentDictionary<Guid, TTimeTicker>(new Dictionary<Guid, TTimeTicker>());

        private static readonly ConcurrentDictionary<Guid, TCronTicker> CronTickers =
            new ConcurrentDictionary<Guid, TCronTicker>(new Dictionary<Guid, TCronTicker>());

        private static readonly ConcurrentDictionary<Guid, CronTickerOccurrence<TCronTicker>> CronOccurrences =
            new ConcurrentDictionary<Guid, CronTickerOccurrence<TCronTicker>>(
                new Dictionary<Guid, CronTickerOccurrence<TCronTicker>>());

        #region Time Ticker Operations

        public Task<TTimeTicker> GetTimeTickerById(Guid id, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = TimeTickers.GetValueOrDefault(id);

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = TimeTickers.Values
                .Where(t => ids.Contains(t.Id))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = TimeTickers.Values
                .Where(x =>
                    (x.Status == TickerStatus.Idle ||
                     (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                    x.ExecutionTime >= roundedMinDate &&
                    x.ExecutionTime < roundedMinDate.AddSeconds(1))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetLockedTimeTickers(string lockHolder, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = TimeTickers.Values
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = TimeTickers.Values
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetAllTimeTickers(Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var timeTickers = TimeTickers.Values.ToArray();

            return Task.FromResult(timeTickers);
        }

        public Task<TTimeTicker[]> GetAllLockedTimeTickers(Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var timeTickers = TimeTickers.Values
                .Where(x => x.LockHolder != null)
                .ToArray();

            return Task.FromResult(timeTickers);
        }

        public Task<TTimeTicker[]> GetTimeTickersWithin(DateTime startDate, DateTime endDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var timeTickers = TimeTickers.Values
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToArray();

            return Task.FromResult(timeTickers);
        }

        public Task<byte[]> GetTimeTickerRequest(Guid tickerId, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = TimeTickers.TryGetValue(tickerId, out var t) ? t.Request : null;

            return Task.FromResult(result);
        }

        public Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = TimeTickers.Values
                .Where(x => tickerStatuses.Contains(x.Status)
                            && x.ExecutionTime > now)
                .OrderBy(x => x.ExecutionTime)
                .Select(x => x.ExecutionTime)
                .FirstOrDefault();

            return Task.FromResult((DateTime?)result);
        }

        public Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                TimeTickers.TryAdd(t.Id, t);

            return Task.CompletedTask;
        }

        public Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                TimeTickers[t.Id] = t;

            return Task.CompletedTask;
        }

        public Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                TimeTickers.Remove(t.Id, out _);

            return Task.CompletedTask;
        }

        #endregion

        #region Cron Ticker Operations

        public Task<TCronTicker> GetCronTickerById(Guid id, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronTickers.GetValueOrDefault(id);

            return Task.FromResult(result);
        }

        public Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronTickers.Values
                .Where(t => ids.Contains(t.Id))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TCronTicker[]> GetNextCronTickers(string[] expressions,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronTickers.Values
                .Where(x => expressions.Contains(x.Expression))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TCronTicker[]> GetAllExistingInitializedCronTickers(Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronTickers.Values
                .Where(x => !string.IsNullOrEmpty(x.InitIdentifier) && x.InitIdentifier.StartsWith("MemoryTicker_Seed"))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TCronTicker[]> GetAllCronTickers(Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickers = CronTickers.Values
                .ToArray();

            return Task.FromResult(cronTickers);
        }

        public Task<Tuple<Guid, string>[]> GetAllCronTickerExpressions(Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronTickers.Values
                .Select(x => Tuple.Create(x.Id, x.Expression))
                .Distinct()
                .ToArray();

            return Task.FromResult(result);
        }

        public Task InsertCronTickers(IEnumerable<TCronTicker> tickers, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                CronTickers.TryAdd(t.Id, t);

            return Task.CompletedTask;
        }

        public Task UpdateCronTickers(IEnumerable<TCronTicker> tickers, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                CronTickers[t.Id] = t;

            return Task.CompletedTask;
        }

        public Task RemoveCronTickers(IEnumerable<TCronTicker> tickers, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                CronTickers.TryRemove(t.Id, out _);

            return Task.CompletedTask;
        }

        #endregion

        #region Cron Ticker Occurrence Operations

        public Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurrenceById(Guid id,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronOccurrences.GetValueOrDefault(id);

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByIds(Guid[] ids,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronOccurrences.Values
                .Where(o => ids.Contains(o.Id))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIds(Guid[] ids,
            int? takeLimit, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
             IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrenceEnumerable = CronOccurrences.Values
                .Where(x => ids.Contains(x.CronTickerId))
                .OrderByDescending(x => x.ExecutionTime);
                
            if(takeLimit.HasValue)
                cronTickerOccurrenceEnumerable = cronTickerOccurrenceEnumerable.Take(takeLimit.Value);
            
            return Task.FromResult(cronTickerOccurrenceEnumerable.ToArray());
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurrences(string lockHolder,
            Guid[] cronTickerIds, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronOccurrences.Values
                .Where(x =>
                    cronTickerIds.Contains(x.CronTickerId) &&
                    ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                     (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetLockedCronTickerOccurrences(string lockHolder,
            TickerStatus[] tickerStatuses, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronOccurrences.Values
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronOccurrences.Values
                .Where(x => !x.ExecutedAt.HasValue && x.Status != TickerStatus.Inprogress &&
                            x.Status != TickerStatus.Cancelled)
                .Where(x => x.ExecutionTime < now.AddSeconds(1))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronOccurrences.Values
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => x.Status == TickerStatus.Queued)
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetCronOccurrencesByCronTickerIdAndStatusFlag(Guid tickerId,
            TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var result = CronOccurrences.Values
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => tickerStatuses.Contains(x.Status))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetAllCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetAllLockedCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .Where(x => x.LockHolder != null)
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerId(Guid cronTickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .Where(x => x.CronTickerId == cronTickerId)
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesWithin(DateTime startDate,
            DateTime endDate, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIdWithin(Guid cronTickerId,
            DateTime startDate, DateTime endDate, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .Where(x => x.CronTickerId == cronTickerId)
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetPastCronTickerOccurrencesByCronTickerId(Guid cronTickerId,
            DateTime today, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date < today)
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetTodayCronTickerOccurrencesByCronTickerId(Guid cronTickerId,
            DateTime today, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date == today)
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetFutureCronTickerOccurrencesByCronTickerId(Guid cronTickerId,
            DateTime today, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrences = CronOccurrences.Values
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date > today)
                .ToArray();

            return Task.FromResult(cronTickerOccurrences);
        }

        public Task<byte[]> GetCronTickerRequestViaOccurrence(Guid tickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            if (CronOccurrences.TryGetValue(tickerId, out var cronTickerOccurrence))
            {
                return CronTickers.TryGetValue(cronTickerOccurrence.CronTickerId, out var cronTicker)
                    ? Task.FromResult(cronTicker.Request)
                    : Task.FromResult<byte[]>(null);
            }

            return Task.FromResult<byte[]>(null);
        }

        public Task<DateTime> GetEarliestCronTickerOccurrenceById(Guid id, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var earliestCronTickerOccurrence = CronOccurrences.Values
                .Where(x => x.Id == id)
                .Where(x => tickerStatuses.Contains(x.Status))
                .Min(x => x.ExecutionTime);

            return Task.FromResult(earliestCronTickerOccurrence);
        }

        public Task InsertCronTickerOccurrences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var o in cronTickerOccurrences)
                CronOccurrences.TryAdd(o.Id, o);

            return Task.CompletedTask;
        }

        public Task RemoveCronTickerOccurrences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var o in cronTickerOccurrences)
                CronOccurrences.TryRemove(o.Id, out _);

            return Task.CompletedTask;
        }

        public Task UpdateCronTickerOccurrences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            foreach (var o in cronTickerOccurrences)
                CronOccurrences[o.Id] = o;

            return Task.CompletedTask;
        }

        #endregion
    }
}