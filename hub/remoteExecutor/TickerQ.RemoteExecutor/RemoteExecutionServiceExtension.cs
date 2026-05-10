using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TickerQ.RemoteExecutor.GrpcServices;
using TickerQ.RemoteExecutor.Logging;
using TickerQ.RemoteExecutor.TunnelClient;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Instrumentation;
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

        // Expose function → owning node lookup so tickers persist the SDK node name at creation.
        // Function names on tickers are stored qualified ("bare@node") to support multiple
        // SDKs hosting the same bare name. The resolver splits on '@' first so qualified
        // values resolve trivially; bare values fall back to the registry for legacy paths.
        TickerFunctionProvider.FunctionNodeResolver = (name) =>
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var atIdx = name.IndexOf('@');
            if (atIdx > 0 && atIdx < name.Length - 1) return name.Substring(atIdx + 1);
            return RemoteFunctionRegistry.GetNodeName(name);
        };

        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            services.AddSingleton<TickerRemoteExecutionTaskHandler>();
            services.AddSingleton<ITickerExecutionTaskHandler, TickerExecutionTaskHandlerRouter>();

            // Register options as singleton so background service can access it
            services.AddSingleton(tickerqRemoteExecutionOptions);

            // Register background service to sync remote functions (also injectable for webhooks)
            services.AddSingleton<RemoteFunctionsSyncService>();
            services.AddHostedService(sp => sp.GetRequiredService<RemoteFunctionsSyncService>());

            // gRPC server for Hub dashboard queries
            services.AddGrpc(options =>
            {
                options.Interceptors.Add<DashboardAuthInterceptor>();
            });
            services.AddSingleton<DashboardAuthInterceptor>();
            services.AddScoped<DashboardGrpcService<TTimeTicker, TCronTicker>>();
            services.AddScoped<DashboardOperationGrpcService<TTimeTicker, TCronTicker>>();
            // Hub → scheduler webhook (resync, remove function) — replaces /webhooks/hub HTTP endpoint.
            services.AddScoped<HubWebhookGrpcService>();

            // Worker stream — single bidi gRPC SDK→Scheduler stream for both function dispatch
            // (Scheduler→SDK direction) and ticker CRUD/status (SDK→Scheduler direction).
            services.AddSingleton<WorkerStreamRegistry>();
            services.AddSingleton<IRemotePayloadLoader, RemotePayloadLoader<TTimeTicker, TCronTicker>>();
            services.AddScoped<SchedulerWorkerServiceImpl<TTimeTicker, TCronTicker>>();

            // Auto-pauses cron tickers whose target SDK node is offline. Uses
            // the WorkerStreamRegistry's Changed event (debounced) plus a 60s
            // safety-net sweep. See WorkerStreamPauseMonitor for full logic.
            services.AddHostedService<WorkerStreamPauseMonitor<TTimeTicker, TCronTicker>>();

            // Per-execution log buffer — populated by SchedulerWorkerServiceImpl when an
            // SDK forwards LogLine frames, read by the dashboard tunnel handler when the
            // log panel hydrates. Sweeper drops idle keys after 30 min so the buffer
            // never grows unboundedly across the process lifetime.
            services.AddSingleton<TickerLogRingBuffer>();
            services.AddHostedService<TickerLogRingBufferSweeper>();

            // Tunnel client. The customer never opens an inbound port — the Hub reaches the
            // node over the persistent outbound tunnel; works behind NAT/firewalls/Docker/K8s
            // out of the box. The earlier callback-mode escape hatch was removed (production
            // lockdown — the tunnel is the only supported path now).
            var tunnelOptions = new TunnelClientOptions
            {
                HubUrl = tickerqRemoteExecutionOptions.HubGrpcEndpointUrl,
                ApiKey = tickerqRemoteExecutionOptions.ApiKey ?? string.Empty,
                NodeName = tickerqRemoteExecutionOptions.NodeName,
                ApplicationUrl = tickerqRemoteExecutionOptions.ApplicationUrl,
            };
            services.AddSingleton(tunnelOptions);
            services.AddSingleton<TunnelClientHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<TunnelClientHostedService>());

            // Replace the No-Op notification sender with the tunnel-bound one so that
            // InternalTickerManager / TickerManager calls (UpdateTimeTickerNotifyAsync etc.)
            // automatically push live updates to the Hub dashboard via SignalR.
            services.RemoveAll<ITickerQNotificationHubSender>();
            services.AddSingleton<ITickerQNotificationHubSender, TunnelTickerQNotificationHubSender>();

            // Decorate ITickerQInstrumentation so each lifecycle hook (Picked up,
            // Started, Cancelled, Skipped, …) also pushes a scheduler-source log
            // line — gives the dashboard panel a fuller view of the scheduler-side
            // activity around the ticker, not just the SDK-side execution.
            var existing = services.LastOrDefault(d => d.ServiceType == typeof(ITickerQInstrumentation));
            if (existing != null)
            {
                services.Remove(existing);
                services.AddSingleton<ITickerQInstrumentation>(sp =>
                {
                    ITickerQInstrumentation inner =
                        existing.ImplementationInstance is ITickerQInstrumentation inst
                            ? inst
                            : existing.ImplementationFactory != null
                                ? (ITickerQInstrumentation)existing.ImplementationFactory(sp)
                                : (ITickerQInstrumentation)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType!);
                    return new TickerLogForwardingInstrumentation(
                        inner,
                        sp.GetService<ITickerQNotificationHubSender>() as TunnelTickerQNotificationHubSender);
                });
            }

            // Auto-map dashboard gRPC endpoints during pipeline build via an
            // IStartupFilter — same pattern TickerQ.Dashboard uses to inject its
            // own routes. The filter calls UseEndpoints inside its Configure
            // delegate, which creates an endpoint scope where MapGrpcService's
            // data-source registration takes effect. With this filter wired,
            // the customer's Program.cs no longer needs `app.MapTickerQDashboardGrpc()`.
            services.AddSingleton<IStartupFilter, DashboardGrpcStartupFilter<TTimeTicker, TCronTicker>>();
        };

        // AddDashboard() registers via DashboardServiceAction, which runs AFTER
        // ExternalProviderConfigServiceAction in TickerQ's startup. Its Replace would
        // overwrite our Tunnel sender with the in-process one. Re-register here so
        // the Tunnel sender always wins regardless of AddDashboard ordering.
        tickerConfiguration.DashboardServiceAction += services =>
        {
            services.RemoveAll<ITickerQNotificationHubSender>();
            services.AddSingleton<ITickerQNotificationHubSender, TunnelTickerQNotificationHubSender>();
        };

        return tickerConfiguration;
    }

    /// <summary>
    /// Auto-maps the dashboard gRPC services into the host's endpoint pipeline. Lets the
    /// customer's Program.cs skip the explicit <c>app.MapTickerQDashboardGrpc()</c> call —
    /// the only required line is <c>app.UseTickerQ()</c> from the TickerQ host pipeline.
    ///
    /// Mirrors <c>TickerQ.Dashboard.DashboardStartupFilter</c>'s pattern: invoke
    /// <c>UseEndpoints</c> inside <c>Configure</c> so the endpoint scope is alive when
    /// gRPC's <c>MapGrpcService</c> writes to the route builder's data sources.
    /// </summary>
    internal sealed class DashboardGrpcStartupFilter<TTimeTicker, TCronTicker> : IStartupFilter
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                // UseRouting must precede UseEndpoints in the same scope, otherwise
                // ASP.NET throws InvalidOperationException at startup.
                //
                // Calling UseRouting twice (once here, once in the user's pipeline
                // via MapControllers / explicit UseRouting) creates two routing
                // scopes. Endpoints registered in each scope are matched in their
                // own scope — our gRPC endpoints land in this filter's scope
                // (auth via DashboardAuthInterceptor, no host-pipeline auth needed),
                // user's endpoints land in their scope. Standard host pipelines
                // work; advanced setups that depend on host middleware running
                // for our gRPC paths must call MapTickerQDashboardGrpc themselves
                // and not register this filter (out of scope for default).
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                    endpoints.MapTickerQDashboardGrpc<TTimeTicker, TCronTicker>());
                next(app);
            };
    }
}
