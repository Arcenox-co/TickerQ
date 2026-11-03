using TickerQ.Tests.Shared.DashboardApi;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure.DashboardApi;

// One class per database, each with its own server in Docker. The function registry is
// process-wide, so no collection runs in parallel with another test.

[CollectionDefinition("PostgreSQL Dashboard API", DisableParallelization = true)]
public sealed class PostgreSqlDashboardApiCollection : ICollectionFixture<PostgreSqlDashboardApiServer>;

[Collection("PostgreSQL Dashboard API")]
public sealed class PostgreSqlDashboardApiTests(PostgreSqlDashboardApiServer server) : DashboardApiContract(server);

[CollectionDefinition("SQL Server Dashboard API", DisableParallelization = true)]
public sealed class SqlServerDashboardApiCollection : ICollectionFixture<SqlServerDashboardApiServer>;

[Collection("SQL Server Dashboard API")]
public sealed class SqlServerDashboardApiTests(SqlServerDashboardApiServer server) : DashboardApiContract(server);

[CollectionDefinition("MySQL Dashboard API", DisableParallelization = true)]
public sealed class MySqlDashboardApiCollection : ICollectionFixture<MySqlDashboardApiServer>;

[Collection("MySQL Dashboard API")]
public sealed class MySqlDashboardApiTests(MySqlDashboardApiServer server) : DashboardApiContract(server);

[CollectionDefinition("MariaDB Dashboard API", DisableParallelization = true)]
public sealed class MariaDbDashboardApiCollection : ICollectionFixture<MariaDbDashboardApiServer>;

[Collection("MariaDB Dashboard API")]
public sealed class MariaDbDashboardApiTests(MariaDbDashboardApiServer server) : DashboardApiContract(server);

[CollectionDefinition("CockroachDB Dashboard API", DisableParallelization = true)]
public sealed class CockroachDbDashboardApiCollection : ICollectionFixture<CockroachDbDashboardApiServer>;

[Collection("CockroachDB Dashboard API")]
public sealed class CockroachDbDashboardApiTests(CockroachDbDashboardApiServer server) : DashboardApiContract(server);

[CollectionDefinition("YugabyteDB Dashboard API", DisableParallelization = true)]
public sealed class YugabyteDbDashboardApiCollection : ICollectionFixture<YugabyteDbDashboardApiServer>;

[Collection("YugabyteDB Dashboard API")]
public sealed class YugabyteDbDashboardApiTests(YugabyteDbDashboardApiServer server) : DashboardApiContract(server);

#if TICKERQ_TEST_ORACLE
[CollectionDefinition("Oracle Dashboard API", DisableParallelization = true)]
public sealed class OracleDashboardApiCollection : ICollectionFixture<OracleDashboardApiServer>;

[Collection("Oracle Dashboard API")]
public sealed class OracleDashboardApiTests(OracleDashboardApiServer server) : DashboardApiContract(server);
#endif
