# TickerQ Backend Reliability, Scheduling, and Authentication Hardening Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task. Use Claude first for coding, strict TDD for every behavior change, and independent GPT-5.6-sol + Claude reviews at the phase gates.

**Goal:** Remove the verified duplicate-execution, silent-job-loss, false-drain, provider-capability, timeout, dashboard-operation, and authentication deployment hazards on `experiment/reliability-observability-bulk` while preserving TickerQ's correct lease-fencing, atomic acquisition, stale-recovery, retry, webhook, observability, and SignalR contracts.

**Architecture:** Establish one authoritative lifecycle for accepted work—acquired, waiting, executing, and completed—used by lease renewal, concurrency limits, cancellation, and graceful drain. Make persistence reliability an explicit capability rather than optimistic default behavior. Bring EF Core, MongoDB, and in-memory execution semantics under shared provider-contract tests, then harden dashboard authentication/deployment defaults without weakening existing verifier behavior.

**Tech Stack:** .NET 10, C#, xUnit, EF Core, MongoDB.Driver/Testcontainers, ASP.NET Core authentication/CORS/Forwarded Headers, OpenTelemetry, GitHub Actions.

---

## Scope and non-negotiable invariants

- Work only on `experiment/reliability-observability-bulk`; do not rewrite or modify `feature/reliability-observability-bulk`.
- Preserve `<Version>10.4.2-beta</Version>`; do not downgrade to bypass restore failures.
- Do not push until all phase gates are green and the user authorizes the push.
- Follow RED → GREEN → REFACTOR for every behavior change. A new test must fail for the expected reason before production code changes.
- Preserve:
  - lease-owner fencing on terminal writes;
  - atomic conditional acquisition;
  - restart-then-cancel stale recovery and restart caps;
  - unique cron occurrence slots;
  - ID-only SignalR cache invalidation;
  - bounded/non-blocking webhook delivery;
  - JWT algorithm/key-confusion resistance and secure cookie flags;
  - current retry count and retry interval behavior.
- Do not claim Mongo integration tests passed unless Docker/Testcontainers actually ran.
- Do not claim the full suite is green until `TickerQ.Tests`, EF, Mongo, dashboard, source generator, and dependent builds all succeed.

## Branch and commit strategy

Keep one implementation branch but use small reversible commits. Suggested commit sequence:

1. `test: repair core test suite after reliability merge`
2. `fix(scheduler): track and bound active executions`
3. `fix(scheduler): renew leases while work waits for capacity`
4. `fix(persistence): make reliability support explicit`
5. `fix(ef): return only immediately acquired tickers`
6. `fix(mongodb): hydrate nested execution chains`
7. `fix(scheduler): prevent node identity ownership collisions`
8. `fix(execution): make timeout abandonment ownership-safe`
9. `fix(dashboard): make run-now revive terminal jobs`
10. `fix(ef): align occurrence update semantics`
11. `fix(auth): harden dashboard deployment boundaries`
12. `fix(auth): stabilize signing and session behavior`
13. `fix(observability): isolate notifiers and complete instrumentation`
14. `test(ci): enforce cross-provider reliability contracts`
15. `docs: document reliability and dashboard security contracts`

Do not combine P0 scheduling/provider fixes with authentication hardening in one commit.

---

## Phase 0: Establish a trustworthy baseline

### Task 1: Repair stale core tests before changing production behavior

**Objective:** Make `tests/TickerQ.Tests` compile at commit `f051a3d` so every later RED failure is meaningful.

**Files:**
- Modify: `tests/TickerQ.Tests/TrimSafeSerializationTests.cs`
- Modify: `tests/TickerQ.Tests/InternalTickerManagerTests.cs`
- Modify: `tests/TickerQ.Tests/RetryExhaustionTests.cs`
- Modify: `tests/TickerQ.Tests/RetryBehaviorTests.cs`
- Modify: `tests/TickerQ.Tests/TickerExecutionTaskHandlerTests.cs`
- Modify: `tests/TickerQ.Tests/TickerManagerTests.cs`
- Review: `src/TickerQ.Dashboard/Hubs/TickerQNotificationHubSender.cs`
- Review: `src/TickerQ/Src/TickerExecutionTaskHandler.cs`

**Step 1: Reproduce the baseline failure**

Run:

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj --configuration Release --no-restore
```

Expected: FAIL with stale `CronOccurrenceUpdateNotification`, constructor-argument, and object-to-`Guid` errors.

**Step 2: Update tests only**

- Replace removed full-entity notification expectations with the preserved ID-only invalidation contract.
- Pass `SchedulerOptionsBuilder` and `ITickerQFailureNotifier` to handler constructors.
- Update mocked method arguments from stale `object`/entity forms to `Guid` IDs.
- Do not change production code to satisfy stale tests.

**Step 3: Verify the repaired baseline**

Run:

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj --configuration Release --no-restore
```

Expected: PASS. Existing warnings must be recorded separately; no compile errors.

**Step 4: Commit**

```bash
git add tests/TickerQ.Tests
git commit -m "test: repair core suite after reliability merge"
```

### Phase 0 gate

Run:

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore
dotnet test tests/TickerQ.EntityFrameworkCore.Tests/TickerQ.EntityFrameworkCore.Tests.csproj -c Release --no-restore
dotnet test tests/TickerQ.SourceGenerator.Tests/TickerQ.SourceGenerator.Tests.csproj -c Release --no-restore
```

Expected: all three projects compile and pass before P0 production changes begin.

---

## Phase 1: Unify concurrency, active-work tracking, and graceful drain

### Task 2: Add failing tests for global active-execution concurrency

**Objective:** Prove `MaxConcurrency` bounds active asynchronous jobs, not just worker-loop count.

**Files:**
- Modify: `tests/TickerQ.Tests/TickerQTaskSchedulerTests.cs`
- Modify: `tests/TickerQ.Tests/TickerQTaskSchedulerAdvancedTests.cs`

**RED test:** Queue at least 10 delegates that increment an atomic active counter, await a release gate, then decrement. Configure `maxConcurrency = 2`; assert observed peak active count is exactly 2 or less.

Run:

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~TickerQTaskScheduler'
```

Expected before fix: FAIL because incomplete async work is detached and peak active count exceeds 2.

### Task 3: Make the scheduler await accepted work and track active executions explicitly

**Objective:** Ensure queue count and active count represent disjoint, authoritative states.

**Files:**
- Modify: `src/TickerQ.Utilities/Interfaces/ITickerQTaskScheduler.cs`
- Modify: `src/TickerQ/Src/TickerQThreadPool/TickerQTaskScheduler.cs`

**Required design:**

```csharp
int TotalQueuedTasks { get; }
int ActiveExecutionCount { get; }
Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
```

Implementation rules:

- Increment active count immediately before invoking accepted work.
- Await the returned task inside `ExecuteWorkAsync`; do not detach it.
- Decrement active count in `finally`.
- Handle `LongRunning` work through the same active-work registry; no untracked `StartNew(...).Unwrap()` fire-and-forget path.
- Keep exception observation, but do not use exception swallowing to detach lifecycle ownership.
- `MaxConcurrency` must cap active executions.
- `ActiveWorkers` remains diagnostic only; it must not be used as completion state.

**GREEN verification:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~TickerQTaskScheduler'
```

Expected: new concurrency test passes; existing scheduler tests remain green.

### Task 4: Replace shutdown polling with the scheduler drain contract

**Objective:** Prevent false “drained” completion while queued or active work remains.

**Files:**
- Modify: `tests/TickerQ.Tests/TickerQSchedulerBackgroundServiceShutdownTests.cs`
- Modify: `src/TickerQ/Src/BackgroundServices/TickerQSchedulerBackgroundService.cs`
- Modify: `src/TickerQ/Src/TickerQThreadPool/TickerQTaskScheduler.cs`

**RED tests:**

1. Queue a job blocked on a test gate, call `StopAsync`, and assert stop does not complete before the gate or configured timeout.
2. Assert timeout returns `false`/logs remaining active work.
3. Assert a completed active job allows stop to finish without waiting for the full timeout.
4. Replace the hollow “started/stopped multiple times” test with an actual restart sequence.

**Implementation:**

- `Freeze()` first.
- Await `WaitForRunningTasksAsync(ShutdownDrainTimeout, hostCancellationToken)`.
- The wait condition is `TotalQueuedTasks == 0 && ActiveExecutionCount == 0`.
- Logging must report queued and active execution counts from the scheduler, not infer them from worker threads.
- Retain `TickerCancellationTokenManager.ActiveCount` only as ticker-domain diagnostics, not the sole drain authority.

**Verify:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~TickerQSchedulerBackgroundServiceShutdownTests|FullyQualifiedName~TickerQTaskScheduler'
```

**Commit Tasks 2–4:**

```bash
git add src/TickerQ.Utilities/Interfaces/ITickerQTaskScheduler.cs \
        src/TickerQ/Src/TickerQThreadPool/TickerQTaskScheduler.cs \
        src/TickerQ/Src/BackgroundServices/TickerQSchedulerBackgroundService.cs \
        tests/TickerQ.Tests/TickerQTaskSchedulerTests.cs \
        tests/TickerQ.Tests/TickerQTaskSchedulerAdvancedTests.cs \
        tests/TickerQ.Tests/TickerQSchedulerBackgroundServiceShutdownTests.cs
git commit -m "fix(scheduler): track and bound active executions"
```

---

## Phase 2: Keep acquired leases alive while work waits

### Task 5: Introduce one acquired-work registration lifecycle

**Objective:** Track acquired jobs from persistence ownership through queue wait, semaphore wait, execution, and terminal persistence.

**Files:**
- Modify: `src/TickerQ.Utilities/TickerCancellationTokenManager.cs`
- Modify: `src/TickerQ.Utilities/Models/TickerCancellationTokenDetails.cs` or its actual declaration file
- Modify: `src/TickerQ/Src/BackgroundServices/TickerQSchedulerBackgroundService.cs`
- Modify: `src/TickerQ/Src/TickerExecutionTaskHandler.cs`
- Modify: `src/TickerQ.Utilities/Managers/InternalTickerManager.cs`
- Test: `tests/TickerQ.Tests/TickerQSchedulerBackgroundServiceTests.cs`
- Test: `tests/TickerQ.Tests/TickerExecutionTaskHandlerTests.cs`

**Required lifecycle:**

```text
persisted acquisition/SetTickersInProgress
  -> register acquired ticker + linked CTS
  -> queue wait
  -> function semaphore wait
  -> execute using same registration/CTS
  -> persist terminal result
  -> unregister/dispose in finally
```

Do not create two independent CTS registrations for the same ticker ID.

**RED tests:**

1. A job waiting longer than one renewal interval appears in `SnapshotRunningForLeaseRenewal`.
2. Cancelling a waiting job cancels its semaphore wait and unregisters once.
3. Handler execution reuses the pre-registered CTS and does not double-register.
4. Terminal completion removes the registration even when notifier/persistence throws.

**Implementation notes:**

- Introduce an internal disposable registration/handle if needed; ownership must be explicit.
- Registration must distinguish root time ticker, chain child, and cron occurrence so only persisted leased rows are renewed.
- Use the same registry for lease renewal and cancellation.

**Verify:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~TickerQSchedulerBackgroundServiceTests|FullyQualifiedName~TickerExecutionTaskHandlerTests'
```

**Commit:**

```bash
git add src/TickerQ.Utilities src/TickerQ/Src tests/TickerQ.Tests
git commit -m "fix(scheduler): renew leases while work waits for capacity"
```

### Phase 2 gate

Add a deterministic integration-style unit test with a short lease and blocked semaphore. Prove at least two renewals occur and stale recovery does not reclaim the waiting job.

---

## Phase 3: Make persistence reliability support explicit and fail closed

### Task 6: Add a provider capability contract

**Objective:** Prevent unsupported providers from lying about lease and recovery success.

**Files:**
- Modify: `src/TickerQ.Utilities/Interfaces/ITickerPersistenceProvider.cs`
- Modify: `src/TickerQ.EntityFrameworkCore/Infrastructure/BasePersistenceProvider.cs`
- Modify: `src/TickerQ.MongoDB/Infrastructure/TickerMongoPersistenceProvider.cs`
- Modify: `src/TickerQ/Src/Provider/TickerInMemoryPersistenceProvider.cs`
- Modify: `src/TickerQ.Caching.StackExchangeRedis/Infrastructure/BaseRedisPersistenceProvider.cs`
- Modify: `src/TickerQ/Src/BackgroundServices/TickerQStaleJobRecoveryBackgroundService.cs`
- Modify: lease-renewal service/loop in `src/TickerQ.Utilities/Managers/InternalTickerManager.cs` or its caller
- Test: create `tests/TickerQ.Tests/PersistenceReliabilityCapabilityTests.cs`

**Compatibility-safe contract:**

```csharp
bool SupportsLeaseBasedRecovery => false;
```

- EF and Mongo override `true`.
- Redis and in-memory remain `false` until they implement the full contract.
- Default renewal counts return `0`, held IDs return `[]`, and recovery must not claim success.
- Background renewal/recovery must skip unsupported providers and emit one clear startup warning.
- Do not let a partial third-party override run recovery while inheriting fake renewal success.

**RED tests:**

1. Minimal stub provider inherits `SupportsLeaseBasedRecovery == false` and cannot enter renewal/recovery.
2. Default renewal returns zero, not requested ID count.
3. EF and Mongo capabilities are true.
4. Unsupported provider logs one warning and continues without stale-recovery loops.

**Verify:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~PersistenceReliabilityCapabilityTests'
dotnet build src/TickerQ.Caching.StackExchangeRedis/TickerQ.Caching.StackExchangeRedis.csproj -c Release --no-restore
dotnet build src/TickerQ.MongoDB/TickerQ.MongoDB.csproj -c Release --no-restore
```

**Commit:**

```bash
git add src tests/TickerQ.Tests/PersistenceReliabilityCapabilityTests.cs
git commit -m "fix(persistence): make reliability support explicit"
```

---

## Phase 4: Correct provider acquisition and projection semantics

### Task 7: Fix EF immediate acquisition readback

**Objective:** Return only rows acquired by the current invocation.

**Files:**
- Modify: `src/TickerQ.EntityFrameworkCore/Infrastructure/BasePersistenceProvider.cs`
- Modify: `tests/TickerQ.EntityFrameworkCore.Tests/Infrastructure/EfCorePersistenceProviderTests.cs`

**RED test:** Seed two requested IDs: one Idle/unlocked and one already `InProgress` under the same node. Call `AcquireImmediateTimeTickersAsync([both])`; assert only the Idle row is returned and changed.

**Implementation options, in preference order:**

1. Provider-supported `UPDATE ... RETURNING` abstraction if portable across supported EF databases.
2. Add a per-acquisition unique token persisted in the same atomic update and read back by token.
3. Transaction with a preselected optimistic token set and affected-ID verification.

Do not use `LockHolder + InProgress` alone as proof of acquisition by this invocation.

**Verify:**

```bash
dotnet test tests/TickerQ.EntityFrameworkCore.Tests/TickerQ.EntityFrameworkCore.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AcquireImmediate'
```

**Commit:**

```bash
git add src/TickerQ.EntityFrameworkCore/Infrastructure/BasePersistenceProvider.cs \
        tests/TickerQ.EntityFrameworkCore.Tests/Infrastructure/EfCorePersistenceProviderTests.cs
git commit -m "fix(ef): return only immediately acquired tickers"
```

### Task 8: Hydrate Mongo time-ticker chains recursively for execution

**Objective:** Preserve root → child → grandchild and deeper execution graphs.

**Files:**
- Modify: `src/TickerQ.MongoDB/Infrastructure/TickerMongoPersistenceProvider.cs`
- Modify: `tests/TickerQ.MongoDB.Tests/MongoPersistenceProviderTests.cs`
- Modify: `tests/TickerQ.MongoDB.Tests/MergeReliabilityContractTests.cs` if a non-Docker shape test is useful

**RED test:** Insert root, child, and grandchild; acquire/queue root; assert returned projection contains the complete hierarchy and all three can reach terminal status.

**Implementation:**

- Replace one-level `LoadChildrenLookup` with bounded-query recursive hydration.
- Preserve `ExecutionTime == null` filtering for child definitions.
- Add cycle protection using a visited-ID set even if database constraints should prevent cycles.
- Avoid one query per node when possible: breadth-first fetch by parent-ID batches.

**Verify with Docker:**

```bash
docker info
dotnet test tests/TickerQ.MongoDB.Tests/TickerQ.MongoDB.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Nested|FullyQualifiedName~Chain'
```

Expected: Docker available and test passes. If Docker is unavailable, record BLOCKED; compilation alone is not runtime proof.

**Commit:**

```bash
git add src/TickerQ.MongoDB/Infrastructure/TickerMongoPersistenceProvider.cs \
        tests/TickerQ.MongoDB.Tests
git commit -m "fix(mongodb): hydrate nested execution chains"
```

---

## Phase 5: Make ownership identity collision-safe

### Task 9: Separate stable node labeling from unique execution ownership

**Objective:** Prevent two processes sharing a machine name from owning/releasing the same rows.

**Files:**
- Modify: `src/TickerQ.Utilities/TickerOptionsBuilder.cs`
- Modify: provider constructors that consume `NodeIdentifier`
- Modify: `src/TickerQ.EntityFrameworkCore/DependencyInjection/ServiceExtension.cs`
- Modify: `src/TickerQ.EntityFrameworkCore/Infrastructure/BasePersistenceProvider.cs`
- Modify: `src/TickerQ.MongoDB/Infrastructure/TickerMongoPersistenceProvider.cs`
- Review: Redis heartbeat ownership behavior before changing Redis
- Test: `tests/TickerQ.EntityFrameworkCore.Tests/Infrastructure/EfCorePersistenceProviderTests.cs`
- Test: `tests/TickerQ.MongoDB.Tests/MongoPersistenceProviderTests.cs`

**Design requirement:**

- Keep a human-readable logical node label for metrics/dashboard.
- Use a process-instance-unique execution owner ID for `LockHolder` fencing, for example logical node + process ID + startup nonce.
- Do not unconditionally release all `InProgress` rows for the current live owner on startup.
- Old owners must be recovered by expired leases; stale queued locks require an explicit `LockedAt`-age recovery rule.
- Preserve Redis heartbeat semantics, where a separate heartbeat proves a node dead; do not apply EF assumptions blindly.

**RED tests:**

1. Two provider instances with the same logical node label have different execution owner IDs.
2. Starting instance B does not release instance A's live `InProgress` row.
3. Expired predecessor ownership is recovered through stale recovery.
4. Stale `Queued` ownership is eventually released without touching a live sibling.

**Verify:** EF test first, then Mongo with Docker.

**Commit:**

```bash
git add src tests/TickerQ.EntityFrameworkCore.Tests tests/TickerQ.MongoDB.Tests
git commit -m "fix(scheduler): prevent node identity ownership collisions"
```

---

## Phase 6: Make timeout semantics ownership-safe

### Task 10: Keep non-cooperative work tracked and leased until it actually exits

**Objective:** Never dispose a scope, release ownership, or report full completion while timed-out user code is still running.

**Files:**
- Modify: `src/TickerQ/Src/TickerExecutionTaskHandler.cs`
- Modify: `src/TickerQ.Utilities/TickerOptionsBuilder.cs` documentation
- Modify: `tests/TickerQ.Tests/TickerExecutionTaskHandlerTests.cs`
- Modify: `tests/TickerQ.Tests/RetryBehaviorTests.cs`

**Required semantics:**

- Timeout fires the cooperative cancellation token.
- If the delegate exits during grace: persist `Cancelled` with timeout reason and release normally.
- If the delegate ignores cancellation: keep its registration, lease, and DI scope alive until the delegate actually exits; log/notify a timeout-pending critical event.
- Do not retry or reacquire the same persisted job while the zombie task remains live on the owning node.
- After actual exit, observe any exception, persist terminal timeout outcome once, release lock/lease, and dispose scope.
- Document that in-process hard termination is impossible; true hard timeout requires external process isolation.

**RED tests:**

1. Cooperative timeout ends `Cancelled` without consuming retry count.
2. Non-cooperative delegate continues after grace; scoped dependency remains usable and undisposed.
3. Non-cooperative job remains in lease-renewal snapshot until actual completion.
4. Terminal write occurs once, after actual delegate exit.
5. No second execution starts while first timed-out delegate remains live.

**Verify:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~TickerExecutionTaskHandlerTests|FullyQualifiedName~RetryBehaviorTests'
```

**Commit:**

```bash
git add src/TickerQ/Src/TickerExecutionTaskHandler.cs \
        src/TickerQ.Utilities/TickerOptionsBuilder.cs \
        tests/TickerQ.Tests/TickerExecutionTaskHandlerTests.cs \
        tests/TickerQ.Tests/RetryBehaviorTests.cs
git commit -m "fix(execution): keep timed-out work ownership-safe"
```

---

## Phase 7: Correct dashboard execution operations

### Task 11: Implement one safe time-ticker run-now command

**Objective:** Make single run-now and bulk retry execute terminal time tickers exactly once.

**Files:**
- Modify: `src/TickerQ.Dashboard/Endpoints/DashboardEndpoints.cs`
- Modify: manager/persistence API only if required for an atomic reset-and-acquire operation
- Modify: `tests/TickerQ.Tests/DashboardEndpointTests.cs`
- Add EF integration coverage in `tests/TickerQ.EntityFrameworkCore.Tests`

**RED tests:**

1. Run-now on `Failed`, `Done`, and `Cancelled` results in one new execution.
2. Bulk retry reports `Affected` only for rows actually revived.
3. Concurrent run-now calls cause exactly one acquisition.
4. Retry count, exception, skip reason, lock, and lease fields are reset consistently.
5. Existing `InProgress` job returns 409 or an explicit no-op result; it is never duplicated.

**Implementation:**

- Create one domain command used by both endpoints.
- Atomically transition eligible terminal row to `Idle`/`Queued` and clear terminal ownership metadata, then invoke the corrected immediate-acquisition path.
- Do not merely update `ExecutionTime`.

**Verify:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~DashboardEndpointTests'
dotnet test tests/TickerQ.EntityFrameworkCore.Tests/TickerQ.EntityFrameworkCore.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~RunNow|FullyQualifiedName~BulkRetry'
```

**Commit:**

```bash
git add src/TickerQ.Dashboard tests
git commit -m "fix(dashboard): make run-now revive terminal jobs"
```

---

## Phase 8: Close EF/Mongo semantic gaps

### Task 12: Fix EF occurrence/status mapping parity

**Objective:** Stop non-status updates from rewriting status and preserve occurrence timestamps/timeouts.

**Files:**
- Modify: `src/TickerQ.EntityFrameworkCore/Infrastructure/MappingExtensions.cs`
- Modify: `src/TickerQ.EntityFrameworkCore/Infrastructure/BasePersistenceProvider.cs`
- Modify: `tests/TickerQ.EntityFrameworkCore.Tests/Infrastructure/EfCorePersistenceProviderTests.cs`

**RED tests:**

1. Retry-count-only update does not change status or `SkippedReason`.
2. Failed retry leaves `SkippedReason == null`.
3. Cron occurrence status update advances `UpdatedAt`.
4. Reclaimed cron occurrence carries parent `TimeoutSeconds`.
5. Release/dead-node paths clear `LeaseUntil` consistently where appropriate.

**Implementation:**

Change bare status branches to:

```csharp
if (props.Contains(nameof(InternalFunctionContext.Status)))
{
    if (context.Status == TickerStatus.Skipped) { /* skipped mapping */ }
    else { /* normal status mapping */ }
}
```

Pass an explicit `updatedAt` into cron occurrence update mapping and include `TimeoutSeconds` in all parent projections.

**Verify:**

```bash
dotnet test tests/TickerQ.EntityFrameworkCore.Tests/TickerQ.EntityFrameworkCore.Tests.csproj -c Release --no-restore
```

**Commit:**

```bash
git add src/TickerQ.EntityFrameworkCore tests/TickerQ.EntityFrameworkCore.Tests
git commit -m "fix(ef): align occurrence update semantics"
```

---

## Phase 9: Harden dashboard deployment and authentication boundaries

### Task 13: Replace credentialed reflect-any CORS with explicit origins

**Objective:** Never allow arbitrary credentialed cross-origin API access.

**Files:**
- Modify: `src/TickerQ.Dashboard/DashboardOptionsBuilder.cs`
- Modify: `src/TickerQ.Dashboard/DependencyInjection/ServiceExtensions.cs`
- Modify: `tests/TickerQ.Tests/DashboardAuthorizationPipelineTests.cs`

**RED tests:**

- Split-origin mode without an allow-list fails validation or denies cross-origin credentials.
- Allowed origin receives exact `Access-Control-Allow-Origin` and credentials.
- Unlisted origin receives neither.

Add an explicit configuration API such as `AllowOrigins(params string[] origins)`; do not infer trust from `BackendDomain` alone.

### Task 14: Require trusted forwarded proxies/networks

**Objective:** Prevent direct clients from spoofing forwarded IP/host/proto.

**Files:**
- Modify: `src/TickerQ.Dashboard/DashboardOptionsBuilder.cs`
- Modify: `src/TickerQ.Dashboard/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/TickerQ.Tests/DashboardAuthorizationPipelineTests.cs`

**Implementation:**

- Do not clear `KnownNetworks`/`KnownProxies` into trust-all mode.
- Accept configured trusted proxies/networks.
- If forwarded headers are enabled without trust boundaries, fail validation or log a high-severity warning and leave them disabled.

### Task 15: Make anonymous dashboard exposure explicit

**Objective:** Remove silent insecure deployment.

**Files:**
- Modify: `src/TickerQ.Dashboard/DashboardOptionsBuilder.cs`
- Modify: `src/TickerQ.Dashboard/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: startup validation/logging
- Test: `tests/TickerQ.Tests/DashboardAuthorizationPipelineTests.cs`

**Compatibility choice:** Prefer explicit `AllowAnonymousDashboard()` acknowledgement. If immediate breaking behavior is unacceptable for `10.4.2-beta`, emit a prominent startup warning now and schedule secure-by-default behavior for the next major release.

**Commit Tasks 13–15:**

```bash
git add src/TickerQ.Dashboard tests/TickerQ.Tests/DashboardAuthorizationPipelineTests.cs
git commit -m "fix(auth): harden dashboard deployment boundaries"
```

### Task 16: Stabilize JWT signing and bound session renewal

**Objective:** Make multi-instance tokens deterministic and session semantics honest.

**Files:**
- Modify: `src/TickerQ.Dashboard/DependencyInjection/ServiceExtensions.cs`
- Modify: `src/TickerQ.Dashboard/Authentication/Endpoints/AuthEndpoints.cs`
- Modify: JWT/cookie option classes
- Add: `tests/TickerQ.Tests/JwtAuthenticationTests.cs`

**Signing-key decision:**

- Remove derivation from randomized `IDataProtector.Protect()` output.
- Require explicit shared signing key for multi-instance use.
- Permit ephemeral signing only behind existing explicit dev opt-in.
- Correct documentation; never claim DataProtection-derived stability unless a deterministic supported derivation is implemented.

**Session decision:**

Choose one explicit contract:

1. Implement rotating one-time refresh tokens with absolute session cap and revocation/version checks, or
2. Remove/rename refresh behavior and document it as sliding access-token renewal with no revocation.

For a dashboard library without durable user session storage, option 2 is the smaller honest change; do not build a partial refresh-token store.

**RED tests:** deterministic key behavior, multi-instance validation, absolute/sliding lifetime behavior, logout expectations.

### Task 17: Harden login and Basic authentication

**Files:**
- Modify: `src/TickerQ.Dashboard/Authentication/Endpoints/LoginRateLimiter.cs`
- Modify: `src/TickerQ.Dashboard/Authentication/Endpoints/AuthEndpoints.cs`
- Modify: `src/TickerQ.Dashboard/Authentication/Schemes/BasicAuthScheme.cs`
- Add/modify auth tests under `tests/TickerQ.Tests`

**Required behavior:**

- Add per-username throttling alongside trusted client-IP throttling.
- Bound limiter storage and prune deterministically.
- Use `CryptographicOperations.FixedTimeEquals` for Basic credentials.
- Use a dummy password hash path for unknown usernames if timing parity is practical.

**Commit Tasks 16–17:**

```bash
git add src/TickerQ.Dashboard tests/TickerQ.Tests
git commit -m "fix(auth): stabilize signing and session behavior"
```

---

## Phase 10: Isolate extension failures and complete observability

### Task 18: Persist terminal state before calling failure notifiers

**Objective:** A throwing custom notifier must never leave a ticker `InProgress`.

**Files:**
- Modify: `src/TickerQ/Src/TickerExecutionTaskHandler.cs`
- Modify: instrumentation/logging if needed
- Test: `tests/TickerQ.Tests/TickerExecutionTaskHandlerTests.cs`

**RED test:** Register a notifier whose `Notify` throws; assert terminal state is persisted exactly once and notifier failure is logged/swallowed.

**Implementation:** Persist outcome first, then invoke notifier inside try/catch. Built-in webhook remains non-blocking.

### Task 19: Complete OpenTelemetry registration and status semantics

**Objective:** Make package setup compile and export durable job spans correctly.

**Files:**
- Modify: `src/TickerQ.Instrumentation.OpenTelemetry/ServiceExtensions.cs`
- Modify: `src/TickerQ.Instrumentation.OpenTelemetry/OpenTelemetryInstrumentation.cs`
- Modify: package README/docs
- Add tests in appropriate OpenTelemetry test project or `TickerQ.Tests`

Add the documented `TracerProviderBuilder.AddTickerQInstrumentation()` helper that calls `AddSource("TickerQ")`. Assert failed/cancelled activities receive appropriate status and sanitized exception tags.

### Task 20: Enforce assistant iteration budget

**Files:**
- Modify: `src/TickerQ.Dashboard/Assistant/TickerAssistantService.cs`
- Test: add assistant configuration test

Pass `AssistantOptions.MaxToolIterations` to the underlying function-invocation configuration; test with a value of 1.

### Task 21: Add webhook drop/redaction observability

**Files:**
- Modify: webhook notifier/worker and metrics files
- Add tests around full-channel behavior

Keep the bounded `DropOldest` architecture. Add a dropped-event counter/log signal and configurable reason sanitization; do not block job execution on webhook delivery.

**Commit Tasks 18–21:**

```bash
git add src tests
git commit -m "fix(observability): isolate notifiers and complete instrumentation"
```

---

## Phase 11: Shared provider contracts and CI enforcement

### Task 22: Create shared reliability contract tests

**Objective:** Run identical behavioral contracts against EF and Mongo instead of reflection-only tests.

**Files:**
- Create: `tests/TickerQ.ProviderContracts.Tests/` if project sharing is practical, or create a shared linked test source under `tests/Shared/`
- Modify: EF and Mongo test projects to consume shared contract tests
- Replace/strengthen: `tests/TickerQ.MongoDB.Tests/MergeReliabilityContractTests.cs`

**Contract cases:**

1. Acquisition race: N concurrent acquirers, exactly one winner.
2. Same-node mixed immediate acquisition returns only newly acquired rows.
3. Lease stamped on acquisition.
4. Lease renewal count reflects actual rows.
5. Ownership transfer rejects stale terminal write.
6. Restart/cancel stale policy and restart cap.
7. Lease ratio validation.
8. Cron parent hydration preserves function/retries/timeout.
9. Root→child→grandchild execution projection.
10. Release clears ownership and lease fields.

Avoid reflection-only “method exists” assertions as primary coverage.

### Task 23: Add Mongo tests to CI with a real service

**Files:**
- Modify: `.github/workflows/build.yml`
- Modify: `tests/TickerQ.MongoDB.Tests/MongoTestFixture.cs`

**CI behavior:**

- Start Mongo service or ensure Testcontainers has Docker access.
- Restore/build/test `TickerQ.MongoDB.Tests`.
- Local no-Docker runs may skip with a clear reason, but CI must fail if Mongo runtime tests cannot start.
- Preserve artifacts/logs for Testcontainers startup failures.

**Verify workflow syntax and local projects:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore
dotnet test tests/TickerQ.EntityFrameworkCore.Tests/TickerQ.EntityFrameworkCore.Tests.csproj -c Release --no-restore
docker info
dotnet test tests/TickerQ.MongoDB.Tests/TickerQ.MongoDB.Tests.csproj -c Release --no-restore
```

**Commit Tasks 22–23:**

```bash
git add tests .github/workflows/build.yml
git commit -m "test(ci): enforce cross-provider reliability contracts"
```

---

## Phase 12: Documentation, compatibility, and final verification

### Task 24: Document the contracts users must understand

**Files:** relevant README/package docs and XML comments.

Document:

- at-least-once execution and lease fencing;
- unique execution owner versus logical node name;
- supported/unsupported provider reliability capabilities;
- cooperative in-process timeout semantics;
- run-now behavior;
- trusted proxy and explicit CORS configuration;
- anonymous-dashboard risk and opt-in;
- signing-key and token-renewal semantics;
- webhook overload/drop metrics;
- OpenTelemetry setup.

Commit:

```bash
git add README.md docs src/**/*.cs
# Stage only intended documentation/XML-comment files.
git commit -m "docs: document reliability and dashboard security contracts"
```

### Task 25: Run full verification matrix

**Static guards:**

```bash
git diff --check
git status --short
git diff origin/main...HEAD --stat
```

**Core:**

```bash
dotnet test tests/TickerQ.Tests/TickerQ.Tests.csproj -c Release --no-restore
```

**Providers:**

```bash
dotnet test tests/TickerQ.EntityFrameworkCore.Tests/TickerQ.EntityFrameworkCore.Tests.csproj -c Release --no-restore
dotnet test tests/TickerQ.Caching.StackExchangeRedis.Tests/TickerQ.Caching.StackExchangeRedis.Tests.csproj -c Release --no-restore
docker info
dotnet test tests/TickerQ.MongoDB.Tests/TickerQ.MongoDB.Tests.csproj -c Release --no-restore
```

**Other regression targets:**

```bash
dotnet test tests/TickerQ.SourceGenerator.Tests/TickerQ.SourceGenerator.Tests.csproj -c Release --no-restore
dotnet build src/TickerQ.Dashboard/TickerQ.Dashboard.csproj -c Release --no-restore
dotnet build src/TickerQ.Instrumentation.OpenTelemetry/TickerQ.Instrumentation.OpenTelemetry.csproj -c Release --no-restore
```

If the full solution restore requires local `TickerQ.Utilities 10.4.2-beta`, build/package it into an OS-safe temporary local feed. Do not change the version to bypass restore.

### Task 26: Independent review gates

1. Claude review of each P0 phase immediately after its commit.
2. GPT-5.6-sol review using the same prompt and exact commit.
3. Reconcile disagreements against source and executable tests.
4. Final security review focused on auth/CORS/proxy/token changes.
5. Final provider parity review focused on EF/Mongo/Redis/in-memory contracts.

No push until:

- all P0/P1 findings have a passing regression test;
- core tests compile and pass;
- EF tests pass;
- Mongo tests actually execute and pass in Docker/CI;
- both independent reviews return no blocker;
- working tree is clean;
- the user approves pushing.

---

## Risks and tradeoffs

1. **Scheduler refactor blast radius:** Awaiting work changes throughput behavior. Benchmark queue latency and throughput before/after; correctness takes priority over accidental unbounded concurrency.
2. **Provider compatibility:** Adding abstract interface members would break third-party providers. Use default capability properties/methods to preserve compilation while failing operationally safe.
3. **Node identity migration:** Existing rows contain old `LockHolder` values. Add migration/recovery behavior so deployment does not strand queued jobs.
4. **Timeout semantics:** .NET cannot safely kill arbitrary in-process code. Do not promise hard termination without process isolation.
5. **Auth defaults:** Requiring auth immediately may break existing deployments. An explicit warning/opt-in during beta is acceptable, but silent exposure is not.
6. **Refresh tokens:** A proper revocable refresh flow needs durable user/session storage. Prefer honest sliding-token semantics over a partial security system unless durable storage is intentionally added.
7. **Mongo CI:** Testcontainers behavior differs by runner. CI must prove runtime behavior, not silently skip.
8. **Public API:** `NodeIdentifier`, persistence defaults, dashboard CORS APIs, and timeout behavior are public contracts; document compatibility notes in release notes.

## Definition of done

- No verified P0/P1 issue remains without an executable regression test.
- Global concurrency is bounded by active executions.
- Shutdown drain waits for queued, waiting, and executing work or reports a bounded timeout accurately.
- Waiting acquired jobs retain renewable ownership.
- Unsupported providers cannot report fake reliability success.
- EF immediate acquisition and Mongo deep chains behave correctly.
- Node collisions cannot release live sibling work.
- Timed-out non-cooperative jobs remain ownership-safe.
- Run-now/bulk-retry executes eligible terminal jobs exactly once.
- CORS/proxy/dashboard-auth/signing/session behavior is explicit and safe.
- EF and Mongo pass shared acquisition/lease/fencing/recovery contracts.
- `TickerQ.Tests` compiles and passes.
- Mongo runtime tests execute in CI.
- Working tree is clean and final reviews have no blocker.
