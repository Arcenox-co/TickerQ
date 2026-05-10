using Microsoft.Extensions.DependencyInjection;
using TickerQ.RemoteExecutor.GrpcServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Standalone registration for the Hub dashboard gRPC service. Use alongside
/// <see cref="RemoteExecutionServiceExtension.AddTickerRemoteExecutor"/> — the
/// HMAC signature this dashboard validates against is auto-supplied by the Hub
/// at runtime (initial value via gRPC sync, rotations via tunnel push).
/// </summary>
public static class DashboardGrpcServiceExtension
{
    public static TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> AddTickerDashboardGrpc(
        this TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> tickerConfiguration)
        => AddTickerDashboardGrpc<TimeTickerEntity, CronTickerEntity>(tickerConfiguration);

    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerDashboardGrpc<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            // Ensure an options instance exists for the dashboard gRPC pipeline.
            // AddTickerRemoteExecutor (when wired) registers the same singleton
            // first; this branch only fires in the standalone-without-executor
            // case, which then has no Hub to source a signature from anyway.
            var hasOptions = services.Any(d => d.ServiceType == typeof(TickerQRemoteExecutionOptions));
            if (!hasOptions)
            {
                services.AddSingleton(new TickerQRemoteExecutionOptions());
            }

            services.AddGrpc(options =>
            {
                options.Interceptors.Add<DashboardAuthInterceptor>();
            });
            services.AddSingleton<DashboardAuthInterceptor>();
            services.AddScoped<DashboardGrpcService<TTimeTicker, TCronTicker>>();
            services.AddScoped<DashboardOperationGrpcService<TTimeTicker, TCronTicker>>();
        };

        return tickerConfiguration;
    }
}
