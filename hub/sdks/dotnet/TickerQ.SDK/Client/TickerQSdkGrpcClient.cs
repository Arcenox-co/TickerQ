using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TickerQ.Grpc.Contracts;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.SDK.Client;

internal sealed class TickerQSdkGrpcClient
{
    private TickerService.TickerServiceClient? _tickerClient;
    private FunctionRegistrationService.FunctionRegistrationServiceClient? _registrationClient;
    private ExecutionService.ExecutionServiceClient? _executionClient;

    private readonly TickerQGrpcChannelProvider _channelProvider;

    // Hub gRPC client (separate channel to hub.tickerq.net)
    private HubService.HubServiceClient? _hubClient;
    private readonly TickerQHubGrpcChannelProvider? _hubChannelProvider;

    public TickerQSdkGrpcClient(
        TickerQGrpcChannelProvider channelProvider,
        TickerQHubGrpcChannelProvider? hubChannelProvider = null)
    {
        _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
        _hubChannelProvider = hubChannelProvider;
    }

    #region Time Tickers

    public async Task<int> AddTimeTickersAsync<T>(T[] tickers, CancellationToken cancellationToken)
        where T : TimeTickerEntity<T>, new()
    {
        var client = await GetTickerClientAsync();
        var request = new AddTimeTickersRequest();
        request.Tickers.AddRange(tickers.Select(ToProtoTimeTicker));
        var response = await client.AddTimeTickersAsync(request, cancellationToken: cancellationToken);
        return response.Affected;
    }

    public async Task<int> UpdateTimeTickersAsync<T>(T[] tickers, CancellationToken cancellationToken)
        where T : TimeTickerEntity<T>, new()
    {
        var client = await GetTickerClientAsync();
        var request = new UpdateTimeTickersRequest();
        request.Tickers.AddRange(tickers.Select(ToProtoTimeTicker));
        var response = await client.UpdateTimeTickersAsync(request, cancellationToken: cancellationToken);
        return response.Affected;
    }

    public async Task<int> DeleteTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        var client = await GetTickerClientAsync();
        var request = new IdsRequest();
        request.Ids.AddRange(ids.Select(id => id.ToString()));
        var response = await client.DeleteTimeTickersAsync(request, cancellationToken: cancellationToken);
        return response.Affected;
    }

    public async Task<byte[]?> GetTimeTickerRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        var client = await GetTickerClientAsync();
        var response = await client.GetTimeTickerRequestAsync(
            new IdRequest { Id = id.ToString() },
            cancellationToken: cancellationToken);
        return response.Data.IsEmpty ? null : response.Data.ToByteArray();
    }

    public async Task<int> UpdateTimeTickerContextAsync(InternalFunctionContext context, CancellationToken cancellationToken)
    {
        var client = await GetTickerClientAsync();
        var proto = ToProtoContext(context);
        var response = await client.UpdateTimeTickerContextAsync(proto, cancellationToken: cancellationToken);
        return response.Affected;
    }

    public async Task UpdateTimeTickersUnifiedContextAsync(Guid[] ids, InternalFunctionContext context, CancellationToken cancellationToken)
    {
        var client = await GetTickerClientAsync();
        var request = new UnifiedContextRequest { Context = ToProtoContext(context) };
        request.Ids.AddRange(ids.Select(id => id.ToString()));
        await client.UpdateTimeTickersUnifiedContextAsync(request, cancellationToken: cancellationToken);
    }

    #endregion

    #region Cron Tickers

    public async Task<int> AddCronTickersAsync<T>(T[] tickers, CancellationToken cancellationToken)
        where T : CronTickerEntity
    {
        var client = await GetTickerClientAsync();
        var request = new AddCronTickersRequest();
        request.Tickers.AddRange(tickers.Select(ToProtoCronTicker));
        var response = await client.AddCronTickersAsync(request, cancellationToken: cancellationToken);
        return response.Affected;
    }

    public async Task<int> UpdateCronTickersAsync<T>(T[] tickers, CancellationToken cancellationToken)
        where T : CronTickerEntity
    {
        var client = await GetTickerClientAsync();
        var request = new UpdateCronTickersRequest();
        request.Tickers.AddRange(tickers.Select(ToProtoCronTicker));
        var response = await client.UpdateCronTickersAsync(request, cancellationToken: cancellationToken);
        return response.Affected;
    }

    public async Task<int> DeleteCronTickersAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        var client = await GetTickerClientAsync();
        var request = new IdsRequest();
        request.Ids.AddRange(ids.Select(id => id.ToString()));
        var response = await client.DeleteCronTickersAsync(request, cancellationToken: cancellationToken);
        return response.Affected;
    }

    #endregion

    #region Cron Occurrences

    public async Task UpdateCronOccurrenceContextAsync(InternalFunctionContext context, CancellationToken cancellationToken)
    {
        var client = await GetTickerClientAsync();
        var proto = ToProtoContext(context);
        await client.UpdateCronOccurrenceContextAsync(proto, cancellationToken: cancellationToken);
    }

    public async Task<byte[]?> GetCronOccurrenceRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        var client = await GetTickerClientAsync();
        var response = await client.GetCronOccurrenceRequestAsync(
            new IdRequest { Id = id.ToString() },
            cancellationToken: cancellationToken);
        return response.Data.IsEmpty ? null : response.Data.ToByteArray();
    }

    #endregion

    #region Function Registration

    public async Task RegisterFunctionsAsync(FunctionDescriptor[] functions, string nodeName, CancellationToken cancellationToken)
    {
        var client = await GetRegistrationClientAsync();
        var request = new RegisterFunctionsRequest { NodeName = nodeName };
        request.Functions.AddRange(functions);
        await client.RegisterFunctionsAsync(request, cancellationToken: cancellationToken);
    }

    #endregion

    #region Execution Stream

    public AsyncDuplexStreamingCall<ExecutionResult, ExecutionCommand> OpenExecutionStream()
    {
        var client = GetExecutionClientSync();
        return client.ExecutionStream();
    }

    public AsyncClientStreamingCall<LogEntry, LogAck> OpenLogStream()
    {
        var client = GetExecutionClientSync();
        return client.SendLogs();
    }

    #endregion

    #region Hub Service

    public async Task<SyncNodesFunctionsResponse> SyncNodesFunctionsAsync(
        SyncNodesFunctionsRequest request, CancellationToken cancellationToken)
    {
        var client = GetHubClient();
        return await client.SyncNodesFunctionsAsync(request, cancellationToken: cancellationToken);
    }

    #endregion

    #region Client Initialization

    private async Task<TickerService.TickerServiceClient> GetTickerClientAsync()
    {
        if (_tickerClient != null) return _tickerClient;
        var channel = await _channelProvider.GetChannelAsync();
        _tickerClient = new TickerService.TickerServiceClient(channel);
        return _tickerClient;
    }

    private async Task<FunctionRegistrationService.FunctionRegistrationServiceClient> GetRegistrationClientAsync()
    {
        if (_registrationClient != null) return _registrationClient;
        var channel = await _channelProvider.GetChannelAsync();
        _registrationClient = new FunctionRegistrationService.FunctionRegistrationServiceClient(channel);
        return _registrationClient;
    }

    private ExecutionService.ExecutionServiceClient GetExecutionClientSync()
    {
        if (_executionClient != null) return _executionClient;
        var channel = _channelProvider.GetChannelIfReady()
            ?? throw new InvalidOperationException("gRPC channel is not ready yet. Ensure hub sync completed.");
        _executionClient = new ExecutionService.ExecutionServiceClient(channel);
        return _executionClient;
    }

    private HubService.HubServiceClient GetHubClient()
    {
        if (_hubClient != null) return _hubClient;
        if (_hubChannelProvider == null)
            throw new InvalidOperationException("Hub gRPC channel provider is not configured.");
        _hubClient = new HubService.HubServiceClient(_hubChannelProvider.GetChannel());
        return _hubClient;
    }

    #endregion

    #region Proto Mapping

    private static TimeTickerMessage ToProtoTimeTicker<T>(T ticker) where T : TimeTickerEntity<T>, new()
    {
        var msg = new TimeTickerMessage
        {
            Id = ticker.Id.ToString(),
            Function = ticker.Function ?? string.Empty,
            Description = ticker.Description ?? string.Empty,
            InitIdentifier = ticker.InitIdentifier ?? string.Empty,
            CreatedAt = ToTimestamp(ticker.CreatedAt),
            UpdatedAt = ToTimestamp(ticker.UpdatedAt),
            Status = (TickerQ.Grpc.Contracts.TickerStatus)(int)ticker.Status,
            LockHolder = ticker.LockHolder ?? string.Empty,
            ExceptionMessage = ticker.ExceptionMessage ?? string.Empty,
            SkippedReason = ticker.SkippedReason ?? string.Empty,
            ElapsedTime = ticker.ElapsedTime,
            Retries = ticker.Retries,
            RetryCount = ticker.RetryCount
        };

        if (ticker.Request is { Length: > 0 })
            msg.Request = ByteString.CopyFrom(ticker.Request);

        if (ticker.ExecutionTime.HasValue)
            msg.ExecutionTime = ToTimestamp(ticker.ExecutionTime.Value);

        if (ticker.LockedAt.HasValue)
            msg.LockedAt = ToTimestamp(ticker.LockedAt.Value);

        if (ticker.ExecutedAt.HasValue)
            msg.ExecutedAt = ToTimestamp(ticker.ExecutedAt.Value);

        if (ticker.RetryIntervals is { Length: > 0 })
            msg.RetryIntervals.AddRange(ticker.RetryIntervals);

        if (ticker.ParentId.HasValue)
            msg.ParentId = ticker.ParentId.Value.ToString();

        if (ticker.RunCondition.HasValue)
            msg.RunCondition = (TickerQ.Grpc.Contracts.RunCondition)(int)ticker.RunCondition.Value;

        return msg;
    }

    private static CronTickerMessage ToProtoCronTicker(CronTickerEntity ticker)
    {
        var msg = new CronTickerMessage
        {
            Id = ticker.Id.ToString(),
            Function = ticker.Function ?? string.Empty,
            Description = ticker.Description ?? string.Empty,
            InitIdentifier = ticker.InitIdentifier ?? string.Empty,
            CreatedAt = ToTimestamp(ticker.CreatedAt),
            UpdatedAt = ToTimestamp(ticker.UpdatedAt),
            Expression = ticker.Expression ?? string.Empty,
            Retries = ticker.Retries,
            IsEnabled = ticker.IsEnabled
        };

        if (ticker.Request is { Length: > 0 })
            msg.Request = ByteString.CopyFrom(ticker.Request);

        if (ticker.RetryIntervals is { Length: > 0 })
            msg.RetryIntervals.AddRange(ticker.RetryIntervals);

        return msg;
    }

    private static FunctionContext ToProtoContext(InternalFunctionContext ctx)
    {
        var proto = new FunctionContext
        {
            FunctionName = ctx.FunctionName ?? string.Empty,
            TickerId = ctx.TickerId.ToString(),
            Type = (TickerQ.Grpc.Contracts.TickerType)(int)ctx.Type,
            Retries = ctx.Retries,
            RetryCount = ctx.RetryCount,
            Status = (TickerQ.Grpc.Contracts.TickerStatus)(int)ctx.Status,
            ElapsedTime = ctx.ElapsedTime,
            ExceptionDetails = ctx.ExceptionDetails ?? string.Empty,
            ReleaseLock = ctx.ReleaseLock,
            RunCondition = (TickerQ.Grpc.Contracts.RunCondition)(int)ctx.RunCondition,
            CachedPriority = (TickerQ.Grpc.Contracts.TickerTaskPriority)(int)ctx.CachedPriority,
            CachedMaxConcurrency = ctx.CachedMaxConcurrency
        };

        if (ctx.ParentId.HasValue)
            proto.ParentId = ctx.ParentId.Value.ToString();

        if (ctx.ExecutedAt != default)
            proto.ExecutedAt = ToTimestamp(ctx.ExecutedAt);

        if (ctx.ExecutionTime != default)
            proto.ExecutionTime = ToTimestamp(ctx.ExecutionTime);

        if (ctx.RetryIntervals is { Length: > 0 })
            proto.RetryIntervals.AddRange(ctx.RetryIntervals);

        if (ctx.ParametersToUpdate is { Count: > 0 })
            proto.ParametersToUpdate.AddRange(ctx.ParametersToUpdate);

        return proto;
    }

    private static Timestamp ToTimestamp(DateTime dt)
    {
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
    }

    #endregion
}
