using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.RemoteExecutor.TunnelClient;

public static class TunnelClientExtension
{
    /// <summary>
    /// Registers a background service that maintains a reverse tunnel to the Hub,
    /// letting the Hub reach this scheduler without a public URL.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerTunnel<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> builder,
        Action<TunnelClientOptions> configure)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var options = new TunnelClientOptions();
        configure(options);

        builder.ExternalProviderConfigServiceAction += services =>
        {
            services.AddSingleton(options);
            services.AddHostedService<TunnelClientHostedService>();
        };

        return builder;
    }
}
