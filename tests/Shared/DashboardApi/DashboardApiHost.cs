using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Tests.Shared.DashboardApi;

/// <summary>
/// Runs only when TICKERQ_DASHBOARD_API_TESTS=1: every class starts its database in Docker, so the
/// suite is opt-in. scripts/tests/run-dashboard-api-tests.sh sets it and runs every database.
/// </summary>
public sealed class DashboardApiFactAttribute : FactAttribute
{
    internal static bool Enabled => string.Equals(
        Environment.GetEnvironmentVariable("TICKERQ_DASHBOARD_API_TESTS"), "1", StringComparison.Ordinal);

    public DashboardApiFactAttribute()
    {
        if (!Enabled)
            Skip = "Set TICKERQ_DASHBOARD_API_TESTS=1 (and start Docker) to run the Dashboard API suite against real databases.";
    }
}

/// <summary>One test's isolated store on a database server: its own database, or an emptied one.</summary>
public abstract class DashboardApiStore : IAsyncDisposable
{
    /// <summary>Registers this store as the node's operational store.</summary>
    public abstract void Configure(TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> tickerq);

    /// <summary>Creates whatever the store needs before the node starts, such as tables.</summary>
    public virtual Task PrepareAsync(IServiceProvider services) => Task.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A database server in Docker that hands each test an isolated store.</summary>
public interface IDashboardApiServer
{
    Task<DashboardApiStore> CreateStoreAsync();
}

/// <summary>How <see cref="DashboardApiHost.StartAsync"/> builds the node.</summary>
public sealed class DashboardApiHostOptions
{
    public bool ActiveLicense { get; init; } = true;
    public Action<SchedulerOptionsBuilder> Scheduler { get; init; }
    public Action<IServiceCollection> Services { get; init; }
}

public static class DashboardApiHost
{
    public const string Namespace = "dashboard-api";
    public const string Api = "/tickerq/dashboard/api";

    /// <summary>Builds and starts a TickerQ node with the Dashboard on a test server over <paramref name="store"/>.</summary>
    public static async Task<WebApplication> StartAsync(DashboardApiStore store, DashboardApiHostOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        // TICKERQ_DASHBOARD_API_LOGS=1 prints the node's warnings and errors, such as the exception behind a 500;
        // =info or =debug also prints TickerQ's own scheduling decisions at that level.
        var logs = Environment.GetEnvironmentVariable("TICKERQ_DASHBOARD_API_LOGS");
        if (logs is "1" or "info" or "debug")
            builder.Logging.AddSimpleConsole(console => console.TimestampFormat = "HH:mm:ss.fff ")
                .SetMinimumLevel(LogLevel.Warning)
                .AddFilter("TickerQ", logs switch { "debug" => LogLevel.Debug, "info" => LogLevel.Information, _ => LogLevel.Warning });
        options.Services?.Invoke(builder.Services);
        builder.Services.AddTickerQ(tickerq =>
        {
            tickerq.UseApplicationNamespace(Namespace)
                .UseReconciliationEpoch(1)
                .ConfigureScheduler(scheduler =>
                {
                    scheduler.NodeIdentifier = "dashboard-api";
                    scheduler.DetailedLoggingEnabled = Environment.GetEnvironmentVariable("TICKERQ_DASHBOARD_API_LOGS") is "info" or "debug";
                    options.Scheduler?.Invoke(scheduler);
                });
            // Logs persist to the store under test, and integration settings are stored there too.
            tickerq.ConfigureExecutionLogs(logs => logs.PersistToDatabase = true);
            store.Configure(tickerq);
            tickerq.AddIntegrations();
            tickerq.AddDashboard(dashboard => dashboard
                .AllowAnonymousDashboard()
                .AllowIntegrationManagement(_ => true));
        });
        if (options.ActiveLicense)
            TestLicense.UseActive(builder.Services);

        var app = builder.Build();
        await store.PrepareAsync(app.Services);
        app.UseTickerQ();
        await app.StartAsync();
        return app;
    }
}

/// <summary>Publishes an in-force license in place of TickerQ's license service.</summary>
public static class TestLicense
{
    // A real certificate is signed offline, so the hosts publish the same in-force state the
    // license service would publish for one. It takes that service's place, keeping the startup
    // order: the state is published before any scheduler, recovery, or Dashboard write reads it.
    public static void UseActive(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IHostedService) &&
                services[i].ImplementationType?.Name == "TickerQLicenseHostedService")
            {
                services[i] = ServiceDescriptor.Singleton<IHostedService, ActiveTestLicense>();
                return;
            }
        }
        throw new InvalidOperationException("TickerQ's license hosted service was not registered.");
    }

    private sealed class ActiveTestLicense(TickerQLicenseStateProvider state) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            state.Publish(TickerQLicenseState.ForCertificate(
                TickerQLicenseStatus.Active,
                isEvaluation: false,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "TickerQ Tests",
                "Business",
                "Full",
                now.AddDays(-1),
                now.AddYears(1),
                daysRemaining: 365,
                schemaVersion: 4,
                algorithm: "Ed25519",
                keyId: "production-2026-01",
                anchoredMinorLine: "5.x"));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
