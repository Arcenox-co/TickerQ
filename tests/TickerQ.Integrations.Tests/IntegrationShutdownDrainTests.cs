using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.DependencyInjection;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Integrations.Infrastructure;

namespace TickerQ.Integrations.Tests;

/// <summary>
/// Shutdown used to cancel the alert worker immediately, discarding anything still queued.
/// It now drains the queue for a bounded window and stops after the scheduler's producers.
/// </summary>
public class IntegrationShutdownDrainTests
{
    [Fact]
    public async Task StopAsync_delivers_alerts_that_were_already_queued()
    {
        var configuration = IntegrationTests.CreateConfiguration(new());
        await configuration.SaveAsync(IntegrationTests.ValidConfiguration(), default);
        var handler = new SlowHandler(TimeSpan.FromMilliseconds(100));
        var options = new IntegrationOptions();
        using var engine = new IntegrationEngine(options, configuration,
            new(new IntegrationTests.ClientFactory(handler), options), NullLogger<IntegrationEngine>.Instance);

        // Distinct functions so the per-function cooldown does not suppress any of them.
        for (var i = 0; i < 5; i++)
            engine.Notify(new() { Kind = "failed", Function = $"Job{i}" });

        await engine.StartAsync(default);
        await engine.StopAsync(default);

        Assert.Equal(5, handler.Calls);
    }

    [Fact]
    public async Task StopAsync_gives_up_after_the_drain_timeout()
    {
        var configuration = IntegrationTests.CreateConfiguration(new());
        await configuration.SaveAsync(IntegrationTests.ValidConfiguration(), default);
        var handler = new SlowHandler(Timeout.InfiniteTimeSpan);
        var options = new IntegrationOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200) };
        using var engine = new IntegrationEngine(options, configuration,
            new(new IntegrationTests.ClientFactory(handler), options), NullLogger<IntegrationEngine>.Instance);
        engine.Notify(new() { Kind = "failed", Function = "Stuck" });

        await engine.StartAsync(default);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var watch = Stopwatch.StartNew();
        await engine.StopAsync(default);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Shutdown took {watch.Elapsed}.");
    }

    [Fact]
    public async Task StopAsync_respects_the_host_shutdown_deadline()
    {
        var configuration = IntegrationTests.CreateConfiguration(new());
        await configuration.SaveAsync(IntegrationTests.ValidConfiguration(), default);
        var handler = new SlowHandler(Timeout.InfiniteTimeSpan);
        var options = new IntegrationOptions { ShutdownDrainTimeout = TimeSpan.FromMinutes(1) };
        using var engine = new IntegrationEngine(options, configuration,
            new(new IntegrationTests.ClientFactory(handler), options), NullLogger<IntegrationEngine>.Instance);
        engine.Notify(new() { Kind = "failed", Function = "Stuck" });

        await engine.StartAsync(default);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var hostDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();
        await engine.StopAsync(hostDeadline.Token);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Shutdown took {watch.Elapsed}.");
    }

    [Fact]
    public async Task Zero_drain_timeout_stops_without_waiting()
    {
        var configuration = IntegrationTests.CreateConfiguration(new());
        await configuration.SaveAsync(IntegrationTests.ValidConfiguration(), default);
        var handler = new SlowHandler(Timeout.InfiniteTimeSpan);
        var options = new IntegrationOptions { ShutdownDrainTimeout = TimeSpan.Zero };
        using var engine = new IntegrationEngine(options, configuration,
            new(new IntegrationTests.ClientFactory(handler), options), NullLogger<IntegrationEngine>.Instance);
        engine.Notify(new() { Kind = "failed", Function = "Stuck" });

        await engine.StartAsync(default);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var watch = Stopwatch.StartNew();
        await engine.StopAsync(default);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Shutdown took {watch.Elapsed}.");
    }

    [Fact]
    public void Engine_is_registered_before_scheduler_services_so_it_stops_after_them()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(options => options
            .UseApplicationNamespace("integration-drain-order")
            .AddIntegrations());

        var hostedTypes = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();
        using var provider = services.BuildServiceProvider();
        var first = hostedTypes[0];
        var resolved = first.ImplementationFactory!(provider);

        Assert.IsType<IntegrationEngine>(resolved);
        Assert.Contains(hostedTypes.Skip(1), d =>
            (d.ImplementationType?.Name ?? "").Contains("StaleJobRecovery", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void Drain_timeout_outside_zero_to_sixty_seconds_is_rejected(int seconds)
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<ArgumentException>(() => services.AddTickerQ(options => options
            .UseApplicationNamespace("integration-drain-range")
            .AddIntegrations(i => i.ShutdownDrainTimeout = TimeSpan.FromSeconds(seconds))));

        Assert.Contains("ShutdownDrainTimeout", error.Message);
    }

    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(delay, cancellationToken);
            Interlocked.Increment(ref Calls);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
