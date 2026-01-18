using Microsoft.Extensions.DependencyInjection;
using TickerQ.SDK.Client;
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
                ApiUri = new Uri("https://hub-api.tickerq.net")
            };
            
            configure(options);
            services.AddSingleton(options);
            services.AddSingleton<TickerQSdkHttpClient>();
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerQRemotePersistenceProvider<TTimeTicker, TCronTicker>>();
            services.AddHostedService<TickerQFunctionRegistrationHostedService>();
        };
        
        return builder;
    }
}
