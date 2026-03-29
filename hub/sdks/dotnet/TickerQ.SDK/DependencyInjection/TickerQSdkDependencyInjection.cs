using Microsoft.Extensions.DependencyInjection;
using TickerQ.SDK.Client;
using TickerQ.SDK.Infrastructure;
using TickerQ.SDK.HostedServices;
using TickerQ.SDK.Persistence;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.SDK.DependencyInjection;

public static class TickerQSdkDependencyInjection
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerQSdk<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker, TCronTicker> builder, Action<TickerSdkOptions> configure)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        builder.DisableBackgroundServices();
        builder.IgnoreSeedDefinedCronTickers();
        builder.ExternalProviderConfigServiceAction += (services) =>
        {
            var options = new TickerSdkOptions
            {
                ApiUri = new Uri(TickerQSdkConstants.HubBaseUrl)
            };

            configure(options);
            options.Validate();
            services.AddSingleton(options);

            // gRPC interceptor (HMAC signing for all outgoing gRPC calls)
            services.AddSingleton<TickerQGrpcClientInterceptor>();

            // Hub gRPC channel (to hub.tickerq.net)
            services.AddSingleton<TickerQHubGrpcChannelProvider>();

            // Scheduler gRPC channel (to RemoteExecutor, initialized after hub sync)
            services.AddSingleton<TickerQGrpcChannelProvider>();

            // gRPC client (wraps all service stubs)
            services.AddSingleton<TickerQSdkGrpcClient>();

            // Services
            services.AddSingleton<TickerQFunctionSyncService>();
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerQRemotePersistenceProvider<TTimeTicker, TCronTicker>>();

            // Hosted services
            services.AddHostedService<TickerQFunctionRegistrationHostedService>();
            services.AddHostedService<TickerQExecutionStreamService>();
        };

        return builder;
    }
}
