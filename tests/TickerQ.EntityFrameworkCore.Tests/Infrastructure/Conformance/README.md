# Relational conformance suite

These tests check that a relational database can run TickerQ's EF Core provider. They are opt-in:
each target runs only when its administrator connection string is set, and every test creates and
drops its own throwaway database (or Oracle user).

| Test class | Covers | Requires |
| --- | --- | --- |
| `RelationalConformanceTests` | Schema certification, index verification, activation metadata, concurrent writers and acquirers, the nullable unique `SeedKey` index, column round-trips, concurrent seed reconciliation, schedule policy storage | A target connection string |
| `RelationalAssistantConformanceTests` | The assistant investigation store with empty and missing text: placeholders in required columns, `""` for optional ones, no reuse of an empty diagnosis, and delete tombstones | A target connection string |
| `RelationalRuntimeConformanceTests` | A real TickerQ host running an immediate time ticker, a two-step chain, and a per-second cron | A target connection string and `TICKERQ_CONFORMANCE_LICENSE` |

## Targets

| Variable | Example administrator connection string |
| --- | --- |
| `TICKERQ_CONFORMANCE_POSTGRESQL` | `Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=...` |
| `TICKERQ_CONFORMANCE_SQLSERVER` | `Server=127.0.0.1,1433;User Id=sa;Password=...;Encrypt=False;TrustServerCertificate=True` |
| `TICKERQ_CONFORMANCE_MYSQL` | `Server=127.0.0.1;Port=3306;User=root;Password=...` |
| `TICKERQ_CONFORMANCE_MARIADB` | `Server=127.0.0.1;Port=3306;User=root;Password=...` |
| `TICKERQ_CONFORMANCE_COCKROACHDB` | `Host=127.0.0.1;Port=26257;Database=defaultdb;Username=root;SSL Mode=Disable;Options=-c autocommit_before_ddl=false` |
| `TICKERQ_CONFORMANCE_YUGABYTEDB` | `Host=127.0.0.1;Port=5433;Database=yugabyte;Username=yugabyte;Password=yugabyte;Timeout=60;Command Timeout=120` |
| `TICKERQ_CONFORMANCE_ORACLE` | `User Id=system;Password=...;Data Source=127.0.0.1:1521/FREEPDB1` |

Set `TICKERQ_CONFORMANCE_KEEP=1` to keep the throwaway databases for inspection after a failure.

## Local containers

```bash
docker run -d --name tq-mariadb -e MARIADB_ROOT_PASSWORD=pw -p 3306:3306 mariadb:11.4
docker run -d --name tq-cockroach -p 26257:26257 cockroachdb/cockroach:latest-v25.2 start-single-node --insecure
docker run -d --name tq-oracle -e ORACLE_PASSWORD=pw -p 1521:1521 gvenzl/oracle-free:23-slim

# yugabyted aborts when the image has no chronyc binary; a stub lets its prerequisite check continue.
docker run -d --name tq-yugabyte -p 5433:5433 --entrypoint /bin/bash yugabytedb/yugabyte:2024.2.2.0-b70 \
  -c "printf '#!/bin/sh\nexit 1\n' > /usr/local/bin/chronyc && chmod +x /usr/local/bin/chronyc && exec bin/yugabyted start --background=false"
```

Run one target at a time:

```bash
TICKERQ_CONFORMANCE_MARIADB="Server=127.0.0.1;Port=3306;User=root;Password=pw" \
  dotnet test tests/TickerQ.EntityFrameworkCore.Tests --filter "FullyQualifiedName~Conformance"
```

## Notes

- Tables are created with `IRelationalDatabaseCreator.CreateTablesAsync()`, not `EnsureCreated()`, which
  skips table creation on CockroachDB.
- The suite keeps CockroachDB in the `public` schema and MySQL, MariaDB, and Oracle in the throwaway
  database or user, matching the requirements documented for those databases.
