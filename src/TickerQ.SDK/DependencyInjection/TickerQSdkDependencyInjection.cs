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
    /// <summary>
    /// Default timeout for HTTP requests to Hub and Scheduler.
    /// </summary>
    private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(30);

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
                // ApiUri initially points to Hub, will be updated to Scheduler URL after sync
                ApiUri = new Uri(TickerQSdkConstants.HubBaseUrl)
            };

            configure(options);
            options.Validate();
            services.AddSingleton(options);

            // Register HTTP clients with IHttpClientFactory
            services.AddHttpClient(TickerQSdkHttpClient.HubClientName, client =>
            {
                client.BaseAddress = new Uri(TickerQSdkConstants.HubBaseUrl);
                client.Timeout = DefaultHttpTimeout;
                client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
                client.DefaultRequestHeaders.Add("X-Api-Secret", options.ApiSecret);
            });

            services.AddHttpClient(TickerQSdkHttpClient.SchedulerClientName, client =>
            {
                client.Timeout = DefaultHttpTimeout;
            });

            services.AddSingleton<TickerQSdkHttpClient>();
            services.AddSingleton<TickerQFunctionSyncService>();
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerQRemotePersistenceProvider<TTimeTicker, TCronTicker>>();
            services.AddHostedService<TickerQFunctionRegistrationHostedService>();
        };

        return builder;
    }
}
