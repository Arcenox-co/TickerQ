using System;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Makes EF Core bulk deletes portable to MariaDB. The MySQL EF Core provider generates the
/// single-table form <c>DELETE FROM `t` AS `s` WHERE ...</c>, which MySQL 8.0.16+ accepts but MariaDB
/// rejects. The equivalent multi-table form <c>DELETE `s` FROM `t` AS `s` WHERE ...</c> runs on both.
/// The interceptor is inert for every other connection type and statement shape.
/// </summary>
internal sealed partial class MySqlDeleteAliasInterceptor : DbCommandInterceptor
{
    public static MySqlDeleteAliasInterceptor Instance { get; } = new();

    // Matches only the provider's aliased single-table delete at the start of a statement.
    [GeneratedRegex(@"^(\s*)DELETE\s+FROM\s+((?:`[^`]+`\.)?`[^`]+`)\s+AS\s+(`[^`]+`)(?=\s)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AliasedDelete();

    internal static string Rewrite(string commandText)
    {
        if (string.IsNullOrEmpty(commandText) ||
            commandText.IndexOf("DELETE", StringComparison.OrdinalIgnoreCase) < 0)
            return commandText;
        // The multi-table form does not allow ORDER BY or LIMIT; leave such statements unchanged.
        if (commandText.Contains(" LIMIT ", StringComparison.OrdinalIgnoreCase) ||
            commandText.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
            return commandText;
        return AliasedDelete().Replace(commandText, "$1DELETE $3 FROM $2 AS $3");
    }

    private static void Apply(DbCommand command)
    {
        // Matches MySql.Data and MySqlConnector without referencing either driver.
        if (command.Connection?.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
            command.CommandText = Rewrite(command.CommandText);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Apply(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Apply(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Apply(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Apply(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Apply(command);
        return ValueTask.FromResult(result);
    }
}
