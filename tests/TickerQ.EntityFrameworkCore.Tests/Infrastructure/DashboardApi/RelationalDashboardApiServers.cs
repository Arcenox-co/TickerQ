using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.EntityFrameworkCore.Tests.Infrastructure.Conformance;
using TickerQ.Tests.Shared.DashboardApi;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.DashboardApi;

/// <summary>
/// One relational database server in Docker for a whole test class. Each test gets its own database
/// (an Oracle user on Oracle) through <see cref="ConformanceDatabase"/>, dropped when the test ends.
/// </summary>
public abstract class RelationalDashboardApiServer : IDashboardApiServer, IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private IContainer _container;

    protected abstract RelationalConformanceTarget Target { get; }
    protected abstract int Port { get; }
    protected abstract ContainerBuilder Configure(ContainerBuilder builder);
    protected abstract string AdminConnectionString(string host, int port);

    private string Admin => AdminConnectionString(_container.Hostname, _container.GetMappedPublicPort(Port));

    public async Task InitializeAsync()
    {
        if (!DashboardApiFactAttribute.Enabled) return;
        _container = Configure(new ContainerBuilder().WithPortBinding(Port, true))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(Port))
            .Build();
        await _container.StartAsync();
        await WaitForConnectionsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    public async Task<DashboardApiStore> CreateStoreAsync()
        => new RelationalDashboardApiStore(await ConformanceDatabase.CreateAsync(Target, Admin, createTables: false));

    // An open port is not a ready server: MySQL restarts once after initializing, and SQL Server and
    // Oracle accept connections well before they accept logins.
    private async Task WaitForConnectionsAsync()
    {
        var deadline = DateTime.UtcNow + StartupTimeout;
        var factory = RelationalConformanceTargets.Factory(Target);
        while (true)
        {
            try
            {
                await using var connection = factory.CreateConnection()!;
                connection.ConnectionString = Admin;
                await connection.OpenAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private sealed class RelationalDashboardApiStore(ConformanceDatabase database) : DashboardApiStore
    {
        public override void Configure(TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> tickerq)
            => tickerq.AddOperationalStore(ef => ef
                .UseTickerQDbContext<TickerQDbContext>(db =>
                    RelationalConformanceTargets.Configure(db, database.Target, database.ConnectionString))
                .SetSchema(database.TickerQSchema));

        // Explicit table creation: EnsureCreated can skip CockroachDB (see ConformanceDatabase).
        public override async Task PrepareAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TickerQDbContext>()
                .GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        public override ValueTask DisposeAsync() => database.DisposeAsync();
    }
}

public sealed class PostgreSqlDashboardApiServer : RelationalDashboardApiServer
{
    protected override RelationalConformanceTarget Target => RelationalConformanceTarget.PostgreSql;
    protected override int Port => 5432;
    protected override ContainerBuilder Configure(ContainerBuilder builder) => builder
        .WithImage("postgres:17-alpine")
        .WithEnvironment("POSTGRES_PASSWORD", "postgres");
    protected override string AdminConnectionString(string host, int port)
        => $"Host={host};Port={port};Database=postgres;Username=postgres;Password=postgres";
}

public sealed class SqlServerDashboardApiServer : RelationalDashboardApiServer
{
    private const string Password = "TickerQ_Pw_2026!";
    protected override RelationalConformanceTarget Target => RelationalConformanceTarget.SqlServer;
    protected override int Port => 1433;
    protected override ContainerBuilder Configure(ContainerBuilder builder) => builder
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithEnvironment("ACCEPT_EULA", "Y")
        .WithEnvironment("MSSQL_SA_PASSWORD", Password);
    protected override string AdminConnectionString(string host, int port)
        => $"Server={host},{port};User Id=sa;Password={Password};Encrypt=False;TrustServerCertificate=True";
}

public sealed class MySqlDashboardApiServer : RelationalDashboardApiServer
{
    protected override RelationalConformanceTarget Target => RelationalConformanceTarget.MySql;
    protected override int Port => 3306;
    protected override ContainerBuilder Configure(ContainerBuilder builder) => builder
        .WithImage("mysql:8.4")
        .WithEnvironment("MYSQL_ROOT_PASSWORD", "pw");
    protected override string AdminConnectionString(string host, int port)
        => $"Server={host};Port={port};User=root;Password=pw";
}

public sealed class MariaDbDashboardApiServer : RelationalDashboardApiServer
{
    protected override RelationalConformanceTarget Target => RelationalConformanceTarget.MariaDb;
    protected override int Port => 3306;
    protected override ContainerBuilder Configure(ContainerBuilder builder) => builder
        .WithImage("mariadb:11.4")
        .WithEnvironment("MARIADB_ROOT_PASSWORD", "pw");
    protected override string AdminConnectionString(string host, int port)
        => $"Server={host};Port={port};User=root;Password=pw";
}

public sealed class CockroachDbDashboardApiServer : RelationalDashboardApiServer
{
    protected override RelationalConformanceTarget Target => RelationalConformanceTarget.CockroachDb;
    protected override int Port => 26257;
    protected override ContainerBuilder Configure(ContainerBuilder builder) => builder
        .WithImage("cockroachdb/cockroach:latest-v25.2")
        .WithCommand("start-single-node", "--insecure");
    protected override string AdminConnectionString(string host, int port)
        => $"Host={host};Port={port};Database=defaultdb;Username=root;SSL Mode=Disable;Options=-c autocommit_before_ddl=false";
}

public sealed class OracleDashboardApiServer : RelationalDashboardApiServer
{
    protected override RelationalConformanceTarget Target => RelationalConformanceTarget.Oracle;
    protected override int Port => 1521;
    protected override ContainerBuilder Configure(ContainerBuilder builder) => builder
        .WithImage("gvenzl/oracle-free:23-slim")
        .WithEnvironment("ORACLE_PASSWORD", "pw");
    protected override string AdminConnectionString(string host, int port)
        => $"User Id=system;Password=pw;Data Source={host}:{port}/FREEPDB1";
}

public sealed class YugabyteDbDashboardApiServer : RelationalDashboardApiServer
{
    protected override RelationalConformanceTarget Target => RelationalConformanceTarget.YugabyteDb;
    protected override int Port => 5433;
    // yugabyted aborts when the image has no chronyc binary; a stub lets its prerequisite check continue.
    protected override ContainerBuilder Configure(ContainerBuilder builder) => builder
        .WithImage("yugabytedb/yugabyte:2024.2.2.0-b70")
        .WithEntrypoint("/bin/bash")
        .WithCommand("-c",
            "printf '#!/bin/sh\\nexit 1\\n' > /usr/local/bin/chronyc && chmod +x /usr/local/bin/chronyc && " +
            "exec bin/yugabyted start --background=false");
    protected override string AdminConnectionString(string host, int port)
        => $"Host={host};Port={port};Database=yugabyte;Username=yugabyte;Password=yugabyte;Timeout=60;Command Timeout=120";
}
