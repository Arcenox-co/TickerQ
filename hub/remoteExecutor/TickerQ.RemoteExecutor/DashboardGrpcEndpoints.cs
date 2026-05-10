using TickerQ.RemoteExecutor.GrpcServices;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities.Entities;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Maps the dashboard gRPC service onto the host's endpoint routes.
/// Call from <c>app.UseEndpoints(...)</c> or <c>app.MapGrpcService&lt;...&gt;()</c>-style composition.
/// </summary>
public static class DashboardGrpcEndpoints
{
    /// <summary>
    /// Maps the Hub dashboard gRPC service using the default ticker entity types.
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQDashboardGrpc(this IEndpointRouteBuilder endpoints)
        => endpoints.MapTickerQDashboardGrpc<TimeTickerEntity, CronTickerEntity>();

    /// <summary>
    /// Maps the Hub dashboard gRPC service for the specified ticker entity types.
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQDashboardGrpc<TTimeTicker, TCronTicker>(this IEndpointRouteBuilder endpoints)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
        endpoints.MapGrpcService<DashboardGrpcService<TTimeTicker, TCronTicker>>();
        endpoints.MapGrpcService<DashboardOperationGrpcService<TTimeTicker, TCronTicker>>();
        endpoints.MapGrpcService<HubWebhookGrpcService>();
        // Worker-stream service. SDKs open OpenWorkerStream against this for both function
        // dispatch and ticker CRUD over a single bidi gRPC stream.
        endpoints.MapGrpcService<SchedulerWorkerServiceImpl<TTimeTicker, TCronTicker>>();
        return endpoints;
    }
}
