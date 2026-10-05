using TickerQ.EntityFrameworkCore.Infrastructure;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfCoreRelationalIndexVerifierTests
{
    public static TheoryData<string, string> AdvertisedProviders => new()
    {
        { "Microsoft.EntityFrameworkCore.Sqlite", "Sqlite" },
        { "Microsoft.EntityFrameworkCore.SqlServer", "SqlServer" },
        { "Npgsql.EntityFrameworkCore.PostgreSQL", "PostgreSql" },
        { "Pomelo.EntityFrameworkCore.MySql", "MySql" },
        { "MySql.EntityFrameworkCore", "MySql" }
    };

    [Theory]
    [MemberData(nameof(AdvertisedProviders))]
    public void Advertised_provider_is_detected_and_has_a_semantic_catalog_query(
        string providerName, string expected)
    {
        var provider = EfCoreRelationalIndexVerifier.DetectProvider(providerName);
        var query = EfCoreRelationalIndexVerifier.GetCatalogQuery(provider);

        Assert.Equal(expected, provider.ToString());
        Assert.Contains("ColumnName", query, StringComparison.Ordinal);
        Assert.Contains("IsUnique", query, StringComparison.Ordinal);
        Assert.Contains("IsEnabled", query, StringComparison.Ordinal);
        Assert.Contains("IsValid", query, StringComparison.Ordinal);
        Assert.Contains("FilterDefinition", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_queries_prove_provider_specific_usable_state_and_null_semantics()
    {
        var sqlite = EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.Sqlite);
        Assert.Contains("pragma_index_info", sqlite, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partial", sqlite, StringComparison.OrdinalIgnoreCase);

        var sqlServer = EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.SqlServer);
        Assert.Contains("sys.indexes", sqlServer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_disabled", sqlServer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_hypothetical", sqlServer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filter_definition", sqlServer, StringComparison.OrdinalIgnoreCase);

        var postgres = EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.PostgreSql);
        Assert.Contains("pg_catalog.pg_index", postgres, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("indisvalid", postgres, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("indisready", postgres, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("indislive", postgres, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULLS NOT DISTINCT", postgres, StringComparison.OrdinalIgnoreCase);

        var mySql = EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.MySql);
        Assert.Contains("INFORMATION_SCHEMA.STATISTICS", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUB_PART", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DATABASE()", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT s.*", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("s.IS_VISIBLE", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("s.IGNORED", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1 AS `IsEnabled`", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS_VISIBLE", mySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IGNORED", mySql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Null_schema_catalog_queries_use_the_provider_model_default_not_the_connection_default()
    {
        var sqlServer = EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.SqlServer);
        Assert.Contains("COALESCE(@schema, 'dbo')", sqlServer, StringComparison.Ordinal);
        Assert.DoesNotContain("SCHEMA_NAME()", sqlServer, StringComparison.OrdinalIgnoreCase);

        var postgres = EfCoreRelationalIndexVerifier.GetCatalogQuery(RelationalIndexProvider.PostgreSql);
        Assert.Contains("COALESCE(@schema, 'public')", postgres, StringComparison.Ordinal);
        Assert.DoesNotContain("current_schema()", postgres, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_provider_fails_closed()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            EfCoreRelationalIndexVerifier.DetectProvider("Example.Unverified.Provider"));

        Assert.Contains("cannot verify", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("Pomelo.EntityFrameworkCore.MySql")]
    public void Native_null_distinct_provider_accepts_unfiltered_unique_seed_key(string providerName)
    {
        var provider = EfCoreRelationalIndexVerifier.DetectProvider(providerName);
        var requirement = SeedKeyRequirement(provider);
        var installed = Installed(requirement, unique: true);

        Assert.True(EfCoreRelationalIndexVerifier.IsSemanticallyEquivalent(
            provider, requirement, installed, out var mismatch), mismatch);
    }

    [Fact]
    public void Sql_server_requires_effective_not_null_filter_for_nullable_unique_seed_key()
    {
        var requirement = SeedKeyRequirement(RelationalIndexProvider.SqlServer);
        var unfiltered = Installed(requirement, unique: true);
        var filtered = unfiltered with { FilterDefinition = "([SeedKey] IS NOT NULL)" };

        Assert.False(EfCoreRelationalIndexVerifier.IsSemanticallyEquivalent(
            RelationalIndexProvider.SqlServer, requirement, unfiltered, out _));
        Assert.True(EfCoreRelationalIndexVerifier.IsSemanticallyEquivalent(
            RelationalIndexProvider.SqlServer, requirement, filtered, out var mismatch), mismatch);
    }

    [Theory]
    [InlineData("wrong table", "other", "ticker", "SeedKey", true, true, true)]
    [InlineData("wrong schema", "CronTickers", "other", "SeedKey", true, true, true)]
    [InlineData("wrong column", "CronTickers", "ticker", "Other", true, true, true)]
    [InlineData("wrong uniqueness", "CronTickers", "ticker", "SeedKey", false, true, true)]
    [InlineData("disabled", "CronTickers", "ticker", "SeedKey", true, false, true)]
    [InlineData("invalid", "CronTickers", "ticker", "SeedKey", true, true, false)]
    public void Semantic_mismatch_is_rejected(
        string _, string table, string schema, string column, bool unique, bool enabled, bool valid)
    {
        var requirement = SeedKeyRequirement(RelationalIndexProvider.PostgreSql);
        var installed = Installed(requirement, unique, table, schema, [column], enabled, valid);

        Assert.False(EfCoreRelationalIndexVerifier.IsSemanticallyEquivalent(
            RelationalIndexProvider.PostgreSql, requirement, installed, out var mismatch));
        Assert.False(string.IsNullOrWhiteSpace(mismatch));
    }

    [Fact]
    public void Composite_key_column_order_is_significant()
    {
        var requirement = new RelationalIndexRequirement(
            "IX_Order", "ticker", "Items", ["First", "Second"], false, null);
        var installed = new InstalledIndexDefinition(
            "IX_Order", "ticker", "Items", ["Second", "First"], false, true, true, null);

        Assert.False(EfCoreRelationalIndexVerifier.IsSemanticallyEquivalent(
            RelationalIndexProvider.PostgreSql, requirement, installed, out _));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "dbo")]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", "public")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", null)]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", null)]
    public void Null_model_schema_matches_provider_catalog_scope(string providerName, string? installedSchema)
    {
        var provider = EfCoreRelationalIndexVerifier.DetectProvider(providerName);
        var requirement = new RelationalIndexRequirement(
            "IX_DefaultSchema", null, "Items", ["Value"], false, null);
        var installed = new InstalledIndexDefinition(
            requirement.Name, installedSchema, requirement.Table, requirement.Columns,
            false, true, true, null);

        Assert.True(EfCoreRelationalIndexVerifier.IsSemanticallyEquivalent(
            provider, requirement, installed, out var mismatch), mismatch);
    }

    private static RelationalIndexRequirement SeedKeyRequirement(RelationalIndexProvider provider)
        => new("UX_CronTickers_SeedKey",
            provider is RelationalIndexProvider.Sqlite or RelationalIndexProvider.MySql ? null : "ticker",
            "CronTickers", ["ApplicationNamespaceKey", "SeedKey"], true, "SeedKey");

    private static InstalledIndexDefinition Installed(
        RelationalIndexRequirement requirement,
        bool unique,
        string? table = null,
        string? schema = null,
        IReadOnlyList<string>? columns = null,
        bool enabled = true,
        bool valid = true)
        => new(requirement.Name, schema ?? requirement.Schema, table ?? requirement.Table,
            columns ?? requirement.Columns, unique, enabled, valid, null);
}
