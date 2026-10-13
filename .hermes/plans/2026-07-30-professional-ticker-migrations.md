# Professional CronTicker and TimeTicker Upgrades Implementation Plan

> **For Hermes:** Implement through strict vertical RED-GREEN-REFACTOR slices and independent review gates.

**Goal:** Provide production-safe, consumer-usable TickerQ schema/data upgrades and startup reconciliation across EF Core, MongoDB, Redis, and InMemory without duplicate schedules, destructive history loss, or broken legacy chains.

**Architecture:** Add a stable seed ownership key, database-authoritative atomic upserts, two-phase cron retirement, pending-only schedule reconciliation, provider bootstrappers for data repair/version validation, and documented EF consumer migrations. Keep all schema changes additive and preserve existing IDs/history.

**Tech stack:** .NET 10, EF Core SQLite/PostgreSQL, MongoDB.Driver, StackExchange.Redis Lua, xUnit/Testcontainers.

---

## Non-negotiable invariants

1. Existing CronTicker/TimeTicker primary keys are never rewritten.
2. Exactly one active code-owned cron exists per stable seed key after convergence.
3. User/dashboard/SDK-created rows are never claimed by code reconciliation.
4. Retirement never deletes in-flight or terminal occurrences/results.
5. Expression/contract changes affect only unleased pending occurrences.
6. Legacy TimeTicker descendants are backfilled and runtime-self-healed before fenced child writes.
7. Provider prerequisite/index/data repair completes before scheduler polling.
8. Every storage mutation plus discoverability index is atomic or repairable and idempotent.
9. All new persisted fields are additive so the prior release can read the upgraded store during rollout.
10. New binaries reject stores with a newer unsupported data version; old binaries cannot enforce a gate they do not know, so backward safety comes from additive shape and rollout policy.

## Slice 1: Freeze public reconciliation semantics

**Tests first**
- Extend `tests/TickerQ.Tests/DefinedCronSeedingPolicyTests.cs`:
  - removing only a cron expression removes the function from the desired seed manifest;
  - required empty-request functions produce a blocked seed rather than disappearing silently;
  - initializer forwards host cancellation.
- Add provider-neutral desired-state contract tests under `tests/Shared/ProviderReliability/DefinedCronMigrationContractTests.cs`.

**Implementation**
- Replace the provider API's ambiguous `DefinedCronTickerSeed[] + allRegisteredFunctions` semantics with a manifest model containing desired local seeds, blocked local seeds, and protected remote/non-local functions.
- Keep a compatibility bridge on `IInternalTickerManager`; avoid a public breaking change where possible.
- Orphan detection compares code-owned rows to desired local seed keys, never to the global runtime registry.

**Verification**
`dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj --filter "DefinedCronSeedingPolicy|DefinedCronMigrationContract"`

## Slice 2: Stable seed ownership and concurrent convergence

**Tests first**
- Two concurrent migrations converge to one active code-owned row.
- Existing legacy seeded row retains its primary key and adopts `SeedKey`.
- Duplicate legacy seeded rows select a deterministic canonical row; extras enter retirement, never crash Redis.
- User rows sharing the same function are untouched.

**Implementation**
- Add nullable `SeedKey`, `SeedLastSeenAt`, `RetirementRequestedAt`, and `RetiredAt` to `CronTickerEntity`.
- `SeedKey` is deterministic from the local code definition identity; expression is not part of identity.
- Existing rows adopt `SeedKey` in place. New rows may use a deterministic ID, but existing IDs never change.
- EF: filtered unique index on non-null `SeedKey`; transactional reconcile with unique-race retry/re-read.
- Mongo: unique partial index on `SeedKey`; atomic upsert.
- Redis: deterministic seed ownership key plus Lua atomic entity/index write; duplicate-tolerant convergence.
- InMemory: `GetOrAdd` under seed-key synchronization.

## Slice 3: Two-phase non-destructive retirement

**Tests first**
- Missing desired seed records `RetirementRequestedAt` but remains enabled during grace.
- Reappearance clears retirement request.
- After grace, definition is disabled/retired; unleased pending occurrences are cancelled/removed.
- Leased/InProgress and terminal occurrences/results survive.
- Blocked required-request seeds pause new scheduling without deleting existing work/history.

**Implementation**
- Add configurable retirement grace with a conservative default and explicit immediate-finalization opt-in for controlled single-node deployments.
- Phase A: mark retirement requested.
- Phase B after grace: disable/retire definition and remove/cancel only unleased pending occurrences.
- Retention remains responsible for terminal history cleanup.
- Scheduler queries exclude retired/disabled definitions but dashboards can still query them.

## Slice 4: Expression and contract reconciliation

**Tests first**
- Expression change removes/recalculates only unleased pending occurrences.
- Leased/InProgress and terminal occurrences remain unchanged.
- Redis pending occurrences execute with the current definition/contract identity.

**Implementation**
- Reconcile definition and pending occurrences in one provider transaction/atomic operation where supported.
- Redis acquisition always refreshes the authoritative current CronTicker definition instead of trusting an embedded stale snapshot.
- Do not mutate terminal history snapshots.

## Slice 5: Professional EF schema and TimeTicker data upgrade

**Tests first**
- Upgrade fixture representing a pre-chain schema/data set.
- Backfill every reachable descendant `ChainRootId` while preserving IDs/status/results.
- Orphan/cyclic graphs fail with a typed actionable migration exception.
- Runtime acquisition self-heals a null descendant that appears after bootstrap.
- Fenced child writes succeed after upgrade.
- Model/snapshot parity includes `NodeFinalizationOutbox`, new cron columns/index, and store metadata.

**Implementation**
- Keep EF schema migrations consumer-owned and generated from the application context.
- Add mapped `TickerQStoreMetadata` with schema/data version.
- Add `ITickerQDataMigration` pipeline that runs after `Database.MigrateAsync()` and before seeding/scheduling.
- Implement provider-portable EF ChainRootId backfill through EF queries/batched updates, with idempotent checkpoint and postcondition; avoid SQLite-only SQL.
- Add runtime chain-root persistence safety in acquisition.
- Generate corrected migrations/snapshots for all shipped samples, including `NodeFinalizationOutbox` and new additive fields.
- Expose documented migration validation/startup behavior; if required schema is absent, throw an actionable exception naming the missing migration rather than silently operating partially.

## Slice 6: Mongo bootstrap ordering and store metadata

**Tests first**
- Indexes and transaction capability are ready before reconciliation/scheduler query.
- Concurrent provisioners are idempotent.
- New binary rejects a newer store data version; legacy metadata is adopted after successful bootstrap.

**Implementation**
- Convert `TickerIndexProvisioner` to `ITickerQPersistenceBootstrapper` and remove late hosted-service registration.
- Create unique partial `SeedKey` index after duplicate convergence/preflight.
- Persist metadata document and validate after prerequisite provisioning.

## Slice 7: Redis atomicity, repair, metadata, and current-definition lookup

**Tests first**
- Atomic cron entity/index upsert cannot leave an invisible entity.
- Repair removes stale IDs and restores missing membership.
- Corrupt JSON is quarantined/reported rather than silently replaced.
- Pending occurrence uses current contract identity.
- Concurrent seed migration converges.

**Implementation**
- Lua scripts atomically write entity plus index memberships and perform compare/update by seed key.
- Startup repair is bounded, idempotent, observable, and runs before reconciliation.
- Store metadata hash and compatibility check.
- Acquisition refreshes current CronTicker definition.

## Slice 8: Cluster-safe, cancellation-aware custom seeders

**Tests first**
- Concurrent `AddOnceAsync(initIdentifier)` creates one TimeTicker on each durable provider.
- Cancellation interrupts startup seeder work.
- Existing delegate signatures still compile and execute.

**Implementation**
- Add cancellation-aware overloads while preserving old overloads.
- Thread initializer cancellation token to callbacks.
- Add provider-backed stable init identity with unique/atomic upsert semantics for TimeTicker `AddOnceAsync`.

## Slice 9: InMemory parity

**Tests first**
- Run shared seed identity, retirement, expression-change, and orphan-occurrence contracts against InMemory.

**Implementation**
- Match durable semantics: stable ownership, two-phase retirement, pending-only cleanup, no leaked indexes/occurrences.

## Slice 10: Consumer-facing migration experience

**Artifacts**
- `docs/upgrading-cron-time-tickers.md` with:
  - schema-first rolling rollout;
  - exact `dotnet ef migrations add` / script / deploy commands;
  - required columns/indexes/tables;
  - data migration lifecycle and logs;
  - retirement grace behavior;
  - downgrade limitations;
  - malformed-data remediation;
  - Redis/Mongo operational prerequisites.
- Sample migration histories and snapshots updated from actual model generation.
- Public diagnostics report current/required store version and pending data migrations without exposing secrets.

## Verification gates

1. Each test fails for the expected missing behavior before production code.
2. Core baseline: 1,061 tests remain green.
3. EF baseline: 174 pass / 2 existing skips remain, plus new upgrade tests.
4. Mongo and Redis real-provider suites pass where Docker/Testcontainers is available.
5. All nine affected core/provider/test projects build successfully.
6. Full solution remains subject to the pre-existing Apple Silicon `Grpc.Tools macosx_x64/protoc` blocker unless separately repaired.
7. Independent pre-commit review must approve security, concurrency, migration compatibility, and docs.

## Rollout

1. Deploy additive EF schema migrations / Mongo indexes / Redis scripts and metadata support.
2. Deploy new binaries node-by-node with retirement grace longer than the maximum rollout window.
3. Verify all nodes report the same store version and no pending data migrations.
4. Allow Phase-B retirement only after the fleet is fully upgraded or the grace expires under an explicitly supported deployment window.
5. Rollback keeps additive fields in place; old binaries ignore them. Never run destructive down-migrations during emergency rollback.
