using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TickerQ.Notifications.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Notifications;

namespace TickerQ.Notifications.DependencyInjection;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds dashboard-managed notifications through the TickerQ builder's package-registration pipeline.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddNotifications<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<NotificationOptions>? configure = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        ArgumentNullException.ThrowIfNull(tickerConfiguration);
        var options = new NotificationOptions();
        configure?.Invoke(options);
        if (options.AllowedHosts == null || options.AllowedHosts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("AllowedHosts must contain valid host names, or be an empty list.");
        if (options.QueueCapacity is < 1 or > 100000 || options.RequestTimeout < TimeSpan.FromSeconds(1) || options.RequestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentException("Use a queue capacity of 1–100,000 and request timeout of 1–60 seconds.");
        if (string.IsNullOrWhiteSpace(options.ApplicationName) || options.ApplicationName.Length > 100 || options.EnvironmentName == null || options.EnvironmentName.Length > 100)
            throw new ArgumentException("Application and environment labels must be at most 100 characters.");
        if (options.DashboardUrl != null && (!Uri.TryCreate(options.DashboardUrl, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http") || url.UserInfo.Length > 0))
            throw new ArgumentException("DashboardUrl must be an absolute HTTP(S) URL without credentials.");
        tickerConfiguration.ExternalProviderConfigServiceAction += services => RegisterServices(services, options);
        return tickerConfiguration;
    }

    private static void RegisterServices(IServiceCollection services, NotificationOptions options)
    {
        if (services.Any(s => s.ServiceType == typeof(NotificationOptions)))
            throw new InvalidOperationException("TickerQ notifications are already registered.");
        if (services.Any(s => s.ServiceType == typeof(FailureWebhookOptions)))
            throw new InvalidOperationException("Use either AddNotifications or NotifyFailuresViaWebhook. Move the existing webhook into a notification destination before enabling the optional package.");
        services.AddSingleton(options);
        services.AddDataProtection();
        services.AddLogging();
        services.AddHttpClient(NotificationTransport.ClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => WebhookSecurity.CreateHandler(options))
            .RemoveAllLoggers(); // Webhook URLs and custom headers can contain credentials.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationPayloadFormatter, SlackFormatter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationPayloadFormatter, TeamsFormatter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationPayloadFormatter, WebhookFormatter>());
        services.AddSingleton<NotificationConfigurationService>();
        services.AddSingleton<NotificationTransport>();
        services.AddSingleton<NotificationEngine>();
        services.Replace(ServiceDescriptor.Singleton<ITickerQFailureNotifier>(sp => sp.GetRequiredService<NotificationEngine>()));
        services.AddSingleton<ITickerNotificationAdminService>(sp => sp.GetRequiredService<NotificationEngine>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<NotificationEngine>());
    }
}
