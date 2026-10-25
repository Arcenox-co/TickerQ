using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Single-row "insert if the key is missing" used by TickerQ's EF Core provider. It reproduces the
/// statements FlexLabs.EntityFrameworkCore.Upsert 10.0 generated for <c>Upsert(e).On(keys).NoUpdate()</c>,
/// which TickerQ previously depended on: the same SQL per database family, the same column selection,
/// and parameters created through EF Core's own type mappings. The statement runs on the context's
/// current connection and transaction without touching its change tracker.
/// </summary>
internal static class RelationalInsertIfMissing
{
    internal enum Dialect
    {
        PostgreSql,
        Sqlite,
        MySql,
        SqlServer,
        Oracle,
    }

    internal sealed record Command(string Sql, IReadOnlyList<(IProperty Property, string ParameterName, object Value)> Arguments);

    /// <summary>
    /// Inserts <paramref name="entity"/> unless a row with the same values for
    /// <paramref name="keyPropertyNames"/> exists. Returns the number of inserted rows (0 or 1).
    /// </summary>
    internal static async Task<int> ExecuteAsync<TEntity>(DbContext context, TEntity entity,
        IReadOnlyList<string> keyPropertyNames, CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not part of the TickerQ model.");
        var command = Build(entityType, entity, keyPropertyNames, DialectFor(context.Database.ProviderName));

        var typeMappings = context.GetService<IRelationalTypeMappingSource>();
        await using var dbCommand = context.Database.GetDbConnection().CreateCommand();
        var parameters = command.Arguments
            .Select(a => (object)CreateParameter(dbCommand, typeMappings, a.Property, a.ParameterName, a.Value))
            .ToArray();
        return await context.Database.ExecuteSqlRawAsync(command.Sql, parameters, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static Dialect DialectFor(string providerName) => providerName switch
    {
        "Npgsql.EntityFrameworkCore.PostgreSQL" => Dialect.PostgreSql,
        "Microsoft.EntityFrameworkCore.Sqlite" => Dialect.Sqlite,
        // Microting.EntityFrameworkCore.MySql is the Pomelo fork that targets EF Core 10; it emits the same SQL.
        "MySql.Data.EntityFrameworkCore" or "MySql.EntityFrameworkCore" or "Pomelo.EntityFrameworkCore.MySql"
            or "Microting.EntityFrameworkCore.MySql" => Dialect.MySql,
        "Microsoft.EntityFrameworkCore.SqlServer" => Dialect.SqlServer,
        "Oracle.EntityFrameworkCore" => Dialect.Oracle,
        _ => throw new NotSupportedException(
            $"TickerQ cannot run an insert-if-missing statement for EF Core provider '{providerName}'."),
    };

    internal static Command Build(IEntityType entityType, object entity, IReadOnlyList<string> keyPropertyNames, Dialect dialect)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(entity);
        if (keyPropertyNames == null || keyPropertyNames.Count == 0)
            throw new ArgumentException("At least one key property is required.", nameof(keyPropertyNames));
        // Complex and owned types would add columns this statement does not write; refuse them
        // rather than silently inserting a partial row.
        if (entityType.GetComplexProperties().Any() || entityType.GetNavigations().Any(n => n.TargetEntityType.IsOwned()))
            throw new NotSupportedException($"'{entityType.DisplayName()}' has complex or owned properties.");

        var keys = keyPropertyNames.Select(name => entityType.FindProperty(name)
            ?? throw new InvalidOperationException($"'{entityType.DisplayName()}' has no property '{name}'.")).ToArray();
        if (keys.Any(k => k.ValueGenerated != ValueGenerated.Never))
            throw new InvalidOperationException("Insert-if-missing keys cannot be database-generated.");

        var columns = entityType.GetProperties().Where(p => IsWritable(p, dialect)).ToArray();
        var values = new List<(string Column, string Expression, IProperty Property, string ParameterName, object Value)>(columns.Length);
        for (var i = 0; i < columns.Length; i++)
        {
            var property = columns[i];
            var value = property.GetGetter().GetClrValueUsingContainingEntity(entity);
            string defaultSql = null;
            if (value == null)
            {
                if (property.GetDefaultValue() != null)
                    value = property.GetDefaultValue();
                else
                    defaultSql = property.GetDefaultValueSql();
            }
            var parameterName = dialect == Dialect.Oracle ? $":p{i}" : $"@p{i}";
            // Oracle always binds the parameter; the other dialects inline a column's default SQL.
            var expression = dialect != Dialect.Oracle && defaultSql != null ? defaultSql : parameterName;
            values.Add((property.GetColumnName(), expression, property, parameterName, value));
        }

        var table = TableName(entityType, dialect);
        var columnList = string.Join(", ", values.Select(v => Escape(v.Column, dialect)));
        var valueList = string.Join(", ", values.Select(v => v.Expression));
        var keyColumns = keys.Select(k => (Name: k.GetColumnName(), Nullable: k.IsColumnNullable())).ToArray();
        var sql = new StringBuilder();
        switch (dialect)
        {
            case Dialect.PostgreSql:
            case Dialect.Sqlite:
                sql.Append("INSERT INTO ").Append(table).Append(" AS \"T\" (").Append(columnList)
                    .Append(") VALUES (").Append(valueList).Append(") ON CONFLICT (")
                    .Append(string.Join(", ", keyColumns.Select(k => Escape(k.Name, dialect))))
                    .Append(") DO NOTHING");
                break;
            case Dialect.MySql:
                sql.Append("INSERT IGNORE INTO ").Append(table).Append(" (").Append(columnList)
                    .Append(") VALUES (").Append(valueList).Append(')');
                break;
            case Dialect.SqlServer:
                // HOLDLOCK makes the MERGE race-safe against concurrent inserts of the same key.
                sql.Append("MERGE INTO ").Append(table).Append(" WITH (HOLDLOCK) AS [T] USING ( VALUES (")
                    .Append(valueList).Append(") ) AS [S] (").Append(columnList).Append(") ON ")
                    .Append(string.Join(" AND ", keyColumns.Select(k => k.Nullable
                        ? $"(([S].[{k.Name}] IS NULL AND [T].[{k.Name}] IS NULL) OR ([S].[{k.Name}] IS NOT NULL AND [T].[{k.Name}] = [S].[{k.Name}]))"
                        : $"[T].[{k.Name}] = [S].[{k.Name}]")))
                    .Append(" WHEN NOT MATCHED BY TARGET THEN INSERT (").Append(columnList)
                    .Append(") VALUES (").Append(columnList).Append(");");
                break;
            case Dialect.Oracle:
                sql.Append("MERGE INTO ").Append(table).Append(" t USING (SELECT ")
                    .Append(string.Join(", ", values.Select(v => v.Expression + " AS " + Escape(v.Column, dialect))))
                    .Append(" FROM dual) s ON (")
                    .Append(string.Join(" AND ", keyColumns.Select(k => "t." + Escape(k.Name, dialect) + " = s." + Escape(k.Name, dialect))))
                    .Append(") WHEN NOT MATCHED THEN INSERT (").Append(columnList).Append(") VALUES (")
                    .Append(string.Join(", ", values.Select(v => "s." + Escape(v.Column, dialect)))).Append(')');
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null);
        }

        return new Command(sql.ToString(), values.Select(v => (v.Property, v.ParameterName, v.Value)).ToArray());
    }

    // Same rule FlexLabs applied: skip shadow properties and store-generated columns, and on the
    // PostgreSQL/SQLite family skip identity-always columns.
    private static bool IsWritable(IProperty property, Dialect dialect)
    {
        if (property.ValueGenerated != ValueGenerated.Never && property.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
            return false;
        if (dialect is Dialect.PostgreSql or Dialect.Sqlite &&
            property.FindAnnotation("Npgsql:ValueGenerationStrategy")?.Value?.ToString() == "IdentityAlwaysColumn")
            return false;
        return !property.IsShadowProperty();
    }

    private static string TableName(IEntityType entityType, Dialect dialect)
    {
        var name = entityType.GetTableName() ?? entityType.GetViewName()
            ?? throw new InvalidOperationException($"'{entityType.DisplayName()}' is not mapped to a table.");
        var schema = dialect == Dialect.Sqlite ? null : entityType.GetSchema();
        return (schema == null ? string.Empty : Escape(schema, dialect) + ".") + Escape(name, dialect);
    }

    private static string Escape(string name, Dialect dialect) => dialect switch
    {
        Dialect.MySql => "`" + name + "`",
        Dialect.SqlServer => "[" + name + "]",
        _ => "\"" + name + "\"",
    };

    private static DbParameter CreateParameter(DbCommand command, IRelationalTypeMappingSource typeMappings,
        IProperty property, string name, object value)
    {
        var parameter = typeMappings.FindMapping(property)?.CreateParameter(command, name, value, null, ParameterDirection.Input);
        if (parameter != null)
            return parameter;
        parameter = command.CreateParameter();
        parameter.Direction = ParameterDirection.Input;
        parameter.Value = value ?? DBNull.Value;
        parameter.ParameterName = name;
        return parameter;
    }
}
