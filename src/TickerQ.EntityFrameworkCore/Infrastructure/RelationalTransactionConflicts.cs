using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Classifies lock and serialization conflicts reported by relational drivers without referencing
/// them. PostgreSQL-compatible drivers expose these through SQLSTATE; SQL Server, MySQL/MariaDB, and
/// Oracle drivers expose a vendor error number instead, which this reads by name.
/// </summary>
internal static class RelationalTransactionConflicts
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo> NumberProperties = new();

    /// <summary>
    /// True when the database aborted a statement or transaction because of a deadlock, lock timeout,
    /// or serialization conflict, so replaying the whole transaction can succeed.
    /// <paramref name="transactionRolledBack"/> is true only when the database guarantees the entire
    /// transaction was rolled back, which also makes a replay safe after commit had started.
    /// </summary>
    internal static bool IsConflict(DbException exception, out bool transactionRolledBack)
    {
        transactionRolledBack = false;
        if (exception.SqlState is "40001" or "40P01")
        {
            transactionRolledBack = true;
            return true;
        }

        var number = VendorErrorNumber(exception);
        if (number == null) return false;
        var typeName = exception.GetType().FullName ?? string.Empty;

        if (typeName.EndsWith(".SqlException", StringComparison.Ordinal))
        {
            // 1205: chosen as deadlock victim; 3960: snapshot isolation update conflict.
            // Both roll back the whole transaction.
            transactionRolledBack = number is 1205 or 3960;
            return transactionRolledBack;
        }

        if (typeName.EndsWith(".MySqlException", StringComparison.Ordinal))
        {
            // 1213: deadlock, InnoDB rolls back the transaction. 1205: lock wait timeout, which
            // rolls back only the statement unless innodb_rollback_on_timeout is enabled.
            transactionRolledBack = number == 1213;
            return number is 1213 or 1205;
        }

        if (typeName.EndsWith(".OracleException", StringComparison.Ordinal))
        {
            // ORA-08177: cannot serialize access; ORA-00060: deadlock detected; ORA-08176:
            // consistent read failure, raised when a serializable snapshot meets a segment created
            // after it started (for example a table's first insert). Oracle rolls back the failing
            // statement only, so these are safe to replay before commit.
            if (number is 8177 or 60 or 8176)
                return true;
            // ORA-00604 wraps an error raised in recursive SQL, for example a serialization
            // failure while maintaining an index; the real code follows in the message.
            return number == 604 && (exception.Message.Contains("ORA-08177", StringComparison.Ordinal) ||
                                     exception.Message.Contains("ORA-00060", StringComparison.Ordinal) ||
                                     exception.Message.Contains("ORA-08176", StringComparison.Ordinal));
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="exception"/> or an inner exception is a lock or serialization conflict
    /// (see <see cref="IsConflict"/>). Execution strategies wrap transient failures in an
    /// <see cref="InvalidOperationException"/>, so callers must look through the whole chain.
    /// </summary>
    internal static bool IsConflictInChain(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is DbException db && IsConflict(db, out _))
                return true;
        return false;
    }

    /// <summary>
    /// True when <paramref name="exception"/> or an inner exception is a unique or primary-key
    /// violation. Insert-if-missing statements are race-safe on providers with native conflict
    /// clauses, but MERGE-based providers (Oracle) can still report the race this way.
    /// </summary>
    internal static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is not DbException db) continue;
            if (db.SqlState == "23505") return true;
            var number = VendorErrorNumber(db);
            if (number == null) continue;
            var typeName = db.GetType().FullName ?? string.Empty;
            if (typeName.EndsWith(".SqlException", StringComparison.Ordinal) && number is 2627 or 2601) return true;
            if (typeName.EndsWith(".MySqlException", StringComparison.Ordinal) && number == 1062) return true;
            if (typeName.EndsWith(".OracleException", StringComparison.Ordinal) && number == 1) return true;
        }
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Reads the public Number property of the provider's own exception type; when it is trimmed the error is simply not classified.")]
    private static int? VendorErrorNumber(DbException exception)
    {
        var property = NumberProperties.GetOrAdd(exception.GetType(),
            type => type.GetProperty("Number", BindingFlags.Instance | BindingFlags.Public));
        return property?.GetValue(exception) is int number ? number : null;
    }
}
