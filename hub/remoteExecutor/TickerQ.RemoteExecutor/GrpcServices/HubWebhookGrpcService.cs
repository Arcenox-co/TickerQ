using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TickerQ.Grpc.Contracts;
using TickerQ.Utilities;

namespace TickerQ.RemoteExecutor.GrpcServices;

/// <summary>
/// gRPC service hosted on RemoteExecutor, called by hub.tickerq.net.
/// Replaces the HTTP webhook endpoints (webhooks/hub, webhooks/hub/remove-function).
/// </summary>
internal sealed class HubWebhookGrpcService : HubWebhookService.HubWebhookServiceBase
{
    private readonly RemoteFunctionsSyncService _syncService;

    public HubWebhookGrpcService(RemoteFunctionsSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    public override Task<Empty> TriggerResync(Empty request, ServerCallContext context)
    {
        _ = _syncService.SyncOnceAsync(CancellationToken.None);
        return Task.FromResult(new Empty());
    }

    public override Task<RemoveFunctionResult> RemoveFunction(RemoveFunctionRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.FunctionName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "FunctionName is required."));

        var removed = RemoveFunctionByName(request.FunctionName);
        return Task.FromResult(new RemoveFunctionResult { Removed = removed });
    }

    private static bool RemoveFunctionByName(string functionName)
    {
        if (!RemoteFunctionRegistry.IsRemote(functionName))
            return false;

        var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        if (!functionDict.Remove(functionName))
            return false;

        RemoteFunctionRegistry.Remove(functionName);
        var requestInfoDict = TickerFunctionProvider.TickerFunctionRequestInfos?.ToDictionary()
            ?? new Dictionary<string, (string RequestType, string RequestExampleJson)>();
        requestInfoDict.Remove(functionName);
        TickerFunctionProvider.RegisterFunctions(functionDict);
        var existingRequestTypes = TickerFunctionProvider.TickerFunctionRequestTypes;
        if (existingRequestTypes != null && existingRequestTypes.Count > 0)
        {
            var requestTypesDict = existingRequestTypes.ToDictionary();
            requestTypesDict.Remove(functionName);
            TickerFunctionProvider.RegisterRequestType(requestTypesDict);
        }
        TickerFunctionProvider.RegisterRequestInfo(requestInfoDict);
        TickerFunctionProvider.Build();
        return true;
    }
}
