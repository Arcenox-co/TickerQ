using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            services.AddHttpClient("tickerq-hub", cfg =>
            {
                cfg.BaseAddress = new Uri(tickerqRemoteExecutionOptions.HubEndpointUrl);
                cfg.DefaultRequestHeaders.Add("X-Api-Key", tickerqRemoteExecutionOptions.ApiKey);
                cfg.DefaultRequestHeaders.Add("X-Api-Secret", tickerqRemoteExecutionOptions.ApiSecret);
            });
            services.AddHttpClient("tickerq-callback");
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
