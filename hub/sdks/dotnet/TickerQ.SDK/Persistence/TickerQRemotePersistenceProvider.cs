using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.SDK.Client;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.SDK.Persistence;

/// <summary>
/// gRPC-based implementation of ITickerPersistenceProvider used by the SDK.
/// Only the methods required for creating, updating, and deleting jobs are implemented.
/// All other members throw NotImplementedException and are intended to be handled
/// by the server-side TickerQ host.
/// </summary>
internal sealed class TickerQRemotePersistenceProvider<TTimeTicker, TCronTicker> :
    ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly TickerQSdkGrpcClient _client;

    public TickerQRemotePersistenceProvider(TickerQSdkGrpcClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    #region Time_Ticker_Core_Methods

    public IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("QueueTimeTickers is handled by the server-side TickerQ host.");

    public IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("QueueTimedOutTimeTickers is handled by the server-side TickerQ host.");

    public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ReleaseAcquiredTimeTickers is handled by the server-side TickerQ host.");

    public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("GetEarliestTimeTickers is handled by the server-side TickerQ host.");

    public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        if (functionContext == null)
            throw new ArgumentNullException(nameof(functionContext));

        return await _client.UpdateTimeTickerContextAsync(functionContext, cancellationToken).ConfigureAwait(false);
    }

    public Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
        => GetRequestBytesAsync(() => _client.GetTimeTickerRequestAsync(id, cancellationToken));

    public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        if (timeTickerIds == null) throw new ArgumentNullException(nameof(timeTickerIds));
        if (functionContext == null) throw new ArgumentNullException(nameof(functionContext));

        await _client.UpdateTimeTickersUnifiedContextAsync(timeTickerIds, functionContext, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Immediate acquisition is handled by the server-side TickerQ host.");

    #endregion

    #region Cron_Ticker_Core_Methods

    public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Cron seeding is handled by the server-side TickerQ host.");

    public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
        => throw new NotImplementedException("GetAllCronTickerExpressions is handled by the server-side TickerQ host.");

    public Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ReleaseDeadNodeTimeTickerResources is handled by the server-side TickerQ host.");

    #endregion

    #region Cron_TickerOccurrence_Core_Methods

    public Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Cron occurrence scheduling is handled by the server-side TickerQ host.");

    public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Cron occurrence queueing is handled by the server-side TickerQ host.");

    public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Cron occurrence timeout handling is handled by the server-side TickerQ host.");

    public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        if (functionContext == null)
            throw new ArgumentNullException(nameof(functionContext));

        await _client.UpdateCronOccurrenceContextAsync(functionContext, cancellationToken).ConfigureAwait(false);
    }

    public Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ReleaseAcquiredCronTickerOccurrences is handled by the server-side TickerQ host.");

    public Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        => GetRequestBytesAsync(() => _client.GetCronOccurrenceRequestAsync(tickerId, cancellationToken));

    public Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Execution status updates are handled by the server-side TickerQ host.");

    public Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("ReleaseDeadNodeOccurrenceResources is handled by the server-side TickerQ host.");

    #endregion

    #region Time_Ticker_Shared_Methods

    public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public Task<TTimeTicker[]> GetTimeTickers(System.Linq.Expressions.Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(System.Linq.Expressions.Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public async Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        if (tickers == null || tickers.Length == 0) return 0;
        return await _client.AddTimeTickersAsync(tickers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        if (tickers == null || tickers.Length == 0) return 0;
        return await _client.UpdateTimeTickersAsync(tickers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        tickerIds ??= Array.Empty<Guid>();
        if (tickerIds.Length == 0) return 0;
        return await _client.DeleteTimeTickersAsync(tickerIds, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Cron_Ticker_Shared_Methods

    public Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public Task<TCronTicker[]> GetCronTickers(System.Linq.Expressions.Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(System.Linq.Expressions.Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
    {
        if (tickers == null || tickers.Length == 0) return 0;
        return await _client.AddCronTickersAsync(tickers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
    {
        if (cronTicker == null || cronTicker.Length == 0) return 0;
        return await _client.UpdateCronTickersAsync(cronTicker, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        cronTickerIds ??= Array.Empty<Guid>();
        if (cronTickerIds.Length == 0) return 0;
        return await _client.DeleteCronTickersAsync(cronTickerIds, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Cron_TickerOccurrence_Shared_Methods

    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(System.Linq.Expressions.Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(System.Linq.Expressions.Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Read operations should be performed against the server-side API directly.");

    public Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken)
        => throw new NotImplementedException("Cron occurrence insertion is handled by the server-side TickerQ host.");

    public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
        => throw new NotImplementedException("Cron occurrence deletion is handled by the server-side TickerQ host.");

    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Immediate cron occurrence acquisition is handled by the server-side TickerQ host.");

    #endregion

    private static async Task<byte[]?> GetRequestBytesAsync(Func<Task<byte[]?>> fetchAsync)
    {
        try
        {
            return await fetchAsync().ConfigureAwait(false);
        }
        catch (global::Grpc.Core.RpcException ex) when (ex.StatusCode == global::Grpc.Core.StatusCode.NotFound)
        {
            return null;
        }
    }
}
