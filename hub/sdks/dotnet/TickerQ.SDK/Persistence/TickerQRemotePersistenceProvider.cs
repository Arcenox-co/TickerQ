using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using TickerQ.SDK.WorkerStream;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;

namespace TickerQ.SDK.Persistence;

/// <summary>
/// SDK-side <see cref="ITickerPersistenceProvider{TTimeTicker, TCronTicker}"/> implementation
/// that writes every CRUD/status request as a <see cref="WorkerEvent"/> onto the long-lived
/// worker stream held by <see cref="WorkerStreamHostedService"/> and awaits the matching
/// <see cref="OperationResult"/> / <see cref="BytesResult"/> by request_id.
///
/// No transient gRPC channels are opened. The SDK process needs only outbound TCP to the
/// scheduler — no inbound port, no callback URL, no per-call HMAC (auth is once-per-stream
/// in the Hello handshake).
/// </summary>
internal sealed class TickerQRemotePersistenceProvider<TTimeTicker, TCronTicker> :
    ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    private readonly TickerSdkOptions _options;
    private readonly WorkerStreamHostedService _stream;

    public TickerQRemotePersistenceProvider(TickerSdkOptions options, WorkerStreamHostedService stream)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Qualify a function name with this SDK's NodeName so the scheduler stores tickers as
    /// "bare@node" — the scheduler's function registry is keyed by qualified name to
    /// support multiple SDKs hosting the same bare name. Idempotent.
    /// </summary>
    private string QualifyFunction(string function)
    {
        if (string.IsNullOrWhiteSpace(function)) return function;
        if (function.Contains('@')) return function;
        var node = _options.NodeName;
        return string.IsNullOrWhiteSpace(node) ? function : $"{function}@{node}";
    }

    private static FunctionContext MapContext(InternalFunctionContext c)
    {
        var f = new FunctionContext
        {
            FunctionName = c.FunctionName ?? string.Empty,
            TickerId = c.TickerId.ToString(),
            Type = (int)c.Type,
            Retries = c.Retries,
            RetryCount = c.RetryCount,
            Status = (int)c.Status,
            ElapsedTimeMs = c.ElapsedTime,
            ExceptionDetails = c.ExceptionDetails ?? string.Empty,
            ReleaseLock = c.ReleaseLock,
            RunCondition = (int)c.RunCondition
        };
        if (c.ParentId.HasValue) f.ParentId = c.ParentId.Value.ToString();
        if (c.ExecutedAt != default) f.ExecutedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(c.ExecutedAt, DateTimeKind.Utc));
        if (c.ExecutionTime != default) f.ExecutionTime = Timestamp.FromDateTime(DateTime.SpecifyKind(c.ExecutionTime, DateTimeKind.Utc));
        if (c.RetryIntervals != null) f.RetryIntervals.Add(c.RetryIntervals);
        if (c.ParametersToUpdate != null) f.ParametersToUpdate.Add(c.ParametersToUpdate);
        return f;
    }

    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    private async Task<int> AwaitOperation(string requestId, WorkerEvent evt, CancellationToken ct)
    {
        var result = await _stream.SendAndAwaitOperationAsync(requestId, evt, OperationTimeout, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.Error) ? "Scheduler reported failure" : result.Error);
        return result.Affected;
    }

    private async Task<byte[]?> AwaitBytes(string requestId, WorkerEvent evt, CancellationToken ct)
    {
        var result = await _stream.SendAndAwaitBytesAsync(requestId, evt, OperationTimeout, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.Error) ? "Scheduler reported failure" : result.Error);
        return result.Found ? result.Payload.ToByteArray() : null;
    }

    #region Queryable_Methods (server-side only)
    public ITickerQueryable<TTimeTicker> TimeTickersQuery() => throw new NotImplementedException();
    public ITickerQueryable<TCronTicker> CronTickersQuery() => throw new NotImplementedException();
    public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery() => throw new NotImplementedException();
    #endregion

    #region Time_Ticker_Core_Methods
    public IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var rid = NewRequestId();
        return await AwaitOperation(rid, new WorkerEvent
        {
            UpdateTimeTicker = new UpdateTimeTickerRequest { RequestId = rid, Context = MapContext(functionContext) }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
    {
        var rid = NewRequestId();
        var bytes = await AwaitBytes(rid, new WorkerEvent
        {
            GetTimeTickerRequest = new GetTimeTickerRequestRequest { RequestId = rid, TickerId = id.ToString() }
        }, cancellationToken).ConfigureAwait(false);
        return bytes!;
    }

    public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var rid = NewRequestId();
        var req = new UpdateTimeTickersUnifiedRequest { RequestId = rid, Context = MapContext(functionContext) };
        req.Ids.Add(timeTickerIds.Select(id => id.ToString()));
        await AwaitOperation(rid, new WorkerEvent { UpdateTimeTickersUnified = req }, cancellationToken).ConfigureAwait(false);
    }

    public Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    #endregion

    #region Cron_Ticker_Core_Methods
    public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    #endregion

    #region Cron_TickerOccurrence_Core_Methods
    public Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var rid = NewRequestId();
        await AwaitOperation(rid, new WorkerEvent
        {
            UpdateCronOccurrence = new UpdateCronTickerOccurrenceRequest { RequestId = rid, Context = MapContext(functionContext) }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        var rid = NewRequestId();
        var bytes = await AwaitBytes(rid, new WorkerEvent
        {
            GetCronOccurrenceRequest = new GetCronTickerOccurrenceRequestRequest { RequestId = rid, TickerId = tickerId.ToString() }
        }, cancellationToken).ConfigureAwait(false);
        return bytes!;
    }

    public Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    #endregion

    #region Time_Ticker_Shared_Methods
    public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<TTimeTicker[]> GetTimeTickers(System.Linq.Expressions.Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(System.Linq.Expressions.Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public async Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        if (tickers == null || tickers.Length == 0) return 0;
        foreach (var t in tickers) t.Function = QualifyFunction(t.Function);
        var rid = NewRequestId();
        return await AwaitOperation(rid, new WorkerEvent
        {
            AddTimeTickers = new AddTimeTickersRequest
            {
                RequestId = rid,
                EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(tickers, Json))
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        if (tickers == null || tickers.Length == 0) return 0;
        foreach (var t in tickers) t.Function = QualifyFunction(t.Function);
        var rid = NewRequestId();
        return await AwaitOperation(rid, new WorkerEvent
        {
            UpdateTimeTickers = new UpdateTimeTickersRequest
            {
                RequestId = rid,
                EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(tickers, Json))
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        if (tickerIds == null || tickerIds.Length == 0) return 0;
        var rid = NewRequestId();
        var req = new RemoveTimeTickersRequest { RequestId = rid };
        req.Ids.Add(tickerIds.Select(id => id.ToString()));
        return await AwaitOperation(rid, new WorkerEvent { RemoveTimeTickers = req }, cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region Cron_Ticker_Shared_Methods
    public Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<TCronTicker[]> GetCronTickers(System.Linq.Expressions.Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(System.Linq.Expressions.Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
    {
        if (tickers == null || tickers.Length == 0) return 0;
        foreach (var t in tickers) t.Function = QualifyFunction(t.Function);
        var rid = NewRequestId();
        return await AwaitOperation(rid, new WorkerEvent
        {
            InsertCronTickers = new InsertCronTickersRequest
            {
                RequestId = rid,
                EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(tickers, Json))
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
    {
        if (cronTicker == null || cronTicker.Length == 0) return 0;
        foreach (var t in cronTicker) t.Function = QualifyFunction(t.Function);
        var rid = NewRequestId();
        return await AwaitOperation(rid, new WorkerEvent
        {
            UpdateCronTickers = new UpdateCronTickersRequest
            {
                RequestId = rid,
                EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(cronTicker, Json))
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        if (cronTickerIds == null || cronTickerIds.Length == 0) return 0;
        var rid = NewRequestId();
        var req = new RemoveCronTickersRequest { RequestId = rid };
        req.Ids.Add(cronTickerIds.Select(id => id.ToString()));
        return await AwaitOperation(rid, new WorkerEvent { RemoveCronTickers = req }, cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region Cron_TickerOccurrence_Shared_Methods
    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(System.Linq.Expressions.Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(System.Linq.Expressions.Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    #endregion
}
