using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Entities;
using FakeDrivers = TickerQ.EntityFrameworkCore.Tests.FakeDrivers;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Database-free checks for the provider-compatibility code. The relational conformance suite
/// exercises the same behavior against real servers.
/// </summary>
public class ProviderCompatibilityTests
{
    [Theory]
    [InlineData("DELETE FROM `TimeTickers` AS `t` WHERE `t`.`Id` = @p0",
        "DELETE `t` FROM `TimeTickers` AS `t` WHERE `t`.`Id` = @p0")]
    [InlineData("DELETE FROM `app`.`TimeTickers` AS `t`\nWHERE `t`.`Id` = @p0",
        "DELETE `t` FROM `app`.`TimeTickers` AS `t`\nWHERE `t`.`Id` = @p0")]
    [InlineData("SELECT 1;\nDELETE FROM `CronTickers` AS `c` WHERE `c`.`Id` = @p0",
        "SELECT 1;\nDELETE `c` FROM `CronTickers` AS `c` WHERE `c`.`Id` = @p0")]
    public void MySql_delete_alias_is_rewritten_to_the_portable_form(string input, string expected)
        => Assert.Equal(expected, MySqlDeleteAliasInterceptor.Rewrite(input));

    [Theory]
    [InlineData("DELETE FROM `TimeTickers` WHERE `Id` = @p0")]
    [InlineData("DELETE FROM `TimeTickers` AS `t` WHERE `t`.`Id` = @p0 ORDER BY `t`.`Id` LIMIT 10")]
    [InlineData("UPDATE `TimeTickers` AS `t` SET `t`.`Status` = 1")]
    [InlineData("SELECT * FROM `TimeTickers` AS `t`")]
    [InlineData("")]
    public void Statements_outside_the_aliased_delete_form_are_unchanged(string input)
        => Assert.Equal(input, MySqlDeleteAliasInterceptor.Rewrite(input));

    [Theory]
    [InlineData("40001", true, true)]
    [InlineData("40P01", true, true)]
    [InlineData("40003", false, false)]
    [InlineData("23505", false, false)]
    public void SqlState_conflicts_are_classified(string sqlState, bool conflict, bool rolledBack)
    {
        Assert.Equal(conflict, RelationalTransactionConflicts.IsConflict(new FakeDrivers.NpgsqlLikeException(sqlState), out var fullRollback));
        Assert.Equal(rolledBack, fullRollback);
    }

    [Fact]
    public void Conflicts_are_found_inside_execution_strategy_wrappers()
    {
        // Npgsql's default execution strategy reports a serialization failure on SaveChanges as an
        // InvalidOperationException around a DbUpdateException around the driver exception.
        var wrapped = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.",
            new DbUpdateException("save failed", new FakeDrivers.NpgsqlLikeException("40001")));

        Assert.True(RelationalTransactionConflicts.IsConflictInChain(wrapped));
        Assert.False(RelationalTransactionConflicts.IsConflictInChain(
            new InvalidOperationException("other", new FakeDrivers.NpgsqlLikeException("23505"))));
    }

    [Theory]
    [InlineData(1205, true, true)]   // deadlock victim
    [InlineData(3960, true, true)]   // snapshot update conflict
    [InlineData(1222, false, false)] // lock request timeout is not classified
    [InlineData(2627, false, false)] // unique violation is not a conflict
    public void Sql_server_conflicts_are_classified(int number, bool conflict, bool rolledBack)
    {
        Assert.Equal(conflict, RelationalTransactionConflicts.IsConflict(new FakeDrivers.SqlException(number), out var fullRollback));
        Assert.Equal(rolledBack, fullRollback);
    }

    [Theory]
    [InlineData(1213, true, true)]   // deadlock rolls back the transaction
    [InlineData(1205, true, false)]  // lock wait timeout rolls back the statement only
    [InlineData(1062, false, false)]
    public void MySql_conflicts_are_classified(int number, bool conflict, bool rolledBack)
    {
        Assert.Equal(conflict, RelationalTransactionConflicts.IsConflict(new FakeDrivers.MySqlException(number), out var fullRollback));
        Assert.Equal(rolledBack, fullRollback);
    }

    [Theory]
    [InlineData(8177, "ORA-08177: can't serialize access for this transaction", true)]
    [InlineData(60, "ORA-00060: deadlock detected", true)]
    [InlineData(8176, "ORA-08176: consistent read failure", true)]
    [InlineData(604, "ORA-00604: recursive SQL\nORA-08177: can't serialize access", true)]
    [InlineData(604, "ORA-00604: recursive SQL\nORA-01652: unable to extend temp segment", false)]
    [InlineData(1, "ORA-00001: unique constraint violated", false)]
    public void Oracle_conflicts_are_classified_and_never_claim_a_full_rollback(int number, string message, bool conflict)
    {
        Assert.Equal(conflict, RelationalTransactionConflicts.IsConflict(new FakeDrivers.OracleException(number, message), out var fullRollback));
        Assert.False(fullRollback);
    }

    [Fact]
    public void Unique_violations_are_recognized_through_wrappers()
    {
        Assert.True(RelationalTransactionConflicts.IsUniqueViolation(new FakeDrivers.NpgsqlLikeException("23505")));
        Assert.True(RelationalTransactionConflicts.IsUniqueViolation(new FakeDrivers.SqlException(2601)));
        Assert.True(RelationalTransactionConflicts.IsUniqueViolation(new FakeDrivers.MySqlException(1062)));
        Assert.True(RelationalTransactionConflicts.IsUniqueViolation(
            new DbUpdateException("wrapped", new FakeDrivers.OracleException(1, "ORA-00001"))));
        Assert.False(RelationalTransactionConflicts.IsUniqueViolation(new FakeDrivers.OracleException(8177, "ORA-08177")));
        Assert.False(RelationalTransactionConflicts.IsUniqueViolation(new InvalidOperationException("other")));
    }

    [Fact]
    public void Oracle_is_a_recognized_index_verification_provider()
    {
        Assert.Equal(RelationalIndexProvider.Oracle, EfCoreRelationalIndexVerifier.DetectProvider("Oracle.EntityFrameworkCore"));
        var query = EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.Oracle);
        Assert.Contains(":p_name", query);
        Assert.Contains(":p_table", query);
        Assert.Contains(":p_schema", query);
        Assert.DoesNotContain(":table", query.Replace(":p_table", string.Empty));
    }

    [Fact]
    public void CockroachDb_catalog_query_does_not_require_indisready()
    {
        Assert.Contains("ix.indisready", EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.PostgreSql));
        Assert.DoesNotContain("indisready", EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.PostgreSql, cockroachDb: true));
    }

    [Fact]
    public void Oracle_with_an_unconfigured_schema_accepts_the_connection_schema()
    {
        var requirement = new RelationalIndexRequirement("UX_CronTickers_SeedKey", null, "CronTickers",
            ["SeedKeyScope", "SeedKey"], true, "SeedKey");
        var installed = new InstalledIndexDefinition("UX_CronTickers_SeedKey", "TICKERQ_OWNER", "CronTickers",
            ["SeedKeyScope", "SeedKey"], true, true, true, null);

        Assert.True(EfCoreRelationalIndexVerifier.IsSemanticallyEquivalent(
            RelationalIndexProvider.Oracle, requirement, installed, out var mismatch), mismatch);
    }

    [Fact]
    public void Oracle_model_scopes_the_seed_key_index_with_a_virtual_column()
    {
        var model = DesignModel(b => b.UseOracle("User Id=u;Password=p;Data Source=localhost/FREEPDB1"));
        var cron = model.FindEntityType(typeof(CronTickerEntity))!;

        var scope = cron.FindProperty("SeedKeyScope");
        Assert.NotNull(scope);
        Assert.Contains("\"SeedKey\" IS NOT NULL", scope!.GetComputedColumnSql());
        Assert.False(scope.GetIsStored() ?? false);
        var index = Assert.Single(cron.GetIndexes(), i => i.GetDatabaseName() == "UX_CronTickers_SeedKey");
        Assert.True(index.IsUnique);
        Assert.Equal(["SeedKeyScope", "SeedKey"], index.Properties.Select(p => p.Name));
    }

    [Fact]
    public void MySql_model_keeps_investigation_index_keys_within_the_innodb_limit()
    {
        var model = DesignModel(b => b.UseMySQL("Server=localhost;Database=tickerq;User=u;Password=p"));
        var investigations = model.FindEntityType(typeof(AssistantInvestigationEntity))!;

        Assert.DoesNotContain(investigations.GetIndexes(),
            i => i.GetDatabaseName() == "IX_AssistantInvestigation_Namespace_Function_OccurredAt");
        var index = Assert.Single(investigations.GetIndexes(),
            i => i.GetDatabaseName() == "IX_AssistantInvestigation_Namespace_OccurredAt");
        Assert.Equal(["ApplicationNamespace", "OccurredAt"], index.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Other_providers_keep_the_portable_model()
    {
        var model = DesignModel(b => b.UseSqlite("Data Source=:memory:"));
        var cron = model.FindEntityType(typeof(CronTickerEntity))!;

        Assert.Null(cron.FindProperty("SeedKeyScope"));
        var seedIndex = Assert.Single(cron.GetIndexes(), i => i.GetDatabaseName() == "UX_CronTickers_SeedKey");
        Assert.Equal(["ApplicationNamespaceKey", "SeedKey"], seedIndex.Properties.Select(p => p.Name));
        Assert.Contains(model.FindEntityType(typeof(AssistantInvestigationEntity))!.GetIndexes(),
            i => i.GetDatabaseName() == "IX_AssistantInvestigation_Namespace_Function_OccurredAt");
    }

    [Fact]
    public void Result_payload_has_no_column_length_so_mysql_maps_a_large_binary_type()
    {
        var model = DesignModel(b => b.UseSqlite("Data Source=:memory:"));
        foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is "TimeTickerResults" or "CronTickerOccurrenceResults"))
            Assert.Null(entity.FindProperty("Payload")!.GetMaxLength());
    }

    [Fact]
    public void Sample_migrations_match_the_current_model()
    {
        // The sample's migrations are compiled into this assembly; read its snapshot from here.
        var options = new DbContextOptionsBuilder<TickerQDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly(typeof(ProviderCompatibilityTests).Assembly.GetName().Name))
            .Options;
        using var context = new TickerQDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges(),
            "The sample's model snapshot is out of date. Regenerate it with dotnet ef migrations add.");
    }

    private static IModel DesignModel(Action<DbContextOptionsBuilder<TickerQDbContext>> configure)
    {
        var builder = new DbContextOptionsBuilder<TickerQDbContext>();
        configure(builder);
        using var context = new TickerQDbContext(builder.Options);
        return context.GetService<IDesignTimeModel>().Model;
    }
}
