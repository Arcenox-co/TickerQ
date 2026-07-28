using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using Xunit;

namespace TickerQ.Tests;

public class JobRetentionRegistrationTests
{
    private static bool HasRetentionHostedService(IServiceCollection services)
        => services.Any(d => d.ImplementationType == typeof(TickerQRetentionBackgroundService));

    [Fact]
    public void Retention_NotRegistered_WhenNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTickerQ();

        Assert.False(HasRetentionHostedService(services));
    }

    [Fact]
    public void Retention_Registered_WhenConfigured_AndBackgroundServicesEnabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTickerQ(o => o.ConfigureJobRetention(r => r.DeleteSucceededAfter = TimeSpan.FromDays(1)));

        Assert.True(HasRetentionHostedService(services));
        Assert.Contains(services, d => d.ServiceType == typeof(JobRetentionOptions));
    }

    [Fact]
    public void Retention_NotRegistered_WhenBackgroundServicesDisabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTickerQ(o =>
        {
            o.DisableBackgroundServices();
            o.ConfigureJobRetention(r => r.DeleteSucceededAfter = TimeSpan.FromDays(1));
        });

        Assert.False(HasRetentionHostedService(services));
    }

    [Fact]
    public void Retention_RegisteredAfterScheduler_SoItStopsFirst()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTickerQ(o => o.ConfigureJobRetention(r => r.DeleteSucceededAfter = TimeSpan.FromDays(1)));

        var indexed = services.Select((d, i) => (d, i)).ToList();

        var retentionIndex = indexed.First(x => x.d.ImplementationType == typeof(TickerQRetentionBackgroundService)).i;
        // The scheduler background service is registered as a singleton (concrete ImplementationType)
        // just before its hosted-service factory. Retention registered after it stops first on shutdown.
        var schedulerIndex = indexed.First(x =>
            x.d.ImplementationType == typeof(TickerQSchedulerBackgroundService)).i;

        // Hosted services stop in reverse registration order; retention must be registered AFTER the
        // scheduler so it stops BEFORE the scheduler drains in-flight work.
        Assert.True(retentionIndex > schedulerIndex);
    }
}
