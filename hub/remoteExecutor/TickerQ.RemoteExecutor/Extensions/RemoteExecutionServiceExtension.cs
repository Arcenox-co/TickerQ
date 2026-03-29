using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.RemoteExecutor.Client;
using TickerQ.RemoteExecutor.Execution;
using TickerQ.RemoteExecutor.GrpcServices;
using TickerQ.RemoteExecutor.Security;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.RemoteExecutor;

public static class RemoteExecutionServiceExtension
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerRemoteExecutor<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, Action<TickerQRemoteExecutionOptions> optionsAction)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var tickerqRemoteExecutionOptions = new TickerQRemoteExecutionOptions();

        optionsAction(tickerqRemoteExecutionOptions);
        tickerqRemoteExecutionOptions.Validate();

        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            // gRPC services (SDK-facing + Hub webhook)
            services.AddGrpc(options =>
            {
                options.Interceptors.Add<TickerQGrpcAuthInterceptor>();
                options.MaxReceiveMessageSize = 16 * 1024 * 1024;
            });

            // Auth interceptors
            services.AddSingleton<TickerQGrpcAuthInterceptor>();
            services.AddSingleton<TickerQHubGrpcClientInterceptor>();

            // Hub gRPC channel (to hub.tickerq.net)
            services.AddSingleton<TickerQHubGrpcChannelProvider>();

            // Execution
            services.AddSingleton<GrpcNodeConnectionManager>();
            services.AddSingleton<TickerRemoteExecutionTaskHandler>();
            services.AddSingleton<ITickerExecutionTaskHandler, TickerExecutionTaskHandlerRouter>();

            // Register options as singleton so background service can access it
            services.AddSingleton(tickerqRemoteExecutionOptions);

            // Register background service to sync remote functions (also injectable for webhooks)
            services.AddSingleton<RemoteFunctionsSyncService>();
            services.AddHostedService(sp => sp.GetRequiredService<RemoteFunctionsSyncService>());
        };

        return tickerConfiguration;
    }
}
