using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.SDK.HostedServices;
using TickerQ.SDK.Infrastructure;
using TickerQ.SDK.Logging;
using TickerQ.SDK.Persistence;
using TickerQ.SDK.WorkerStream;
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
            var options = new TickerSdkOptions();
            configure(options);
            options.Validate();
            services.AddSingleton(options);

            // Boot-time function-sync runs over gRPC (HubService.SyncNodesFunctions on
            // grpc.hub.tickerq.net). The response carries the env's ApplicationUrl
            // (where the worker stream dials) and WebhookSignature (HMAC for the
            // SDK↔Scheduler plane). No HTTP client involved.
            services.AddSingleton<TickerQFunctionSyncService>();
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerQRemotePersistenceProvider<TTimeTicker, TCronTicker>>();

            // Per-execution log forwarding. Singleton queue is the shared rendezvous
            // between the ILogger provider (writes) and WorkerStreamHostedService (drains).
            // Logger provider is additive — it sits alongside the host's existing console /
            // AppInsights providers so customer logs still go to their normal sinks.
            services.AddSingleton<TickerExecutionLogQueue>();
            services.AddLogging(b => b.Services.AddSingleton<ILoggerProvider>(sp =>
                new TickerExecutionLoggerProvider(
                    sp.GetRequiredService<TickerExecutionLogQueue>(),
                    sp.GetRequiredService<TickerSdkOptions>())));

            // Hosted services start in registration order. Function-sync MUST run before the
            // worker stream so ApiUri + WebhookSignature are populated when the stream opens.
            services.AddHostedService<TickerQFunctionRegistrationHostedService>();
            // SDK→Hub control stream — Hub still pushes TriggerResync / RemoveFunction to SDK.
            services.AddHostedService<TickerQSdkControlClient>();

            // Worker stream — single bidi connection that carries both directions of
            // SDK↔Scheduler traffic. Must be a singleton so the persistence provider can
            // resolve the same instance and call SendAndAwaitOperationAsync on it.
            services.AddSingleton<WorkerStreamHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<WorkerStreamHostedService>());
        };

        return builder;
    }
}
