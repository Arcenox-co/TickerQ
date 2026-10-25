using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using TickerQ.EntityFrameworkCore.Infrastructure;

#if TICKERQ_TEST_MICROTING_POMELO_NAMESPACE
// Some Microting releases still ship Pomelo's assembly and namespace (see eng/DotNetLines.props).
using MicrotingSchemaBehavior = Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior;
#elif TICKERQ_TEST_MICROTING
using MicrotingSchemaBehavior = Microting.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior;
#endif

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.Conformance;

/// <summary>
/// A relational database the conformance suite can run against. Each target is opt-in through an
/// administrator connection string in <c>TICKERQ_CONFORMANCE_&lt;NAME&gt;</c>; the suite creates and
/// drops an isolated database (or Oracle user) per test, so the target server is never modified
/// beyond those throwaway objects.
/// </summary>
public enum RelationalConformanceTarget
{
    PostgreSql,
    CockroachDb,
    YugabyteDb,
    MySql,
    MariaDb,
    /// <summary>MariaDB through Microting.EntityFrameworkCore.MySql, the Pomelo fork for EF Core 10.</summary>
    MariaDbMicroting,
    SqlServer,
    /// <summary>Oracle with the provider's default settings. Point it at 19c/21c as well as 23ai.</summary>
    Oracle,
    /// <summary>Oracle with the provider's <c>OracleSQLCompatibility.DatabaseVersion19</c> setting.</summary>
    OracleCompatibility19,
}

internal static class RelationalConformanceTargets
{
    public static string EnvironmentVariable(RelationalConformanceTarget target)
        => "TICKERQ_CONFORMANCE_" + target.ToString().ToUpperInvariant();

    public static string AdminConnectionString(RelationalConformanceTarget target)
        => Environment.GetEnvironmentVariable(EnvironmentVariable(target));

    public static IEnumerable<RelationalConformanceTarget> Configured()
        => Enum.GetValues<RelationalConformanceTarget>()
            .Where(target => !string.IsNullOrWhiteSpace(AdminConnectionString(target)));

    /// <summary>TickerQ's relational schema for the target; null uses the connection's default.</summary>
    public static string Schema(RelationalConformanceTarget target) => target switch
    {
        RelationalConformanceTarget.PostgreSql or RelationalConformanceTarget.YugabyteDb
            or RelationalConformanceTarget.SqlServer => "ticker",
        // CockroachDB cannot run EF's EnsureSchema (CREATE SCHEMA inside a DO block), so TickerQ
        // tables live in the default public schema there, which EF never tries to create.
        RelationalConformanceTarget.CockroachDb => "public",
        // MySQL/MariaDB have no schemas inside a database, and an Oracle schema is a user.
        _ => null,
    };

    public static DbProviderFactory Factory(RelationalConformanceTarget target) => target switch
    {
        RelationalConformanceTarget.PostgreSql or RelationalConformanceTarget.CockroachDb
            or RelationalConformanceTarget.YugabyteDb => NpgsqlFactory.Instance,
        RelationalConformanceTarget.MySql or RelationalConformanceTarget.MariaDb
            or RelationalConformanceTarget.MariaDbMicroting => MySql.Data.MySqlClient.MySqlClientFactory.Instance,
        RelationalConformanceTarget.SqlServer => Microsoft.Data.SqlClient.SqlClientFactory.Instance,
#if TICKERQ_TEST_ORACLE
        RelationalConformanceTarget.Oracle or RelationalConformanceTarget.OracleCompatibility19
            => Oracle.ManagedDataAccess.Client.OracleClientFactory.Instance,
#endif
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    public static DbContextOptionsBuilder<TContext> Configure<TContext>(
        DbContextOptionsBuilder<TContext> builder, RelationalConformanceTarget target, string connectionString)
        where TContext : DbContext
    {
        Configure((DbContextOptionsBuilder)builder, target, connectionString);
        return builder;
    }

    public static void Configure(DbContextOptionsBuilder builder, RelationalConformanceTarget target, string connectionString)
    {
        switch (target)
        {
            case RelationalConformanceTarget.PostgreSql:
            case RelationalConformanceTarget.CockroachDb:
            case RelationalConformanceTarget.YugabyteDb:
                builder.UseNpgsql(connectionString);
                break;
            case RelationalConformanceTarget.MySql:
            case RelationalConformanceTarget.MariaDb:
                builder.UseMySQL(connectionString);
                break;
            case RelationalConformanceTarget.MariaDbMicroting:
#if TICKERQ_TEST_MICROTING
                // Pomelo-based providers reject EF schemas unless told how to treat them; Ignore keeps
                // TickerQ's tables in the connection's database, as users of this provider configure it.
                builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                    mysql => mysql.SchemaBehavior(MicrotingSchemaBehavior.Ignore));
                break;
#else
                throw new NotSupportedException("Microting.EntityFrameworkCore.MySql has no release for this .NET line.");
#endif
            case RelationalConformanceTarget.SqlServer:
                builder.UseSqlServer(connectionString);
                break;
#if TICKERQ_TEST_ORACLE
            case RelationalConformanceTarget.Oracle:
                builder.UseOracle(connectionString);
                break;
            case RelationalConformanceTarget.OracleCompatibility19:
                builder.UseOracle(connectionString, oracle =>
                    oracle.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19));
                break;
#else
            case RelationalConformanceTarget.Oracle:
            case RelationalConformanceTarget.OracleCompatibility19:
                throw new NotSupportedException("Oracle.EntityFrameworkCore has no release for this .NET line.");
#endif
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }
}

/// <summary>Moves every TickerQ table into the target's schema (or its default schema).</summary>
public sealed class ConformanceSchemaModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var schema = context.GetService<IDbContextOptions>()
            .FindExtension<ConformanceSchemaExtension>()?.Schema;
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => e.GetTableName() != null))
            entity.SetSchema(schema);
    }
}

/// <summary>Carries the target schema into the model and keys the model cache by it.</summary>
public sealed class ConformanceSchemaExtension(string schema) : IDbContextOptionsExtension
{
    public string Schema { get; } = schema;
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
    public void ApplyServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { }
    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo(ConformanceSchemaExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => $"ConformanceSchema={extension.Schema ?? "<default>"} ";
        public override int GetServiceProviderHashCode() => (extension.Schema ?? string.Empty).GetHashCode();
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo info && info.Extension is ConformanceSchemaExtension e && e.Schema == extension.Schema;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["ConformanceSchema"] = extension.Schema ?? string.Empty;
    }
}

/// <summary>An isolated database on a conformance target, dropped on dispose.</summary>
internal sealed class ConformanceDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _name;

    private ConformanceDatabase(RelationalConformanceTarget target, string adminConnectionString, string name,
        string connectionString)
    {
        Target = target;
        _adminConnectionString = adminConnectionString;
        _name = name;
        ConnectionString = connectionString;
    }

    public RelationalConformanceTarget Target { get; }
    public string ConnectionString { get; }
    public string Schema => RelationalConformanceTargets.Schema(Target);

    /// <summary>
    /// Schema to pass to TickerQ's <c>SetSchema</c>. TickerQ treats null as its "ticker" default, so
    /// targets where a schema is a database (MySQL/MariaDB) or a user (Oracle) name the isolated one.
    /// </summary>
    public string TickerQSchema => Target switch
    {
        RelationalConformanceTarget.MySql or RelationalConformanceTarget.MariaDb
            or RelationalConformanceTarget.MariaDbMicroting => _name,
        RelationalConformanceTarget.Oracle or RelationalConformanceTarget.OracleCompatibility19 => _name,
        _ => Schema,
    };

    public DbContextOptions<TestTickerQDbContext> Options(Action<DbContextOptionsBuilder<TestTickerQDbContext>> extra = null)
    {
        var builder = RelationalConformanceTargets.Configure(
            new DbContextOptionsBuilder<TestTickerQDbContext>(), Target, ConnectionString);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new ConformanceSchemaExtension(Schema));
        builder.ReplaceService<IModelCustomizer, ConformanceSchemaModelCustomizer>();
        // Mirrors what TickerQ's EF registration adds to every context it stores data in.
        builder.AddInterceptors(MySqlDeleteAliasInterceptor.Instance);
        extra?.Invoke(builder);
        return builder.Options;
    }

    public static async Task<ConformanceDatabase> CreateAsync(RelationalConformanceTarget target, bool createTables = true)
    {
        var admin = RelationalConformanceTargets.AdminConnectionString(target)
            ?? throw new InvalidOperationException($"{RelationalConformanceTargets.EnvironmentVariable(target)} is not set.");
        var name = "tqconf" + Guid.NewGuid().ToString("N")[..12];
        var factory = RelationalConformanceTargets.Factory(target);
        var builder = factory.CreateConnectionStringBuilder()!;
        builder.ConnectionString = admin;

        switch (target)
        {
            case RelationalConformanceTarget.PostgreSql:
            case RelationalConformanceTarget.CockroachDb:
            case RelationalConformanceTarget.YugabyteDb:
                await ExecuteAsync(factory, admin, $"CREATE DATABASE {name}");
                builder["Database"] = name;
                break;
            case RelationalConformanceTarget.MySql:
            case RelationalConformanceTarget.MariaDb:
            case RelationalConformanceTarget.MariaDbMicroting:
                await ExecuteAsync(factory, admin, $"CREATE DATABASE `{name}`");
                builder["Database"] = name;
                break;
            case RelationalConformanceTarget.SqlServer:
                await ExecuteAsync(factory, admin, $"CREATE DATABASE [{name}]");
                builder["Initial Catalog"] = name;
                break;
            case RelationalConformanceTarget.Oracle:
            case RelationalConformanceTarget.OracleCompatibility19:
                name = name.ToUpperInvariant();
                const string password = "TqConf_Pw_2026";
                await ExecuteAsync(factory, admin,
                    $"CREATE USER {name} IDENTIFIED BY \"{password}\" DEFAULT TABLESPACE USERS QUOTA UNLIMITED ON USERS");
                await ExecuteAsync(factory, admin,
                    $"GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE, CREATE VIEW, CREATE PROCEDURE, CREATE TRIGGER TO {name}");
                builder["User Id"] = name;
                builder["Password"] = password;
                break;
        }

        var database = new ConformanceDatabase(target, admin, name, builder.ConnectionString);
        if (!createTables)
            return database;
        // Create the tables explicitly. EnsureCreated decides whether to create them by checking for
        // existing tables, and CockroachDB's built-in pg_extension tables make an empty database look
        // populated, so EnsureCreated would silently do nothing there. Later EnsureCreated calls in
        // the shared contracts then see TickerQ's own tables and are no-ops on every target.
        await using (var context = new TestTickerQDbContext(database.Options()))
            await context.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>()
                .CreateTablesAsync();
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        // Set TICKERQ_CONFORMANCE_KEEP=1 to keep the throwaway database for inspection after a failure.
        if (Environment.GetEnvironmentVariable("TICKERQ_CONFORMANCE_KEEP") == "1")
            return;
        var factory = RelationalConformanceTargets.Factory(Target);
        try
        {
            switch (Target)
            {
                case RelationalConformanceTarget.PostgreSql:
                    NpgsqlConnection.ClearAllPools();
                    await ExecuteAsync(factory, _adminConnectionString, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
                    break;
                case RelationalConformanceTarget.CockroachDb:
                    NpgsqlConnection.ClearAllPools();
                    await ExecuteAsync(factory, _adminConnectionString, $"DROP DATABASE IF EXISTS {_name} CASCADE");
                    break;
                case RelationalConformanceTarget.YugabyteDb:
                    NpgsqlConnection.ClearAllPools();
                    await ExecuteAsync(factory, _adminConnectionString, $"DROP DATABASE IF EXISTS {_name}");
                    break;
                case RelationalConformanceTarget.MySql:
                case RelationalConformanceTarget.MariaDb:
                case RelationalConformanceTarget.MariaDbMicroting:
                    MySql.Data.MySqlClient.MySqlConnection.ClearAllPools();
#if TICKERQ_TEST_MICROTING
                    // Microting runs on MySqlConnector, which keeps its own pool.
                    MySqlConnector.MySqlConnection.ClearAllPools();
#endif
                    await ExecuteAsync(factory, _adminConnectionString, $"DROP DATABASE IF EXISTS `{_name}`");
                    break;
                case RelationalConformanceTarget.SqlServer:
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    await ExecuteAsync(factory, _adminConnectionString,
                        $"IF DB_ID('{_name}') IS NOT NULL BEGIN ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]; END");
                    break;
#if TICKERQ_TEST_ORACLE
                case RelationalConformanceTarget.Oracle:
                case RelationalConformanceTarget.OracleCompatibility19:
                    Oracle.ManagedDataAccess.Client.OracleConnection.ClearAllPools();
                    await ExecuteAsync(factory, _adminConnectionString, $"DROP USER {_name} CASCADE");
                    break;
#endif
            }
        }
        catch
        {
            // Cleanup is best-effort; a leftover throwaway database must not mask the test result.
        }
    }

    private static async Task ExecuteAsync(DbProviderFactory factory, string connectionString, string sql)
    {
        await using var connection = factory.CreateConnection()!;
        connection.ConnectionString = connectionString;
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Skips the end-to-end runtime tests unless a target and a license are configured. The runtime only
/// executes jobs under a valid license, so set TICKERQ_CONFORMANCE_LICENSE to a .tqlicense path.
/// </summary>
public sealed class RelationalRuntimeConformanceTheoryAttribute : TheoryAttribute
{
    public RelationalRuntimeConformanceTheoryAttribute()
    {
        if (!RelationalConformanceTargets.Configured().Any())
            Skip = "Set one or more TICKERQ_CONFORMANCE_<TARGET> admin connection strings to run the runtime conformance tests.";
        else if (!File.Exists(Environment.GetEnvironmentVariable("TICKERQ_CONFORMANCE_LICENSE") ?? string.Empty))
            Skip = "Set TICKERQ_CONFORMANCE_LICENSE to a TickerQ license file to run the runtime conformance tests.";
    }
}

/// <summary>Skips the conformance suite unless at least one target is configured.</summary>
public sealed class RelationalConformanceTheoryAttribute : TheoryAttribute
{
    public RelationalConformanceTheoryAttribute()
    {
        if (!RelationalConformanceTargets.Configured().Any())
            Skip = "Set one or more TICKERQ_CONFORMANCE_<TARGET> admin connection strings " +
                   "(POSTGRESQL, COCKROACHDB, YUGABYTEDB, MYSQL, MARIADB, SQLSERVER, ORACLE) to run the relational conformance suite.";
    }
}

public static class RelationalConformanceData
{
    public static IEnumerable<object[]> Targets()
        => RelationalConformanceTargets.Configured().Select(target => new object[] { target });
}
