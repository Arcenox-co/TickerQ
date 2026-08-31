using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces.Managers;
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
        Assert.DoesNotContain(services,
            d => d.ImplementationType == typeof(TickerQRetentionCapabilityValidator));
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

    [Fact]
    public async Task UnsupportedProvider_FailsBeforeSchedulerHostedServiceStarts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(o => o.ConfigureJobRetention(r => r.DeleteSucceededAfter = TimeSpan.FromDays(1)));
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(false);
        services.Replace(ServiceDescriptor.Singleton(manager));

        var probe = new SchedulerStartProbe();
        var schedulerRegistration = services.Select((descriptor, index) => (descriptor, index))
            .First(x => x.descriptor.ServiceType == typeof(IHostedService) &&
                        x.index > services.Select((d, i) => (d, i)).First(y =>
                            y.d.ImplementationType == typeof(TickerQSchedulerBackgroundService)).i);
        services[schedulerRegistration.index] = ServiceDescriptor.Singleton<IHostedService>(probe);

        using var host = new HostBuilder().ConfigureServices(collection =>
        {
            foreach (var descriptor in services)
                collection.Add(descriptor);
        }).Build();

        await Assert.ThrowsAsync<NotSupportedException>(() => host.StartAsync());
        Assert.False(probe.Started);
    }

    private sealed class SchedulerStartProbe : IHostedService
    {
        public bool Started { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
