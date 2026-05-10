using Grpc.Core;
using TickerQ.RemoteExecutor.Grpc;
using TickerQ.RemoteExecutor.Logging;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.RemoteExecutor.GrpcServices;

/// <summary>
/// gRPC read-only dashboard service. Delegates all queries to
/// <see cref="ITickerDashboardDataService{TTimeTicker,TCronTicker}"/>.
/// </summary>
public sealed class DashboardGrpcService<TTimeTicker, TCronTicker> : DashboardService.DashboardServiceBase
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly ITickerDashboardDataService<TTimeTicker, TCronTicker> _service;
    private readonly TickerLogRingBuffer _logBuffer;

    public DashboardGrpcService(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> service,
        TickerLogRingBuffer logBuffer)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logBuffer = logBuffer ?? throw new ArgumentNullException(nameof(logBuffer));
    }

    // ===== Time tickers =====

    public override async Task<TimeTickerListResponse> GetTimeTickers(TimeTickerQueryRequest request, ServerCallContext context)
    {
        var result = await _service.GetTimeTickersFlatAsync(request.ToFilter(), context.CancellationToken).ConfigureAwait(false);
        var response = new TimeTickerListResponse { Pagination = result.ToProto() };
        foreach (var item in result.Items) response.Items.Add(item.ToProto());
        return response;
    }

    public override async Task<TimeTickerFlatProto> GetTimeTickerById(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id format"));
        var dto = await _service.GetTimeTickerByIdAsync(id, context.CancellationToken).ConfigureAwait(false);
        if (dto == null) throw new RpcException(new Status(StatusCode.NotFound, "Time ticker not found"));
        return dto.ToProto();
    }

    public override async Task<TimeTickerListResponse> GetTimeTickerChildren(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id format"));
        var children = await _service.GetTimeTickerChildrenAsync(id, context.CancellationToken).ConfigureAwait(false);
        var response = new TimeTickerListResponse();
        foreach (var child in children) response.Items.Add(child.ToProto());
        return response;
    }

    public override async Task<TickerRequestResponse> GetTimeTickerRequest(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id format"));
        var bytes = await _service.GetTimeTickerRequestAsync(id, context.CancellationToken).ConfigureAwait(false);
        return new TickerRequestResponse
        {
            Payload = bytes == null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(bytes)
        };
    }

    // ===== Cron tickers =====

    public override async Task<CronTickerListResponse> GetCronTickers(CronTickerQueryRequest request, ServerCallContext context)
    {
        var result = await _service.GetCronTickersFlatAsync(request.ToFilter(), context.CancellationToken).ConfigureAwait(false);
        var response = new CronTickerListResponse { Pagination = result.ToProto() };
        foreach (var item in result.Items) response.Items.Add(item.ToProto());
        return response;
    }

    public override async Task<CronTickerFlatProto> GetCronTickerById(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id format"));
        var dto = await _service.GetCronTickerByIdAsync(id, context.CancellationToken).ConfigureAwait(false);
        if (dto == null) throw new RpcException(new Status(StatusCode.NotFound, "Cron ticker not found"));
        return dto.ToProto();
    }

    // ===== Cron occurrences =====

    public override async Task<CronOccurrenceListResponse> GetCronOccurrences(CronOccurrenceQueryRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CronTickerId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid cron_ticker_id format"));
        var result = await _service.GetCronOccurrencesFlatAsync(id, request.ToFilter(), context.CancellationToken).ConfigureAwait(false);
        var response = new CronOccurrenceListResponse { Pagination = result.ToProto() };
        foreach (var item in result.Items) response.Items.Add(item.ToProto());
        return response;
    }

    public override async Task<CronOccurrenceFlatProto> GetCronOccurrenceById(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id format"));
        var dto = await _service.GetCronOccurrenceByIdAsync(id, context.CancellationToken).ConfigureAwait(false);
        if (dto == null) throw new RpcException(new Status(StatusCode.NotFound, "Occurrence not found"));
        return dto.ToProto();
    }

    public override async Task<TickerRequestResponse> GetCronOccurrenceRequest(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id format"));
        var bytes = await _service.GetCronOccurrenceRequestAsync(id, context.CancellationToken).ConfigureAwait(false);
        return new TickerRequestResponse
        {
            Payload = bytes == null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(bytes)
        };
    }

    // ===== Executions =====

    public override async Task<ExecutionListResponse> GetExecutions(ExecutionQueryRequest request, ServerCallContext context)
    {
        var result = await _service.GetExecutionsFlatAsync(request.ToFilter(), context.CancellationToken).ConfigureAwait(false);
        var response = new ExecutionListResponse { Pagination = result.ToProto() };
        foreach (var item in result.Items) response.Items.Add(item.ToProto());
        return response;
    }

    // ===== Stats =====

    public override async Task<StatusCountsResponse> GetOverallStatuses(EmptyRequest request, ServerCallContext context)
    {
        var data = await _service.GetOverallStatusesAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new StatusCountsResponse();
        foreach (var (status, count) in data)
            response.Items.Add(new StatusCountProto { Status = status.ToProto(), Count = count });
        return response;
    }

    public override async Task<NodeJobCountsResponse> GetNodeJobs(EmptyRequest request, ServerCallContext context)
    {
        var data = await _service.GetNodeJobsAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new NodeJobCountsResponse();
        foreach (var (node, count) in data)
            response.Items.Add(new NodeJobCountProto { NodeName = node ?? string.Empty, JobCount = count });
        return response;
    }

    // ===== Overview =====

    public override async Task<TimeTickerListResponse> GetUpcomingTickers(CountRequest request, ServerCallContext context)
    {
        var data = await _service.GetUpcomingTickersAsync(request.Count, context.CancellationToken).ConfigureAwait(false);
        var response = new TimeTickerListResponse();
        foreach (var item in data) response.Items.Add(item.ToProto());
        return response;
    }

    public override async Task<ExecutionListResponse> GetRecentActivity(CountRequest request, ServerCallContext context)
    {
        var data = await _service.GetRecentActivityAsync(request.Count, context.CancellationToken).ConfigureAwait(false);
        var response = new ExecutionListResponse();
        foreach (var item in data) response.Items.Add(item.ToProto());
        return response;
    }

    // ===== Nodes / Functions =====

    public override async Task<NodeListResponse> GetNodes(EmptyRequest request, ServerCallContext context)
    {
        var data = await _service.GetNodesAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new NodeListResponse();
        foreach (var item in data) response.Items.Add(item.ToProto());
        return response;
    }

    public override async Task<FunctionListResponse> GetNodeFunctions(NodeNameRequest request, ServerCallContext context)
    {
        var data = await _service.GetNodeFunctionsAsync(request.NodeName, context.CancellationToken).ConfigureAwait(false);
        var response = new FunctionListResponse();
        foreach (var item in data) response.Items.Add(item.ToProto());
        return response;
    }

    public override async Task<FunctionListResponse> GetAllFunctions(EmptyRequest request, ServerCallContext context)
    {
        var data = await _service.GetAllFunctionsAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new FunctionListResponse();
        foreach (var item in data) response.Items.Add(item.ToProto());
        return response;
    }

    // ===== Host =====

    public override async Task<HostStatusProto> GetHostStatus(EmptyRequest request, ServerCallContext context)
    {
        var dto = await _service.GetHostStatusAsync(context.CancellationToken).ConfigureAwait(false);
        return dto.ToProto();
    }

    public override async Task<NextTickerProto> GetNextTicker(EmptyRequest request, ServerCallContext context)
    {
        var dto = await _service.GetNextTickerAsync(context.CancellationToken).ConfigureAwait(false);
        return dto.ToProto();
    }

    // ===== Graphs =====

    public override async Task<GraphResponse> GetTimeTickersGraph(GraphRangeRequest request, ServerCallContext context)
    {
        var data = await _service.GetTimeTickersGraphAsync(request.PastDays, request.FutureDays, context.CancellationToken).ConfigureAwait(false);
        var response = new GraphResponse();
        foreach (var bucket in data) response.Buckets.Add(bucket.ToProto());
        return response;
    }

    public override async Task<GraphResponse> GetCronTickersGraph(GraphRangeRequest request, ServerCallContext context)
    {
        var data = await _service.GetCronTickersGraphAsync(request.PastDays, request.FutureDays, context.CancellationToken).ConfigureAwait(false);
        var response = new GraphResponse();
        foreach (var bucket in data) response.Buckets.Add(bucket.ToProto());
        return response;
    }

    public override async Task<GraphResponse> GetCronTickerGraphById(CronTickerGraphRangeRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CronTickerId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid cron_ticker_id"));
        var data = await _service.GetCronTickerGraphByIdAsync(id, request.PastDays, request.FutureDays, context.CancellationToken).ConfigureAwait(false);
        var response = new GraphResponse();
        foreach (var bucket in data) response.Buckets.Add(bucket.ToProto());
        return response;
    }

    public override async Task<GraphResponse> GetCronOccurrencesGraph(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));
        var data = await _service.GetCronOccurrencesGraphAsync(id, context.CancellationToken).ConfigureAwait(false);
        var response = new GraphResponse();
        foreach (var bucket in data) response.Buckets.Add(bucket.ToProto());
        return response;
    }

    // ===== Logs =====

    public override Task<TickerLogTailResponse> GetTickerLogTail(GetByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        var lines = _logBuffer.GetTail(id);
        var response = new TickerLogTailResponse();
        foreach (var line in lines)
        {
            response.Lines.Add(new TickerLogLineProto
            {
                UnixMs = line.UnixMs,
                Level = line.Level,
                Source = line.Source ?? string.Empty,
                Message = line.Message ?? string.Empty,
                Category = line.Category ?? string.Empty,
                FunctionName = line.FunctionName ?? string.Empty
            });
        }
        return Task.FromResult(response);
    }
}
