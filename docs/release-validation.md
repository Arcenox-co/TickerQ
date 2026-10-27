# Installed-package validation

After packing a complete release artifact set, validate the exact packages a customer
will install:

```sh
python3 scripts/verify-package-consumers.py \
  --packages ./nupkgs --version 10.5.1 --framework net10.0 \
  --rid linux-x64 --aot
```

Use the package version/framework for that release lane. The RID must match the runner;
NativeAOT needs the platform's .NET native build prerequisites (on Ubuntu, clang and
zlib development headers). Omit `--aot` and `--rid` for managed-only verification.
Pass `--license /absolute/path/to/tickerq.tqlicense` to additionally run the external,
licensed scheduler consumer. The certificate is read from its original path and is never
copied into the repository, package feed, temporary consumer workspace, or command output.

Optional provider lanes connect to infrastructure supplied by the release job:

```sh
python3 scripts/verify-package-consumers.py \
  --packages ./nupkgs --version 10.5.1 --framework net10.0 \
  --rid linux-x64 --aot \
  --license /run/secrets/tickerq.tqlicense \
  --redis-connection localhost:6379,abortConnect=true \
  --ef-sqlite /tmp/tickerq-package-smoke.db \
  --mongo-connection 'mongodb://localhost:27017/?replicaSet=rs0' \
  --mongo-database tickerq_package_smoke
```

Redis is exercised in both managed and native lanes. EF Core/SQLite and MongoDB are
executed in managed lanes because those providers are not Native AOT-supported. MongoDB
must be a replica set so activation and chain writes can use transactions. Every database
and server passed here must be isolated disposable infrastructure.

The script copies `tests/PackageSmoke` outside the repository, blocks inherited build
properties, and uses a new NuGet cache. Exact version references and package source
mapping force every TickerQ dependency to the staged feed. NuGet.org supplies only
third-party dependencies and SDK runtime/compiler packs. Restored assets are audited
to reject project references, unexpected caches, and mismatched TickerQ versions.

The core consumer starts DI and a host with background scheduling disabled and an
intentionally missing license. It checks that execution remains disallowed, that the
analyzer embedded in the installed Core package registered consumer jobs, and that
request/result metadata works with reflection-based JSON disabled. A direct generated
delegate call checks constructor injection and generated code; no scheduled execution,
license replacement, certificate fixture, or internal test hook is used.

The dashboard consumer starts a real HTTP host, invokes constructor-injected
`ITickerFunction` registrations, round-trips typed request metadata, and calls the
dashboard options endpoint. The basic optional consumer still checks dependency wiring.
The observability consumer saves, reloads and redacts protected integration settings,
renders a Slack preview, executes a licensed job, and observes enqueue/execute/completion
activities. It runs in both managed and native modes without sending a webhook.

With a license, the scheduler lane completes a typed job, a parent/child chain, a cron
occurrence, and cooperative cancellation, then reads the records through dashboard APIs.
The Redis lane repeats typed and chained execution plus dashboard point/list queries
against a real server in both managed and native modes. The managed EF Core and MongoDB
lanes perform the corresponding real persistence and dashboard operations against SQLite
and a MongoDB replica set. These package checks complement the larger provider suites.
Successful temporary
workspaces are removed; failed ones remain at the path printed by the script for diagnosis.
