# Professional Ticker Migration Review-Blocker Plan

> **For Hermes:** Implement each task with strict RED-GREEN-REFACTOR. Do not commit, merge, push, or open a PR without explicit user authorization.

**Goal:** Close the remaining mixed-version, shared-store ownership, stale-definition, third-party provider, and Redis recurring-slot correctness gaps identified by independent review.

**Architecture:** Add an explicit application namespace and monotonic manifest epoch to code-owned CronTicker identity. Persist a definition revision and stamp it on generated occurrences so acquisition can fail closed when stale writers race reconciliation. Require providers to advertise rich reconciliation support, and add a Lua-backed canonical Redis recurring-slot reservation. Keep EF schema migrations consumer-owned and additive.

**Tech stack:** .NET 10, EF Core, MongoDB, StackExchange.Redis/Lua, xUnit.

---

## Invariants

1. A code-owned definition is identified by `(ApplicationNamespace, StableDefinitionId)`, never function name alone.
2. User/dashboard rows without explicit framework ownership are never adopted automatically.
3. A manifest epoch can only move forward. Lower-epoch writers cannot mutate definitions, retirement state, or pending occurrences.
4. Every pending occurrence records the authoritative definition revision. Creation and acquisition reject stale revisions.
5. Providers that do not support rich reconciliation fail startup clearly when code-defined cron reconciliation is required; no lossy legacy projection runs silently.
6. Redis publishes exactly one recurring occurrence for `(CronTickerId, ExecutionTime)` atomically with its entity and scheduler indexes.
7. No migration deletes terminal history, results, leases, or in-flight work.

## Task 1: Namespace and manifest-epoch contracts

**Files:**
- Modify: `src/TickerQ.Utilities/TickerOptionsBuilder.cs`
- Modify: `src/TickerQ.Utilities/Models/CronSeedIdentity.cs`
- Modify: `src/TickerQ.Utilities/Models/DefinedCronSeedManifest.cs`
- Modify: `src/TickerQ.Utilities/Entities/CronTickerEntity.cs`
- Test: `tests/TickerQ.Tests/DefinedCronSeedingPolicyTests.cs`
- Test: `tests/Shared/ProviderReliability/DefinedCronMigrationContractTests.cs`

**RED:** Add tests proving two application namespaces can own the same function independently; ambiguous legacy rows are not adopted; lower epochs cannot overwrite higher epochs.

**Implementation:** Add explicit `ApplicationNamespace` and monotonic `ManifestEpoch` options. Derive `SeedKey` from namespace plus stable definition identity. Persist owner namespace and last-applied epoch as additive nullable fields. Require explicit operator mapping for ambiguous pre-namespace rows; preserve them unchanged by default.

**GREEN:** Run focused shared and InMemory contract tests with a fresh build.

## Task 2: Rich-provider capability and fail-closed startup

**Files:**
- Modify: `src/TickerQ.Utilities/Interfaces/ITickerPersistenceProvider.cs`
- Modify: built-in provider implementations
- Modify: `src/TickerQ/Src/BackgroundServices/TickerQInitializerHostedService.cs`
- Test: `tests/TickerQ.Tests/DesignTimeToolDetectionTests.cs`
- Create: prior-contract provider compatibility fixture under `tests/TickerQ.Tests`

**RED:** Add a provider compiled/represented with only the legacy tuple method. Assert startup refuses rich reconciliation with a precise compatibility error rather than invoking the legacy destructive method.

**Implementation:** Add `SupportsAuthoritativeCronReconciliation => false` compatibility capability. Built-ins return true. Initializer invokes rich reconciliation only when supported; if code-defined seeds exist and capability is false, fail before mutation. Keep legacy APIs callable explicitly for source/binary compatibility.

**GREEN:** Run focused startup/compatibility tests.

## Task 3: Definition revision fencing

**Files:**
- Modify: `src/TickerQ.Utilities/Entities/CronTickerEntity.cs`
- Modify: `src/TickerQ.Utilities/Entities/CronTickerOccurrenceEntity.cs` or its base model
- Modify: all four built-in providers
- Modify: scheduler occurrence generation/acquisition paths
- Modify: EF configurations and sample migrations/snapshots
- Test: shared provider reliability contracts plus provider-specific race tests

**RED:** Pause an old-revision writer, reconcile a new expression/contract, resume the old writer, and assert its occurrence cannot be acquired or executed. Cover lower manifest epoch, expression change, blocked seed, and old-node restart.

**Implementation:** Increment/persist `DefinitionRevision` on material definition changes. Stamp occurrences at atomic creation. Creation must compare expected current revision; acquisition refreshes the authoritative definition and rejects/quarantines mismatches. Invalidate/version EF caches by revision.

**GREEN:** Run provider-focused stale-writer tests on EF, Mongo replica-set and standalone paths, real Redis, and InMemory.

## Task 4: Redis canonical recurring-slot publication

**Files:**
- Modify: `src/TickerQ.Caching.StackExchangeRedis/Helpers/RedisKeyBuilder.cs`
- Create: `src/TickerQ.Caching.StackExchangeRedis/Scripts/AddCronOccurrenceOnce.lua`
- Modify: `src/TickerQ.Caching.StackExchangeRedis/Infrastructure/TickerRedisPersistenceProvider.cs`
- Modify: occurrence deletion/reschedule scripts to release slots with ID/revision compare-and-delete
- Test: `tests/TickerQ.Caching.StackExchangeRedis.Tests/Infrastructure/RedisRealScriptTests.cs`

**RED:** Barrier two real Redis clients creating the same slot and assert one canonical entity, one slot owner, one reverse index member, and one pending member. Add crash/retry and ABA reschedule/delete tests.

**Implementation:** Use a canonical slot key containing cron ID and UTC execution ticks. One Lua script reserves the slot and publishes entity/all-ID/reverse/pending indexes atomically. Return the committed occurrence ID to losers. Release only when slot value matches the occurrence ID and definition revision.

**GREEN:** Run real-Redis race test repeatedly and full Redis suite.

## Task 5: Consumer-owned schema artifacts and rollout activation

**Files:**
- Modify: EF mappings
- Regenerate three sample migrations/snapshots
- Update: `docs/upgrading-cron-time-tickers.md`
- Update: provider READMEs
- Test: `EfTimeTickerStoreUpgradeTests` and migration-application verification

**RED:** Add pre-upgrade fixtures proving missing namespace/epoch/revision schema fails with actionable diagnostics and that lower-epoch rollout cannot activate.

**Implementation:** Add nullable owner namespace, manifest epoch, definition revision, and occurrence revision columns/indexes. Keep migrations host-owned. Document two safe activation modes: drain old writers before raising epoch, or deploy binaries capable of honoring the gate before activation. Remove any claim that additive shape alone makes mixed writers safe.

**GREEN:** Build all samples, run `has-pending-model-changes`, and apply each full migration chain to a fresh database.

## Final verification gates

1. Focused RED/GREEN evidence for every task.
2. Full core, EF, MongoDB, and Redis suites with fresh builds.
3. Full solution build using `PROTOBUF_PROTOC=/opt/homebrew/bin/protoc`.
4. Repeated real-provider concurrency loops.
5. `git diff --check`, secret scan, and local documentation-link validation.
6. Independent review against the current worktree—not baseline plan text.
7. No commit/push/PR until explicit authorization.

## Risks and trade-offs

- Namespace/epoch configuration is a new operational contract. A default namespace is convenient but unsafe for shared stores; durable mode should require an explicit value when code-owned seeds are present.
- True protection from pre-gate binaries is impossible: binaries that do not know the epoch cannot honor it. Safe first adoption therefore requires draining old writers before activation. The epoch protects subsequent rollouts once the whole fleet supports it.
- Definition revision adds storage and index churn but is required to prevent stale generated work from executing.
- Redis Cluster requires all Lua keys to share a hash slot; key design must preserve cluster compatibility or explicitly declare standalone-only support.
