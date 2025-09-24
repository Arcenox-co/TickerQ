using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Provider
{
    internal class
        TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPersistenceProvider<TTimeTicker,
        TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private static readonly ConcurrentDictionary<Guid, TTimeTicker> TimeTickers =
            new(new Dictionary<Guid, TTimeTicker>());

        private static readonly ConcurrentDictionary<Guid, TCronTicker> CronTickers =
            new(new Dictionary<Guid, TCronTicker>());

        private static readonly ConcurrentDictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>> CronOccurrences =
            new(new Dictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>>());


        public IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}