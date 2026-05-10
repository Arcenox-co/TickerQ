using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TickerQ.RemoteExecutor.HubWebhook;

namespace TickerQ.RemoteExecutor.GrpcServices;

/// <summary>
/// gRPC server-side handler for Hub-originated webhooks (function resync /
/// individual function removal). Replaces the old /webhooks/hub HTTP endpoints.
/// </summary>
public sealed class HubWebhookGrpcService : HubWebhookService.HubWebhookServiceBase
{
    private readonly RemoteFunctionsSyncService _syncService;

    public HubWebhookGrpcService(RemoteFunctionsSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    public override Task<Empty> TriggerResync(Empty request, ServerCallContext context)
    {
        // Fire-and-forget so the Hub call returns quickly while the scheduler
        // pulls fresh state from the Hub via gRPC.
        _ = _syncService.SyncOnceAsync(CancellationToken.None);
        return Task.FromResult(new Empty());
    }

    public override Task<RemoveFunctionResult> RemoveFunction(RemoveFunctionRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.FunctionName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "function_name is required"));

        var removed = RemoteFunctionRegistry.Unregister(request.FunctionName);
        return Task.FromResult(new RemoveFunctionResult { Removed = removed });
    }
}
