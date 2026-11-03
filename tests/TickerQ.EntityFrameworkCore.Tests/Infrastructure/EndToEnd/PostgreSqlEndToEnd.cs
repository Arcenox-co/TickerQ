using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Tests.Shared.DashboardApi;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.EndToEnd;

/// <summary>
/// Real TickerQ hosts — scheduler, workers, recovery, and optionally the Dashboard over a test
/// server — on one PostgreSQL 17 container. Each test gets its own database. The function registry
/// is process-wide, so the collection never runs in parallel with other tests.
/// </summary>
[CollectionDefinition("PostgreSql end-to-end", DisableParallelization = true)]
public sealed class PostgreSqlEndToEndCollection : ICollectionFixture<PostgreSqlEndToEndServer>;

public sealed class PostgreSqlEndToEndServer : IAsyncLifetime
{
    /// <summary>The Application Name every host connection carries, so its connections can be counted.</summary>
    internal const string HostApplicationName = "tickerq-e2e";

    private PostgreSqlContainer _postgres;

    public async Task InitializeAsync()
    {
        if (!PostgreSqlRaceFactAttribute.Enabled) return;
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready -h 127.0.0.1 -U postgres"))
            .Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null) await _postgres.DisposeAsync();
    }

    internal string AdminConnectionString => _postgres.GetConnectionString();

    /// <summary>Creates an empty database and returns the connection string the hosts use for it.</summary>
    internal async Task<string> CreateDatabaseAsync()
    {
        var name = "e2e_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = name,
            ApplicationName = HostApplicationName,
        }.ConnectionString;
    }

    /// <summary>
    /// The connections the hosts hold open on <paramref name="connectionString"/>'s database right now,
    /// each as its state and the start of its last statement.
    /// </summary>
    internal async Task<List<string>> HostConnectionsAsync(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            ApplicationName = "tickerq-e2e-monitor",
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(state, '') || ': ' || left(regexp_replace(query, '\\s+', ' ', 'g'), 700) FROM pg_stat_activity " +
            "WHERE datname = @database AND application_name = @application",
            connection);
        command.Parameters.AddWithValue("database", database!);
        command.Parameters.AddWithValue("application", HostApplicationName);
        var connections = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) connections.Add(reader.GetString(0));
        return connections;
    }
}

/// <summary>How <see cref="EndToEndHost.StartAsync"/> builds one node.</summary>
internal sealed class EndToEndHostOptions
{
    public string Node { get; init; } = "e2e-node";
    public bool CreateTables { get; init; } = true;
    public bool Dashboard { get; init; }
    public bool ActiveLicense { get; init; } = true;
    public Action<SchedulerOptionsBuilder> Scheduler { get; init; }
    public Action<IServiceCollection> Services { get; init; }
}

internal static class EndToEndHost
{
    internal const string Namespace = "e2e";
    internal const string DashboardApi = "/tickerq/dashboard/api";

    /// <summary>Builds, migrates (optionally), and starts a TickerQ node on a test server.</summary>
    internal static async Task<WebApplication> StartAsync(string connectionString, EndToEndHostOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new NodeLabel(options.Node));
        options.Services?.Invoke(builder.Services);
        builder.Services.AddTickerQ(tickerq =>
        {
            tickerq.UseApplicationNamespace(Namespace)
                .UseReconciliationEpoch(1)
                .ConfigureScheduler(scheduler =>
                {
                    scheduler.NodeIdentifier = options.Node;
                    options.Scheduler?.Invoke(scheduler);
                })
                .AddOperationalStore(ef => ef
                    .UseTickerQDbContext<TickerQDbContext>(db => db.UseNpgsql(connectionString))
                    .SetSchema("ticker"));
            if (options.Dashboard)
                tickerq.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard());
        });
        if (options.ActiveLicense)
            TestLicense.UseActive(builder.Services);

        var app = builder.Build();
        if (options.CreateTables)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }
        app.UseTickerQ();
        await app.StartAsync();
        return app;
    }

    internal static string ExecutionOwnerId(this IHost host)
        => host.Services.GetRequiredService<SchedulerOptionsBuilder>().ExecutionOwnerId;

    internal static async Task<T> QueryAsync<T>(this IHost host, Func<TickerQDbContext, Task<T>> query)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TickerQDbContext>());
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds, failing with <paramref name="what"/> after
    /// <paramref name="timeout"/> and, when given, what <paramref name="describe"/> reports then.
    /// </summary>
    internal static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string what,
        Func<Task<string>> describe = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0}s waiting for {what}." +
                                           (describe == null ? "" : " " + await describe()));
            await Task.Delay(50);
        }
    }
}

/// <summary>Which node a function ran on: resolved from the executing host's services.</summary>
internal sealed record NodeLabel(string Name);
