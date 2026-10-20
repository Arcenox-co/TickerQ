# Installed-package validation

After packing a complete release artifact set, validate the exact packages a customer
will install:

```sh
python3 scripts/verify-package-consumers.py \
  --packages ./nupkgs --version 10.5.0-rc.1 --framework net10.0 \
  --rid linux-x64 --aot
```

Use the package version/framework for that release lane. The RID must match the runner;
NativeAOT needs the platform's .NET native build prerequisites (on Ubuntu, clang and
zlib development headers). Omit `--aot` and `--rid` for managed-only verification.

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

The managed optional consumer resolves dashboard, integrations and OpenTelemetry wiring,
checks embedded dashboard assets, and inspects EF/MongoDB/Redis registrations without
connecting to those stores or sending notifications. NativeAOT publishes and runs only
the core consumer. This verifies packaging, registration and native startup, not full
provider behavior or licensed scheduler execution; those remain covered by their
separate test suites. Successful temporary workspaces are removed; failed ones remain
at the path printed by the script for diagnosis.
