using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal enum RelationalIndexProvider
{
    Sqlite,
    SqlServer,
    PostgreSql,
    MySql
}

internal sealed record RelationalIndexRequirement(
    string Name,
    string Schema,
    string Table,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    string NullableUniqueColumn);

internal sealed record InstalledIndexDefinition(
    string Name,
    string Schema,
    string Table,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    bool IsEnabled,
    bool IsValid,
    string FilterDefinition);

/// <summary>
/// Verifies installed relational indexes from provider catalogs without taking hard dependencies on
/// provider ADO packages. A same-name index is not proof: its owning table/schema, ordered key columns,
/// uniqueness, usable state, and nullable SeedKey behavior must all match.
/// </summary>
internal static class EfCoreRelationalIndexVerifier
{
    internal static RelationalIndexProvider DetectProvider(string providerName)
    {
        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return RelationalIndexProvider.Sqlite;
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return RelationalIndexProvider.SqlServer;
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return RelationalIndexProvider.PostgreSql;
        if (providerName.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return RelationalIndexProvider.MySql;

        throw new InvalidOperationException(
            $"TickerQ cannot verify required relational indexes for provider '{providerName}'. " +
            "Schema/data versions will not be certified.");
    }

    internal static string GetCatalogQuery(RelationalIndexProvider provider) => provider switch
    {
        RelationalIndexProvider.Sqlite => """
            SELECT NULL AS "SchemaName", m.tbl_name AS "TableName", m.name AS "IndexName",
                   il."unique" AS "IsUnique", 1 AS "IsEnabled", 1 AS "IsValid",
                   CASE WHEN il.partial = 1
                        THEN substr(m.sql, instr(upper(m.sql), ' WHERE ') + 7)
                        ELSE NULL END AS "FilterDefinition",
                   ii.seqno + 1 AS "Ordinal", ii.name AS "ColumnName"
            FROM sqlite_master AS m
            JOIN pragma_index_list(m.tbl_name) AS il ON il.name = m.name
            JOIN pragma_index_info(m.name) AS ii
            WHERE m.type = 'index'
              AND m.name = $name COLLATE NOCASE AND m.tbl_name = $table COLLATE NOCASE
            ORDER BY ii.seqno
            """,
        RelationalIndexProvider.SqlServer => """
            SELECT s.name AS [SchemaName], t.name AS [TableName], i.name AS [IndexName],
                   i.is_unique AS [IsUnique], CONVERT(bit, CASE WHEN i.is_disabled = 0 THEN 1 ELSE 0 END) AS [IsEnabled],
                   CONVERT(bit, CASE WHEN i.is_hypothetical = 0 THEN 1 ELSE 0 END) AS [IsValid],
                   i.filter_definition AS [FilterDefinition], ic.key_ordinal AS [Ordinal], c.name AS [ColumnName]
            FROM sys.indexes AS i
            JOIN sys.tables AS t ON t.object_id = i.object_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = @name AND t.name = @table
              AND s.name = COALESCE(@schema, 'dbo')
              AND ic.key_ordinal > 0
            ORDER BY ic.key_ordinal
            """,
        RelationalIndexProvider.PostgreSql => """
            SELECT ns.nspname AS "SchemaName", tbl.relname AS "TableName", idx.relname AS "IndexName",
                   ix.indisunique AS "IsUnique", ix.indislive AS "IsEnabled",
                   (ix.indisvalid AND ix.indisready AND
                    position('NULLS NOT DISTINCT' in upper(pg_get_indexdef(ix.indexrelid))) = 0) AS "IsValid",
                   pg_get_expr(ix.indpred, ix.indrelid) AS "FilterDefinition",
                   keys.ordinality AS "Ordinal", att.attname AS "ColumnName"
            FROM pg_catalog.pg_index AS ix
            JOIN pg_catalog.pg_class AS idx ON idx.oid = ix.indexrelid
            JOIN pg_catalog.pg_class AS tbl ON tbl.oid = ix.indrelid
            JOIN pg_catalog.pg_namespace AS ns ON ns.oid = tbl.relnamespace
            JOIN LATERAL unnest(ix.indkey::smallint[]) WITH ORDINALITY AS keys(attnum, ordinality)
              ON keys.ordinality <= ix.indnkeyatts
            LEFT JOIN pg_catalog.pg_attribute AS att
              ON att.attrelid = tbl.oid AND att.attnum = keys.attnum
            WHERE idx.relname = @name AND tbl.relname = @table
              AND ns.nspname = COALESCE(@schema, 'public')
            ORDER BY keys.ordinality
            """,
        RelationalIndexProvider.MySql => """
            /* Catalog fields are mapped by the reader to SchemaName, TableName, IndexName,
               IsUnique, IsEnabled, IsValid, FilterDefinition, Ordinal, and ColumnName.
               Read s.* (including SUB_PART) so this single query works on MySQL (IS_VISIBLE),
               MariaDB (IGNORED), and older variants. Certification fails closed when neither
               usable-state column exists. */
            SELECT s.*
            FROM INFORMATION_SCHEMA.STATISTICS AS s
            WHERE s.TABLE_SCHEMA = DATABASE() AND s.INDEX_NAME = @name AND s.TABLE_NAME = @table
            ORDER BY s.SEQ_IN_INDEX
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    internal static async Task VerifyAsync(
        DbConnection connection,
        string providerName,
        IReadOnlyList<RelationalIndexRequirement> requirements,
        CancellationToken cancellationToken)
    {
        var provider = DetectProvider(providerName);
        foreach (var requirement in requirements)
        {
            var installed = await ReadInstalledAsync(connection, provider, requirement, cancellationToken)
                .ConfigureAwait(false);
            if (installed == null)
                throw new InvalidOperationException(
                    $"Required TickerQ index '{requirement.Name}' is missing from " +
                    $"'{DisplayName(requirement.Schema, requirement.Table)}'.");
            if (!IsSemanticallyEquivalent(provider, requirement, installed, out var mismatch))
                throw new InvalidOperationException(
                    $"Required TickerQ index '{requirement.Name}' is not semantically valid: {mismatch}.");
        }
    }

    internal static bool IsSemanticallyEquivalent(
        RelationalIndexProvider provider,
        RelationalIndexRequirement requirement,
        InstalledIndexDefinition installed,
        out string mismatch)
    {
        // PostgreSQL quoted identifiers are case-sensitive. The other advertised engines resolve
        // ordinary catalog identifiers case-insensitively (subject to their database/filesystem rules).
        var identifierComparison = provider == RelationalIndexProvider.PostgreSql
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!string.Equals(requirement.Name, installed.Name, identifierComparison))
            return Fail("name does not match", out mismatch);
        if (!string.Equals(requirement.Table, installed.Table, identifierComparison))
            return Fail($"expected table '{requirement.Table}', found '{installed.Table}'", out mismatch);
        var expectedSchema = EffectiveSchema(provider, requirement.Schema);
        if (!string.Equals(expectedSchema ?? string.Empty, installed.Schema ?? string.Empty,
                identifierComparison))
            return Fail($"expected schema '{expectedSchema}', found '{installed.Schema}'", out mismatch);
        if (!requirement.Columns.SequenceEqual(installed.Columns,
                provider == RelationalIndexProvider.PostgreSql ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase))
            return Fail(
                $"expected ordered columns ({string.Join(", ", requirement.Columns)}), found " +
                $"({string.Join(", ", installed.Columns)})", out mismatch);
        if (requirement.IsUnique != installed.IsUnique)
            return Fail(requirement.IsUnique ? "index is not unique" : "index is unexpectedly unique", out mismatch);
        if (!installed.IsEnabled)
            return Fail("index is disabled or not maintained", out mismatch);
        if (!installed.IsValid)
            return Fail("index is invalid, hypothetical, or not ready", out mismatch);

        if (!string.IsNullOrWhiteSpace(requirement.NullableUniqueColumn))
        {
            if (provider == RelationalIndexProvider.SqlServer)
            {
                if (!IsSingleColumnNotNullFilter(installed.FilterDefinition, requirement.NullableUniqueColumn))
                    return Fail(
                        $"SQL Server nullable unique SeedKey index must be filtered with " +
                        $"'{requirement.NullableUniqueColumn} IS NOT NULL'", out mismatch);
            }
            else if (!string.IsNullOrWhiteSpace(installed.FilterDefinition))
            {
                return Fail(
                    $"{provider} must use its native multiple-NULL unique-index semantics, not a partial predicate",
                    out mismatch);
            }
        }
        else if (!string.IsNullOrWhiteSpace(installed.FilterDefinition))
        {
            return Fail("unexpected partial/filtered predicate changes the indexed row set", out mismatch);
        }

        mismatch = string.Empty;
        return true;
    }

    private static async Task<InstalledIndexDefinition> ReadInstalledAsync(
        DbConnection connection,
        RelationalIndexProvider provider,
        RelationalIndexRequirement requirement,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = GetCatalogQuery(provider);
        AddParameter(command, provider == RelationalIndexProvider.Sqlite ? "$name" : "@name", requirement.Name);
        AddParameter(command, provider == RelationalIndexProvider.Sqlite ? "$table" : "@table", requirement.Table);
        if (provider is RelationalIndexProvider.SqlServer or RelationalIndexProvider.PostgreSql)
            AddParameter(command, "@schema", (object)requirement.Schema ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        string schema = null;
        string table = null;
        string name = null;
        string filter = null;
        bool? unique = null;
        bool enabled = true;
        bool valid = true;
        var columns = new List<(int Ordinal, string Name)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (provider == RelationalIndexProvider.MySql)
            {
                table ??= ReadNullableString(reader, "TABLE_NAME");
                name ??= ReadNullableString(reader, "INDEX_NAME");
                unique ??= Convert.ToInt32(reader["NON_UNIQUE"]) == 0;
                var hasVisible = HasColumn(reader, "IS_VISIBLE");
                var hasIgnored = HasColumn(reader, "IGNORED");
                enabled &= hasVisible || hasIgnored;
                if (hasVisible)
                    enabled &= string.Equals(Convert.ToString(reader["IS_VISIBLE"]), "YES", StringComparison.OrdinalIgnoreCase);
                if (hasIgnored)
                    enabled &= string.Equals(Convert.ToString(reader["IGNORED"]), "NO", StringComparison.OrdinalIgnoreCase);
                var mySqlColumn = ReadNullableString(reader, "COLUMN_NAME");
                valid &= reader["SUB_PART"] is DBNull && !string.IsNullOrWhiteSpace(mySqlColumn);
                if (!string.IsNullOrWhiteSpace(mySqlColumn))
                    columns.Add((Convert.ToInt32(reader["SEQ_IN_INDEX"]), mySqlColumn));
                continue;
            }

            schema ??= ReadNullableString(reader, "SchemaName");
            table ??= ReadNullableString(reader, "TableName");
            name ??= ReadNullableString(reader, "IndexName");
            filter ??= ReadNullableString(reader, "FilterDefinition");
            unique ??= Convert.ToBoolean(reader["IsUnique"]);
            enabled &= Convert.ToBoolean(reader["IsEnabled"]);
            valid &= Convert.ToBoolean(reader["IsValid"]);
            var column = ReadNullableString(reader, "ColumnName");
            if (string.IsNullOrWhiteSpace(column))
                valid = false; // Expression indexes cannot prove the required mapped column identity.
            else
                columns.Add((Convert.ToInt32(reader["Ordinal"]), column));
        }

        return name == null
            ? null
            : new InstalledIndexDefinition(name, schema, table,
                columns.OrderBy(x => x.Ordinal).Select(x => x.Name).ToArray(),
                unique == true, enabled, valid, filter);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string ReadNullableString(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool HasColumn(DbDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string EffectiveSchema(RelationalIndexProvider provider, string configuredSchema)
        => configuredSchema ?? provider switch
        {
            RelationalIndexProvider.SqlServer => "dbo",
            RelationalIndexProvider.PostgreSql => "public",
            _ => null
        };

    private static bool IsSingleColumnNotNullFilter(string filter, string column)
    {
        if (string.IsNullOrWhiteSpace(filter)) return false;
        static string Normalize(string value)
        {
            var result = new StringBuilder(value.Length);
            foreach (var character in value)
                if (!char.IsWhiteSpace(character) && character is not '[' and not ']' and not '"' and not '`'
                    and not '(' and not ')')
                    result.Append(char.ToUpperInvariant(character));
            return result.ToString();
        }

        return Normalize(filter) == Normalize(column + " IS NOT NULL");
    }

    private static bool Fail(string reason, out string mismatch)
    {
        mismatch = reason;
        return false;
    }

    private static string DisplayName(string schema, string table)
        => string.IsNullOrWhiteSpace(schema) ? table : schema + "." + table;
}
