using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Integrations.Monitoring;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.DependencyInjection;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds dashboard-managed integrations through the TickerQ builder's package-registration pipeline.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddIntegrations<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<IntegrationOptions>? configure = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        ArgumentNullException.ThrowIfNull(tickerConfiguration);
        var options = new IntegrationOptions();
        configure?.Invoke(options);
        if (options.AllowedHosts == null || options.AllowedHosts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("AllowedHosts must contain valid host names, or be an empty list.");
        if (options.QueueCapacity is < 1 or > 100000 || options.RequestTimeout < TimeSpan.FromSeconds(1) || options.RequestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentException("Use a queue capacity of 1–100,000 and request timeout of 1–60 seconds.");
        if (string.IsNullOrWhiteSpace(options.ApplicationName) || options.ApplicationName.Length > 100 || options.EnvironmentName == null || options.EnvironmentName.Length > 100)
            throw new ArgumentException("Application and environment labels must be at most 100 characters.");
        if (options.DashboardUrl != null && (!Uri.TryCreate(options.DashboardUrl, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http") || url.UserInfo.Length > 0))
            throw new ArgumentException("DashboardUrl must be an absolute HTTP(S) URL without credentials.");
        if (options.MonitoringInterval < TimeSpan.FromSeconds(5) || options.MonitoringInterval > TimeSpan.FromMinutes(5))
            throw new ArgumentException("MonitoringInterval must be between 5 seconds and 5 minutes.");
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            RegisterServices(services, options);
            services.AddSingleton<IMonitoringExecutionSource, MonitoringExecutionSource<TTimeTicker, TCronTicker>>();
            services.AddHostedService<IntegrationMonitoringService>();
        };
        return tickerConfiguration;
    }

    private static void RegisterServices(IServiceCollection services, IntegrationOptions options)
    {
        if (services.Any(s => s.ServiceType == typeof(IntegrationOptions)))
            throw new InvalidOperationException("TickerQ integrations are already registered.");
        services.AddSingleton(options);
        services.AddDataProtection();
        services.AddLogging();
        services.AddHttpClient(IntegrationTransport.ClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => WebhookSecurity.CreateHandler(options))
            .RemoveAllLoggers(); // Webhook URLs and custom headers can contain credentials.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIntegrationPayloadFormatter, SlackFormatter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIntegrationPayloadFormatter, TeamsFormatter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIntegrationPayloadFormatter, WebhookFormatter>());
        services.AddSingleton<IntegrationConfigurationService>();
        services.AddSingleton<MonitoringStateStore>();
        services.AddSingleton<IntegrationTransport>();
        services.AddSingleton<IntegrationEngine>();
        services.Replace(ServiceDescriptor.Singleton<ITickerQFailureNotifier>(sp => sp.GetRequiredService<IntegrationEngine>()));
        services.AddSingleton<ITickerIntegrationAdminService>(sp => sp.GetRequiredService<IntegrationEngine>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IntegrationEngine>());
    }
}
