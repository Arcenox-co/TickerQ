# Application-Partitioned Runtime State Implementation Plan

> **For Hermes:** Implement task-by-task with strict RED-GREEN-REFACTOR, independent spec review, then code-quality review. Do not commit, push, package, or publish without explicit user authorization.

**Goal:** Physically partition all TickerQ runtime state by canonical application namespace so unrelated applications can safely share one EF, MongoDB, Redis, or in-memory backend without fencing, repairing, acquiring, or mutating each other’s work.

**Architecture:** Treat the canonical application namespace as part of every runtime identity and storage key. Activation remains application-scoped, but every runnable document/row/key, graph edge, scheduler index, result sidecar, terminal-evidence record, outbox record, reconciliation write, retention/recovery operation, and readiness repair is constrained to the same immutable partition. Namespaced queue-only producers write to that partition without activation admission; scheduler-enabled providers additionally require exact `Activated` epoch for public runnable mutations. Initializer-only TimeTicker seeding receives an internal ambient capability permitting the exact construction-bound namespace/epoch during `Activating`.

**Tech Stack:** .NET 10, EF Core 10, MongoDB.Driver, StackExchange.Redis/Lua, xUnit, Testcontainers.

---

## Non-negotiable invariants

1. Runtime identity is `(ApplicationNamespace, TickerType, TickerId)`; no provider query or mutation may match only `TickerId` when a runtime namespace is bound.
2. A provider snapshots one canonical runtime namespace, epoch, and scheduler mode at construction. Protocol calls and manifests cannot redirect it.
3. Scheduler mode permits runnable writes only at exact `Activated` epoch, except initializer-only startup seeding at exact `Activating` epoch.
4. Queue-only mode ignores activation metadata but still writes only its configured namespace partition. Namespace-less queue-only mode is the explicit legacy partition.
5. Structural schema/index bootstrap may be store-global and idempotent; runtime repair/reconciliation/data migration is partition-local.
6. Legacy global runtime rows may be adopted only through explicit single-owner configuration and a store-global adoption lease. Never infer ownership.
7. Terminal replay authorization and current generation authority must be decided atomically in the same partition transaction/script.
8. Public Cron semantic updates increment `DefinitionRevision` and atomically quarantine prior-revision unleased occurrences.
9. All Idle/Queued publication, release, recovery, replacement, and unified-context writes use exact admission plus graph/revision/owner-token fencing.
10. Existing public compatibility overloads retain baseline default-interface bridges without recursive defaults.

---

### Task 1: Freeze the partition contract in shared models

**Objective:** Introduce one canonical runtime partition value and make provider construction snapshot it consistently.

**Files:**
- Create: `src/TickerQ.Utilities/Models/TickerQRuntimePartition.cs`
- Modify: `src/TickerQ.Utilities/TickerOptionsBuilder.cs`
- Modify: `src/TickerQ.Utilities/Models/ReconciliationActivationScope.cs`
- Modify: `src/TickerQ/DependencyInjection/TickerQServiceExtensions.cs`
- Test: `tests/TickerQ.Tests/RuntimeScopeAndManifestHardeningTests.cs`
- Test: `tests/TickerQ.Tests/TickerOptionsBuilderTests.cs`

**Steps:**
1. RED: add tests proving namespace normalization is immutable, bounded, deterministic, and shared by scheduler and queue-only providers.
2. RED: prove namespace-less scheduler construction is invalid while namespace-less queue-only construction maps only to an explicit `LegacyGlobal` partition.
3. Implement a sealed canonical partition value with length-prefixed SHA-256 storage key plus retained display namespace.
4. Snapshot `RuntimePartition`, `RuntimeSchedulerEnabled`, and `ReconciliationEpoch` when options are bound; later option mutation must throw.
5. GREEN: run the focused Core tests.

**Verification:**
`dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj --filter 'FullyQualifiedName~RuntimeScopeAndManifestHardeningTests|FullyQualifiedName~TickerOptionsBuilderTests'`

---

### Task 2: Add initializer-only startup-seeding admission

**Objective:** Allow configured startup TimeTicker seeders to publish during the exact `Activating` epoch without opening public pre-start admission.

**Files:**
- Create: `src/TickerQ.Utilities/Models/StartupSeederAdmissionContext.cs`
- Modify: `src/TickerQ/Src/BackgroundServices/TickerQInitializerHostedService.cs`
- Modify: provider admission helpers in EF, MongoDB, Redis, and in-memory
- Test: `tests/TickerQ.Tests/ReconciliationActivationProtocolTests.cs`
- Test: provider activation test classes

**Steps:**
1. RED: configure `UseTickerSeeder`, start a fresh scheduler, and prove the seed is durably present before activation commit on all providers.
2. RED: resolve the same public manager before `StartAsync`; prove identical mutation remains denied.
3. Implement an ambient async-flow capability whose constructor/setter is inaccessible to consumer code; bind exact partition and epoch.
4. Enter that capability only around initializer seeder execution and clear it in `finally`.
5. Provider runnable admission accepts either exact `Activated`, or exact `Activating` plus matching startup capability.
6. Seeder failures must abort activation; same-epoch restart behavior must remain deterministic.
7. GREEN: run Core and provider-focused seeder tests.

---

### Task 3: Restore public interface source/binary compatibility

**Objective:** Preserve compatibility bridges while keeping authoritative manifest support explicit.

**Files:**
- Modify: `src/TickerQ.Utilities/Interfaces/ITickerPersistenceProvider.cs`
- Modify: `hub/sdks/dotnet/TickerQ.SDK/Persistence/TickerQRemotePersistenceProvider.cs`
- Test: `tests/TickerQ.Tests/PersistenceReliabilityCapabilityTests.cs`
- Create: compile-contract fixture under `tests/TickerQ.Tests/Compatibility/`

**Steps:**
1. RED: compile a provider that overrides only `DefinedCronTickerSeed[]` and relies on tuple default bridging.
2. Restore the baseline tuple overload default body by projecting tuples to seeds.
3. Ensure richer overload defaults do not recurse when neither overload is overridden; fail closed with a precise capability exception.
4. Ensure remote provider advertises non-authoritative reconciliation and cannot reach unsupported manifest migration at startup.
5. GREEN: run compatibility fixture and Core tests.

---

### Task 4: Bind manifests to the immutable runtime partition

**Objective:** Prevent arbitrary public protocol callers from reconciling another namespace.

**Files:**
- Modify: `DefinedCronSeedManifest` ingress in in-memory, EF, MongoDB, and Redis providers
- Test: shared `tests/Shared/ProviderReliability/DefinedCronMigrationContractTests.cs`
- Test: each provider migration-contract class

**Steps:**
1. RED: provider constructed for A rejects manifest B before any write.
2. RED: queue-only legacy migration requires explicit legacy mode; null/global inference is rejected.
3. Validate exact manifest namespace against construction-bound partition before entering provider-native transaction/script.
4. Require initializer capability at exact `Activating` epoch for scheduler authoritative reconciliation.
5. GREEN: run all four migration-contract suites.

---

### Task 5: Partition in-memory runtime state

**Objective:** Make the reference provider demonstrate complete cross-application isolation.

**Files:**
- Modify: `src/TickerQ/Src/Provider/TickerInMemoryPersistenceProvider.cs`
- Test: `tests/TickerQ.Tests/TickerInMemoryActivationEpochTests.cs`
- Create: `tests/TickerQ.Tests/TickerInMemoryApplicationPartitionTests.cs`

**Steps:**
1. RED: A and B may use identical GUIDs without cross-read, cross-acquire, cross-repair, or cross-result exposure.
2. RED: A activation/repair cannot block or mutate B.
3. Partition all dictionaries/indexes/evidence/outbox/graph state by immutable partition key.
4. Apply exact partition predicates to CRUD, acquisition, retention, recovery, terminal replay, and repair.
5. GREEN: run all in-memory provider tests.

---

### Task 6: Add EF partition columns and host-owned migrations

**Objective:** Persist partition ownership on every EF runtime table without framework-owned migration history.

**Files:**
- Modify entity/configuration files under `src/TickerQ.EntityFrameworkCore/`
- Modify sample migrations and snapshots in ApplicationDbContext, Console, and WebApi samples
- Test: `EfTimeTickerStoreUpgradeTests.cs`, `CronTickerConfigurationTests.cs`, `SampleMigrationRollbackContractTests.cs`

**Steps:**
1. RED: model metadata requires non-null bounded `ApplicationNamespaceKey` on definitions, occurrences, TimeTickers, results, metadata, and outbox rows.
2. RED: required composite indexes begin with namespace key for runnable scans and uniqueness.
3. Add partition columns/configuration without adding provider-owned concrete migrations.
4. Generate/update only host sample migrations and snapshots.
5. Prove upgrade and rollback preserve host migration history and existing rows.
6. Require explicit legacy owner option before assigning legacy rows to a namespace.
7. GREEN: run EF upgrade/configuration/rollback tests and EF 10 pending-model checks.

---

### Task 7: Route every EF operation through partitioned admission and graph boundaries

**Objective:** Close all EF runnable publication and retry gaps.

**Files:**
- Modify: `BasePersistenceProvider.cs`
- Modify: `TickerEFCorePersistenceProvider.cs`
- Modify: `EfCoreDataMigrationPipeline.cs`
- Test: EF activation, revision, persistence, PostgreSQL race, and store-upgrade classes

**Steps:**
1. RED: public Cron insert/update before activation fails; semantic update increments revision and quarantines old occurrences.
2. RED: unified Idle writes require partition + activation + graph sentinel + owner/token + authoritative root generation.
3. RED: dead-node descendant cleanup cannot mutate after root generation advances.
4. RED: retention retry injects `40001` and proves a fresh DbContext/transaction per attempt.
5. Add partition predicate to every read/write/delete/index scan.
6. Route all Idle/Queued release/recovery/replacement paths through `ExecuteTimeTickerGraphMutationAsync` in sentinel→activation→root order.
7. Move retention strategy creation to a short-lived context and create all attempt state inside the delegate.
8. GREEN: run full EF suite with PostgreSQL opt-in.

---

### Task 8: Partition MongoDB collections and transaction predicates

**Objective:** Ensure every Mongo runtime mutation is partition-local and transactionally admitted.

**Files:**
- Modify: `TickerMongoContext.cs`, `ITickerMongoContext.cs`, `TickerIndexProvisioner.cs`, `TickerMongoPersistenceProvider.cs`
- Modify Mongo entity/document mappings as required
- Test: Mongo activation, revision, result, retention, outbox, and partition tests

**Steps:**
1. RED: A/B identical IDs remain isolated across definitions, occurrences, TimeTickers, results, evidence, outbox, and graph repair.
2. RED: public Cron CRUD uses activation-locked transactions, revision 1 on insert, CAS increment on semantic update, and old-occurrence quarantine.
3. RED: every release/dead-node/stale/chain-replacement Idle write holds partition activation and graph/parent locks.
4. RED: exact terminal replay racing B reacquisition returns false.
5. Add partition field and compound unique/runnable indexes to every document type.
6. Apply partition predicate to all filters and transactional lock documents.
7. Validate replay evidence and current authority in one transaction, or supersede evidence atomically in reacquisition.
8. Register Mongo transaction capability probing as repeatable process-local readiness for same-epoch followers.
9. Standalone scoped scheduler remains fail-closed where cross-document atomicity is required.
10. GREEN: run full Mongo suite.

---

### Task 9: Namespace every Redis key and index

**Objective:** Make application partition part of every Redis hash tag/key while preserving cluster slot guarantees.

**Files:**
- Modify: `RedisKeyBuilder.cs`
- Modify: `BaseRedisPersistenceProvider.cs`, `TickerRedisPersistenceProvider.cs`
- Modify all Lua scripts under `src/TickerQ.Caching.StackExchangeRedis/Scripts/`
- Test: Redis real-script, activation, manager, result, outbox, heartbeat, and partition tests

**Steps:**
1. RED: A/B identical IDs produce different keys and never cross-read/acquire/repair/evidence/outbox.
2. Build all keys from the immutable hashed partition; preserve a common per-partition Redis Cluster hash tag.
3. Pass explicit `scoped|queue-only|invalid|startup-seeder` admission mode to every runnable-mutation script.
4. Replace unconditional `UpdateTimeTickers` SET/index sequence with one Lua CAS for standalone/scoped mode.
5. Add queue-only Cluster-safe Cron producer behavior or explicitly reject it at provider construction; do not claim partial compatibility.
6. Public Cron updates CAS/increment revision and quarantine prior-revision pending occurrences atomically.
7. Queue-only operations ignore activation metadata; invalid scheduler mode always rejects.
8. Bound terminal evidence per partition and reject replay after any different current generation.
9. GREEN: run full Redis suite including real Lua and Cluster-mode regressions.

---

### Task 10: Partition results, outbox, maintenance, and background services end-to-end

**Objective:** Ensure no non-provider caller reconstructs global identities or scans another partition.

**Files:**
- Modify Core background services, execution context, managers, heartbeat, retention, recovery, and remote callback paths
- Modify shared internal context models to carry immutable partition identity where needed
- Test: Core service/gate/retention/recovery and provider integration tests

**Steps:**
1. RED: A maintenance service cannot discover or mutate B work even with identical IDs/owners.
2. Propagate partition through internal contexts and remote callback identity.
3. Require exact partition in terminal-result and Node finalization APIs.
4. Ensure heartbeat/dead-node ownership is partitioned.
5. Ensure same-epoch readiness probes run per process and per partition.
6. GREEN: run focused Core and provider integration tests.

---

### Task 11: Explicit legacy adoption protocol

**Objective:** Upgrade existing global stores without guessing ownership.

**Files:**
- Modify options, initializer, provider migration pipelines, docs, and sample migrations
- Create provider-specific adoption tests

**Steps:**
1. Add explicit `UseLegacyRuntimePartitionAdoption()` (name subject to API review) requiring a target namespace and positive epoch.
2. Acquire a store-global adoption lease before inspecting legacy rows.
3. Reject adoption if namespaced runtime rows already exist for multiple owners or another adopter holds/completed the lease.
4. Stamp all legacy rows/keys/results/outbox/graphs atomically or resumably with durable checkpoints.
5. Make adoption idempotent and crash-resumable.
6. Document that two applications cannot both claim the same legacy global runtime state.
7. GREEN: run upgrade/interruption/restart tests per provider.

---

### Task 12: Documentation and consumer contract

**Objective:** Make isolation, topology, queue-only behavior, and upgrade requirements explicit.

**Files:**
- Modify: `README.md`
- Modify: `docs/upgrading-cron-time-tickers.md`
- Modify provider READMEs and all sample `Program.cs` files

**Steps:**
1. Document namespace as physical runtime partition, not only seed identity.
2. Document scheduler vs queue-only admission and Redis Cluster support matrix.
3. Document explicit legacy adoption and one-owner limitation.
4. Show multi-application shared-store examples and identical-ID isolation.
5. Build all nine modified samples.

---

### Task 13: Canonical verification and independent approval

**Objective:** Obtain trustworthy release-gate evidence on the final tree.

**Steps:**
1. Run `git diff --check`, exact conflict-marker scan, added-secret scan, and stale verifier cleanup.
2. Run EF 10 pending-model checks for all three sample contexts.
3. Apply upgrade/rollback migrations to disposable databases and verify host migration history.
4. Build all nine modified sample hosts.
5. Run fresh serial solution build.
6. Run full serial solution tests with `TICKERQ_POSTGRES_RACE_TESTS=1` and retain per-project totals.
7. Run explicit real Redis, Mongo replica-set/standalone, PostgreSQL, SQL Server catalog, Redis Cluster, and cross-application partition tests.
8. Dispatch fresh independent P0/P1 reviews for shared/Core, EF, and Mongo/Redis.
9. If any P0/P1 remains, reopen remediation; do not create an artifact.
10. Only after approval, ask the user whether to commit/push/package.

---

## Principal risks and decisions

- **Breaking storage change:** Physical partitioning requires schema/key migration. Mitigate with explicit resumable adoption, never implicit assignment.
- **Redis Cluster:** Cross-key atomicity is valid only when every per-partition key shares one hash tag. The key builder must enforce this centrally.
- **Public IDs:** GUID remains consumer-facing, but every provider predicate includes partition. Cross-partition lookup by GUID alone returns not found.
- **Remote execution:** Remote callback protocol may need partition identity. Treat omission as fail-closed, not legacy global fallback.
- **Schema bootstrap:** Index/table creation may remain store-global; runtime data repair may not.
- **Scale:** Per-partition indexes/keys increase storage cardinality but eliminate global scans and cross-app locking.
- **Compatibility:** Default-interface methods must remain source-compatible; storage compatibility is handled by explicit adoption rather than silent behavior.

## Exit criteria

- Two applications sharing one backend can use identical ticker IDs without any cross-visible row/key/result/outbox/evidence.
- Starting, upgrading, repairing, retaining, recovering, or fencing application A cannot block or mutate B.
- Public managers are fail-closed before scheduler activation, while namespaced queue-only managers work before host start.
- Startup seeders succeed during exact activating epoch and failures abort activation.
- Every semantic Cron update revisions and quarantines stale pending work atomically.
- Terminal replay racing reacquisition is rejected atomically on all providers.
- Full canonical verification passes and all three independent reviewers return `APPROVED`.
