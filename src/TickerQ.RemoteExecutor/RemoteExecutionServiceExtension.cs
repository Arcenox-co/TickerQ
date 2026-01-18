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
        
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            services.AddHttpClient("tickerq-callback", cfg =>
            {
                cfg.DefaultRequestHeaders.Add("X-Api-Key", tickerqRemoteExecutionOptions.ApiKey);
                cfg.DefaultRequestHeaders.Add("X-Api-Secret", tickerqRemoteExecutionOptions.ApiSecret);
            });
            services.AddSingleton<ITickerExecutionTaskHandler, TickerRemoteExecutionTaskHandler>();
            
            // Register options as singleton so background service can access it
            services.AddSingleton(tickerqRemoteExecutionOptions);
            
            // Register background service to sync remote functions
            services.AddHostedService<RemoteFunctionsSyncService>();
        };

        return tickerConfiguration;
    }
}