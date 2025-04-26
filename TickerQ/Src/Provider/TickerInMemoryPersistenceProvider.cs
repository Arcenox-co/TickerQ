using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Src.Provider
{
    public class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPersistenceProvider<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTicker, new()
        where TCronTicker : CronTicker, new()
    {
        private static readonly Dictionary<Guid, TTimeTicker> _timeTickers = new Dictionary<Guid, TTimeTicker>();
        private static readonly Dictionary<Guid, TCronTicker> _cronTickers = new Dictionary<Guid, TCronTicker>();
        private static readonly Dictionary<Guid, CronTickerOccurrence<TCronTicker>> _cronOccurrences = new Dictionary<Guid, CronTickerOccurrence<TCronTicker>>();

        #region Time Ticker Operations

        public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            var result = _timeTickers.GetValueOrDefault(id);

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var result = _timeTickers.Values
                .Where(t => ids.Contains(t.Id))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate, CancellationToken cancellationToken = default)
        {
            var result = _timeTickers.Values
                .Where(x =>
                    ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                     (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                    x.ExecutionTime >= roundedMinDate &&
                    x.ExecutionTime < roundedMinDate.AddSeconds(1))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetLockedTimeTickers(string lockHolder, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default)
        {
            var result = _timeTickers.Values
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now, CancellationToken cancellationToken = default)
        {
            var result = _timeTickers.Values
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var result = _timeTickers.TryGetValue(tickerId, out var t) ? t.Request : null;

            return Task.FromResult(result);
        }

        public Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default)
        {
            var result = _timeTickers.Values
                .Where(x => x.LockHolder == null
                            && tickerStatuses.Contains(x.Status)
                            && x.ExecutionTime > now)
                .OrderBy(x => x.ExecutionTime)
                .Select(x => x.ExecutionTime)
                .FirstOrDefault();

            return Task.FromResult((DateTime?)result);
        }

        public Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                _timeTickers.Add(t.Id, t);

            return Task.CompletedTask;
        }

        public Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                _timeTickers[t.Id] = t;

            return Task.CompletedTask;
        }

        public Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                _timeTickers.Remove(t.Id);

            return Task.CompletedTask;
        }

        #endregion

        #region Cron Ticker Operations

        public Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            var result = _cronTickers.GetValueOrDefault(id);

            return Task.FromResult(result);
        }

        public Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var result = _cronTickers.Values
                .Where(t => ids.Contains(t.Id))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TCronTicker[]> GetNextCronTickers(string[] expressions, CancellationToken cancellationToken = default)
        {
            var result = _cronTickers.Values
                .Where(x => expressions.Contains(x.Expression))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<TCronTicker[]> GetAllExistingInitializedCronTickers(CancellationToken cancellationToken = default)
        {
            var result = _cronTickers.Values
                .Where(x => !string.IsNullOrEmpty(x.InitIdentifier) && x.InitIdentifier.StartsWith("MemoryTicker_Seed"))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<string[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
        {
            var result = _cronTickers.Values
                .Select(x => x.Expression)
                .Distinct()
                .ToArray();

            return Task.FromResult(result);
        }

        public Task InsertCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                _cronTickers.Add(t.Id, t);

            return Task.CompletedTask;
        }

        public Task UpdateCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                _cronTickers[t.Id] = t;

            return Task.CompletedTask;
        }

        public Task RemoveCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default)
        {
            foreach (var t in tickers)
                _cronTickers.Remove(t.Id);

            return Task.CompletedTask;
        }

        #endregion

        #region Cron Ticker Occurrence Operations

        public Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurenceById(Guid id, CancellationToken cancellationToken = default)
        {
            var result = _cronOccurrences.GetValueOrDefault(id);

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurencesByIds(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var result = _cronOccurrences.Values
                .Where(o => ids.Contains(o.Id))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurences(string lockHolder, Guid[] cronTickerIds, CancellationToken cancellationToken = default)
        {
            var result = _cronOccurrences.Values
                .Where(x =>
                    cronTickerIds.Contains(x.CronTickerId) &&
                    ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                     (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetLockedCronTickerOccurences(string lockHolder, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default)
        {
            var result = _cronOccurrences.Values
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now, CancellationToken cancellationToken = default)
        {
            var result = _cronOccurrences.Values
                .Where(x => !x.ExecutedAt.HasValue && x.Status != TickerStatus.Inprogress &&
                            x.Status != TickerStatus.Cancelled)
                .Where(x => x.ExecutionTime < now.AddSeconds(1))
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var result = _cronOccurrences.Values
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => x.Status == TickerStatus.Queued)
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<byte[]> GetCronTickerRequestViaOccurence(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var result = _cronOccurrences.TryGetValue(tickerId, out var t) ? t.CronTicker.Request : null;

            return Task.FromResult(result);
        }

        public Task InsertCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            foreach (var o in cronTickerOccurrences)
                _cronOccurrences.Add(o.Id, o);

            return Task.CompletedTask;
        }

        public Task RemoveCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            foreach (var o in cronTickerOccurrences)
                _cronOccurrences.Remove(o.Id);

            return Task.CompletedTask;
        }

        public Task UpdateCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            foreach (var o in cronTickerOccurrences)
                _cronOccurrences[o.Id] = o;

            return Task.CompletedTask;
        }

        #endregion
    }
}
