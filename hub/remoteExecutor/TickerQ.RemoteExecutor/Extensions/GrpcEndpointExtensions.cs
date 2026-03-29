using Microsoft.AspNetCore.Routing;
using TickerQ.RemoteExecutor.GrpcServices;

namespace TickerQ.RemoteExecutor;

public static class GrpcEndpointExtensions
{
    /// <summary>
    /// Maps all TickerQ gRPC services (SDK communication + Hub webhooks).
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQGrpcServices(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

        // SDK-facing services
        endpoints.MapGrpcService<TickerGrpcService>();
        endpoints.MapGrpcService<FunctionRegistrationGrpcService>();
        endpoints.MapGrpcService<ExecutionGrpcService>();

        // Hub-facing webhook service
        endpoints.MapGrpcService<HubWebhookGrpcService>();

        return endpoints;
    }
}
