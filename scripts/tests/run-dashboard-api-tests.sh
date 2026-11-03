#!/usr/bin/env bash
# Runs the Dashboard API suite (tests/Shared/DashboardApi) against real databases in Docker.
# Each database's test class starts its own container, so only Docker needs to be running.
#
#   scripts/tests/run-dashboard-api-tests.sh                 # every database
#   scripts/tests/run-dashboard-api-tests.sh postgresql redis
#
# Databases: postgresql sqlserver mysql mariadb oracle cockroachdb yugabytedb mongodb redis
set -uo pipefail

cd "$(dirname "$0")/../.."
export TICKERQ_DASHBOARD_API_TESTS=1

all="postgresql sqlserver mysql mariadb oracle cockroachdb yugabytedb mongodb redis"
targets="${*:-$all}"
ef=tests/TickerQ.EntityFrameworkCore.Tests

if ! docker info >/dev/null 2>&1; then
  echo "Docker is not running." >&2
  exit 1
fi

failed=""
for target in $targets; do
  case "$target" in
    postgresql)  project=$ef; class=DashboardApi.PostgreSqlDashboardApiTests ;;
    sqlserver)   project=$ef; class=DashboardApi.SqlServerDashboardApiTests ;;
    mysql)       project=$ef; class=DashboardApi.MySqlDashboardApiTests ;;
    mariadb)     project=$ef; class=DashboardApi.MariaDbDashboardApiTests ;;
    oracle)      project=$ef; class=DashboardApi.OracleDashboardApiTests ;;
    cockroachdb) project=$ef; class=DashboardApi.CockroachDbDashboardApiTests ;;
    yugabytedb)  project=$ef; class=DashboardApi.YugabyteDbDashboardApiTests ;;
    mongodb)     project=tests/TickerQ.MongoDB.Tests; class=MongoDashboardApiTests ;;
    redis)       project=tests/TickerQ.Caching.StackExchangeRedis.Tests; class=RedisDashboardApiTests ;;
    *) echo "Unknown database '$target'. Choose from: $all" >&2; exit 1 ;;
  esac
  echo "== $target"
  dotnet test "$project" --nologo --filter "FullyQualifiedName~$class" || failed="$failed $target"
done

if [ -n "$failed" ]; then
  echo "Failed:$failed" >&2
  exit 1
fi
echo "Every database passed."
