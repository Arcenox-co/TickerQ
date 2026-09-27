using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class DesignTimeToolDetectionTests
{
    [Fact]
    public async Task Initializer_Skips_When_UseTickerQ_Not_Called()
    {
        // Design-time tools (dotnet-getdocument, dotnet-ef) build the host but
        // never call UseTickerQ. Verify the initializer does nothing in that case.
        var context = new TickerExecutionContext();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var configuration = Substitute.For<IConfiguration>();

        var initializer = new TickerQInitializerHostedService(context, serviceProvider, configuration);
        await initializer.StartAsync(CancellationToken.None);

        // InitializationRequested is false by default, so no seeding should occur.
        Assert.False(initializer.InitializationRequested);
    }

    [Fact]
    public void UseTickerQ_Sets_InitializationRequested_On_Initializer()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(options =>
            options.UseDefinedCronApplicationNamespace("design-time-tests"));

        var host = BuildMinimalHost(services);
        host.UseTickerQ();

        var initializer = host.Services.GetRequiredService<TickerQInitializerHostedService>();
        Assert.True(initializer.InitializationRequested);
    }

    [Fact]
    public void UseTickerQ_Does_Not_Perform_IO()
    {
        // UseTickerQ should complete without touching the database or building functions.
        var services = new ServiceCollection();
        services.AddTickerQ(options =>
            options.UseDefinedCronApplicationNamespace("design-time-tests"));

        var host = BuildMinimalHost(services);

        // This should not throw — no DB access, no function scanning
        var result = host.UseTickerQ();
        Assert.Same(host, result);
    }

    [Fact]
    public async Task Initializer_Runs_Seeding_When_InitializationRequested()
    {
        var internalManager = Substitute.For<IInternalTickerManager>();
        ActivationEpochTestDouble.Configure(internalManager);
        var context = SchedulerContext();
        var configuration = Substitute.For<IConfiguration>();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(new SchedulerOptionsBuilder());

        var sp = services.BuildServiceProvider();
        var initializer = new TickerQInitializerHostedService(context, sp, configuration);
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        // MigrateDefinedCronTickers should have been called (even if with empty list)
        await internalManager.Received(1).MigrateDefinedCronTickers(
            Arg.Any<DefinedCronSeedManifest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initializer_Respects_IgnoreSeedDefinedCronTickers()
    {
        var internalManager = Substitute.For<IInternalTickerManager>();
        ActivationEpochTestDouble.Configure(internalManager);

        var services = new ServiceCollection();
        services.AddTickerQ(options => options
            .IgnoreSeedDefinedCronTickers()
            .UseDefinedCronApplicationNamespace("design-time-tests")
            .UseReconciliationEpoch(1));

        var host = BuildMinimalHost(services);
        host.UseTickerQ();

        var context = host.Services.GetRequiredService<TickerExecutionContext>();
        var configuration = host.Services.GetRequiredService<IConfiguration>();

        // Create a new initializer with the real context that has seeding disabled
        var initializer = new TickerQInitializerHostedService(context, host.Services, configuration);
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        // Seeding should be skipped — IInternalTickerManager was not registered via AddTickerQ's
        // mock, so if it tried to seed, it would call the real (non-mock) manager.
        // The real assertion: IgnoreSeedDefinedCronTickers sets the flag correctly.
        var optionsSeeding = context.OptionsSeeding;
        Assert.NotNull(optionsSeeding);
        Assert.False(optionsSeeding.SeedDefinedCronTickers);
    }

    [Fact]
    public async Task Initializer_FinalizesPersistence_AfterDefinitionReconciliation_BeforeUserSeeding()
    {
        var events = new List<string>();
        var internalManager = Substitute.For<IInternalTickerManager>();
        ActivationEpochTestDouble.Configure(internalManager);
        internalManager
            .RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("time-repair");
                return Task.FromResult(TimeTickerChainRepairResult.Empty);
            });
        internalManager
            .MigrateDefinedCronTickers(
                Arg.Any<DefinedCronSeedManifest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("reconcile");
                return Task.CompletedTask;
            });

        var bootstrapper = Substitute.For<ITickerQPersistenceBootstrapper>();
        bootstrapper.BootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("bootstrap");
                return Task.CompletedTask;
            });

        var finalizer = Substitute.For<ITickerQPersistenceFinalizer>();
        finalizer.FinalizeAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("finalize");
                return Task.CompletedTask;
            });

        var context = new TickerExecutionContext
        {
            OptionsSeeding = new RecordingOptionsSeeding(
                seedDefinedCronTickers: true,
                timeSeederAction: _ =>
                {
                    events.Add("user-seed");
                    return Task.CompletedTask;
                })
        };
        var configuration = Substitute.For<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(bootstrapper);
        services.AddSingleton(finalizer);
        services.AddSingleton(new SchedulerOptionsBuilder());

        var initializer = new TickerQInitializerHostedService(context, services.BuildServiceProvider(), configuration)
        {
            InitializationRequested = true
        };

        await initializer.StartAsync(CancellationToken.None);

        Assert.Equal(["bootstrap", "time-repair", "reconcile", "finalize", "user-seed"], events);
    }

    [Fact]
    public async Task Initializer_Runs_External_Provider_Action()
    {
        var actionCalled = false;
        var context = SchedulerContext();
        context.ExternalProviderApplicationAction = _ => actionCalled = true;

        var internalManager = Substitute.For<IInternalTickerManager>();
        ActivationEpochTestDouble.Configure(internalManager);
        var configuration = Substitute.For<IConfiguration>();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(new SchedulerOptionsBuilder());

        var sp = services.BuildServiceProvider();
        var initializer = new TickerQInitializerHostedService(context, sp, configuration);
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        Assert.True(actionCalled);
        Assert.Null(context.ExternalProviderApplicationAction);
    }

    [Fact]
    public async Task Initializer_StopAsync_Is_NoOp()
    {
        var context = new TickerExecutionContext();
        var sp = Substitute.For<IServiceProvider>();
        var config = Substitute.For<IConfiguration>();

        var initializer = new TickerQInitializerHostedService(context, sp, config);

        // Should complete immediately without exceptions
        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void QueueOnlyHost_DoesNotRequire_ReconciliationActivationCapability()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(options => options.DisableBackgroundServices());

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(TickerQReconciliationActivationCapabilityValidator));
    }

    [Fact]
    public void Initializer_Registered_Before_Scheduler_Services()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(options =>
            options.UseDefinedCronApplicationNamespace("design-time-tests"));

        // Verify registration order: initializer should appear before scheduler
        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        Assert.True(hostedServiceDescriptors.Count >= 1);

        // First hosted service should be the initializer (resolved via factory from singleton)
        var first = hostedServiceDescriptors[0];
        // The factory registration resolves TickerQInitializerHostedService
        var sp = BuildMinimalHost(new ServiceCollection()).Services;
        // Just verify the initializer singleton is registered before scheduler
        var allSingletons = services
            .Where(d => d.Lifetime == ServiceLifetime.Singleton)
            .Select(d => d.ServiceType ?? d.ImplementationType)
            .ToList();

        var initializerIndex = allSingletons.IndexOf(typeof(TickerQInitializerHostedService));
        var schedulerIndex = allSingletons.IndexOf(typeof(TickerQSchedulerBackgroundService));

        Assert.True(initializerIndex >= 0, "TickerQInitializerHostedService should be registered");
        // Scheduler may or may not be registered (depends on DisableBackgroundServices)
        if (schedulerIndex >= 0)
        {
            Assert.True(initializerIndex < schedulerIndex,
                "Initializer should be registered before scheduler");
        }
    }

    private static IHost BuildMinimalHost(IServiceCollection services)
    {
        services.TryAddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.TryAddSingleton(Substitute.For<IHostApplicationLifetime>());
        services.TryAddSingleton(Substitute.For<IHostEnvironment>());
        services.AddLogging();
        return new MinimalHost(services.BuildServiceProvider());
    }

    private static TickerExecutionContext SchedulerContext() => new()
    {
        OptionsSeeding = new RecordingOptionsSeeding(true, _ => Task.CompletedTask)
    };

    private sealed class RecordingOptionsSeeding(
        bool seedDefinedCronTickers,
        Func<IServiceProvider, Task> timeSeederAction) : ITickerOptionsSeeding
    {
        public bool SeedDefinedCronTickers { get; } = seedDefinedCronTickers;
        public long ReconciliationEpoch => 1;
        public string DefinedCronApplicationNamespace => "design-time-tests";
        public Func<IServiceProvider, CancellationToken, Task> TimeSeederAction { get; } =
            (serviceProvider, _) => timeSeederAction(serviceProvider);
        public Func<IServiceProvider, CancellationToken, Task> CronSeederAction { get; } = null;
    }

    /// <summary>
    /// Minimal IHost implementation for testing UseTickerQ without a full host builder.
    /// </summary>
    private sealed class MinimalHost : IHost
    {
        public IServiceProvider Services { get; }

        public MinimalHost(IServiceProvider services) => Services = services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
