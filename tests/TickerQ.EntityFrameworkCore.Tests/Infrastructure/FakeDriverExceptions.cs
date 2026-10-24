using System.Data.Common;

namespace TickerQ.EntityFrameworkCore.Tests.FakeDrivers;

// Stand-ins named like the real driver exceptions; the classifier matches on type name and a
// public Number property, so no driver package is needed.
public sealed class SqlException(int number) : DbException("sql server")
{
    public int Number { get; } = number;
}

public sealed class MySqlException(int number) : DbException("mysql")
{
    public int Number { get; } = number;
}

public sealed class OracleException(int number, string message) : DbException(message)
{
    public int Number { get; } = number;
}

public sealed class NpgsqlLikeException(string sqlState) : DbException("postgres")
{
    public override string SqlState { get; } = sqlState;
}
