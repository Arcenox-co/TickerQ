using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

using Xunit;

namespace TickerQ.Tests.Shared.ProviderReliability;

/// <summary>
/// One authoritative provider-neutral contract for <c>MigrateDefinedCronTickers</c>'s desired-state
/// reconciliation semantics (Slice 1: "freeze public reconciliation semantics"), exercised as real
/// runtime behavior against a live store and <c>&lt;Compile Link&gt;</c>-shared into every durable
/// provider test project — the same pattern as <see cref="ProviderReliabilityContractTests"/>.
///
/// The invariant under test: orphan detection compares persisted code-owned (seeded) rows to the
/// <see cref="DefinedCronSeedManifest"/> — the desired local seed keys — and NEVER to the global
/// runtime function registry. Because the decision is now manifest-driven, these cases drive purely
/// through the public provider API and require no <c>TickerFunctionProvider</c> manipulation, so they
/// are order-independent across the shared suite.
/// </summary>
public abstract class DefinedCronMigrationContractTests
{
    /// <summary>The live provider under test, backed by a real store.</summary>
    protected abstract ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider { get; }

    /// <summary>The fixed "now" the provider's clock returns for the duration of a test.</summary>
    protected abstract DateTime Now { get; }

    private static DefinedCronSeedManifest OwnedManifest(
        string applicationNamespace, params DefinedCronTickerSeed[] seeds)
        => new(applicationNamespace, seeds);

    private static DefinedCronSeedManifest OwnedManifestWithLegacyOwner(
        string applicationNamespace, string function, params DefinedCronTickerSeed[] seeds)
        => new(applicationNamespace, seeds,
            new Dictionary<string, string>(StringComparer.Ordinal) { [function] = applicationNamespace });

    [Fact]
    public async Task ActivationEpoch_HigherActivatingRejectsLowerCheckpointAndCommit()
    {
        const long higherEpoch = 90_003;
        var begun = await Provider.BeginReconciliationActivationEpochAsync(higherEpoch, CancellationToken.None);
        Assert.Equal(higherEpoch, begun.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, begun.Phase);

        var lowerCheckpoint = await Provider.AdvanceReconciliationCheckpointAsync(
            higherEpoch - 1, "stale-lower-checkpoint", CancellationToken.None);
        var lowerCommit = await Provider.CommitReconciliationActivationEpochAsync(
            higherEpoch - 1, CancellationToken.None);

        Assert.All(new[] { lowerCheckpoint, lowerCommit }, state =>
        {
            Assert.Equal(higherEpoch, state.Epoch);
            Assert.Equal(ActivationEpochPhase.Activating, state.Phase);
            Assert.NotEqual("stale-lower-checkpoint", state.Checkpoint);
        });

        var committed = await Provider.CommitReconciliationActivationEpochAsync(higherEpoch, CancellationToken.None);
        Assert.Equal(higherEpoch, committed.Epoch);
        Assert.Equal(ActivationEpochPhase.Activated, committed.Phase);
    }

    [Theory]
    [InlineData("a-first")]
    [InlineData("b-first")]
    [InlineData("concurrent")]
    public async Task Namespaced_ExplicitLegacyOwner_IsDeterministicAcrossApplicationOrder(string order)
    {
        var function = $"namespaced-explicit-adoption-{order}";
        var legacy = SeededRow(function);
        legacy.SeedKey = function;
        await Provider.InsertCronTickers([legacy], CancellationToken.None);

        var ownership = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            [function] = "competing-app-a"
        };
        var appA = new DefinedCronSeedManifest("competing-app-a",
            [new DefinedCronTickerSeed(function, "*/7 * * * *")], ownership);
        var appB = new DefinedCronSeedManifest("competing-app-b",
            [new DefinedCronTickerSeed(function, "*/11 * * * *")], ownership);

        if (order == "a-first")
        {
            await Provider.MigrateDefinedCronTickers(appA, CancellationToken.None);
            await Provider.MigrateDefinedCronTickers(appB, CancellationToken.None);
        }
        else if (order == "b-first")
        {
            await Provider.MigrateDefinedCronTickers(appB, CancellationToken.None);
            await Provider.MigrateDefinedCronTickers(appA, CancellationToken.None);
        }
        else
        {
            await Task.WhenAll(
                Provider.MigrateDefinedCronTickers(appA, CancellationToken.None),
                Provider.MigrateDefinedCronTickers(appB, CancellationToken.None));
        }

        var rows = await Provider.GetCronTickers(r => r.Function == function, CancellationToken.None);
        Assert.Equal(2, rows.Length);
        Assert.Single(rows, r => r.Id == legacy.Id
                                 && r.SeedOwnerNamespace == "competing-app-a"
                                 && r.SeedKey == CronSeedIdentity.SeedKey("competing-app-a", function));
        Assert.Single(rows, r => r.SeedOwnerNamespace == "competing-app-b"
                                 && r.SeedKey == CronSeedIdentity.SeedKey("competing-app-b", function));
    }

    [Fact]
    public async Task Namespaced_ExplicitLegacyOwner_AdoptsCanonicallyEquivalentDecomposedFunction()
    {
        const string applicationNamespace = "unicode-adopter";
        const string composedFunction = "Caf\u00e9Job";
        const string decomposedFunction = "Cafe\u0301Job";
        var legacy = SeededRow(decomposedFunction);
        legacy.SeedKey = decomposedFunction;
        await Provider.InsertCronTickers([legacy], CancellationToken.None);
        var manifest = OwnedManifestWithLegacyOwner(applicationNamespace, composedFunction,
            new DefinedCronTickerSeed(composedFunction, "*/9 * * * *"));

        await Provider.MigrateDefinedCronTickers(manifest, CancellationToken.None);

        var adopted = Assert.Single(await Provider.GetCronTickers(
            row => row.Id == legacy.Id, CancellationToken.None));
        Assert.Equal(applicationNamespace, adopted.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey(applicationNamespace, composedFunction), adopted.SeedKey);
    }

    [Fact]
    public async Task Namespaced_ExplicitLegacyOwner_AdoptsOutOfOrderCombiningMarkPlaintextKey()
    {
        const string canonicalNamespace = "legacy-\u00e0\u0315-owner";
        const string rawNamespace = "legacy-a\u0315\u0300-owner";
        const string canonicalFunction = "job-\u00e0\u0315";
        const string rawFunction = "job-a\u0315\u0300";
        var legacy = SeededRow(canonicalFunction);
        legacy.SeedKey = $"{rawNamespace}:{rawFunction}";
        await Provider.InsertCronTickers([legacy], CancellationToken.None);
        var manifest = OwnedManifestWithLegacyOwner(canonicalNamespace, canonicalFunction,
            new DefinedCronTickerSeed(canonicalFunction, "*/11 * * * *"));

        await Provider.MigrateDefinedCronTickers(manifest, CancellationToken.None);

        var rows = (await Provider.GetCronTickers(_ => true, CancellationToken.None))
            .Where(row => CronSeedIdentity.CanonicallyEquals(row.Function, canonicalFunction)).ToArray();
        var adopted = Assert.Single(rows);
        Assert.Equal(legacy.Id, adopted.Id);
        Assert.True(adopted.IsEnabled);
        Assert.Equal(canonicalNamespace, adopted.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey(canonicalNamespace, canonicalFunction), adopted.SeedKey);
    }

    [Fact]
    public async Task Namespaced_ExplicitLegacyOwner_AdoptsOutOfOrderBareFunctionKeyWithDistinctStableId()
    {
        const string applicationNamespace = "bare-key-adopter";
        const string canonicalFunction = "job-\u00e0\u0315";
        const string rawFunction = "job-a\u0315\u0300";
        const string stableDefinitionId = "stable-job-definition";
        var legacy = SeededRow(canonicalFunction);
        legacy.SeedKey = rawFunction;
        await Provider.InsertCronTickers([legacy], CancellationToken.None);
        var manifest = OwnedManifestWithLegacyOwner(applicationNamespace, canonicalFunction,
            new DefinedCronTickerSeed(canonicalFunction, "*/13 * * * *",
                stableDefinitionId: stableDefinitionId));

        await Provider.MigrateDefinedCronTickers(manifest, CancellationToken.None);

        var rows = (await Provider.GetCronTickers(_ => true, CancellationToken.None))
            .Where(row => CronSeedIdentity.CanonicallyEquals(row.Function, canonicalFunction)).ToArray();
        var adopted = Assert.Single(rows);
        Assert.Equal(legacy.Id, adopted.Id);
        Assert.True(adopted.IsEnabled);
        Assert.Equal(applicationNamespace, adopted.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey(applicationNamespace, stableDefinitionId), adopted.SeedKey);
    }

    [Fact]
    public async Task Namespaced_PreNfcV2OwnedSeed_IsCanonicalizedInPlaceWithoutDuplicate()
    {
        const string composedNamespace = "Caf\u00e9-R\u00e9sum\u00e9-\u00e0\u0315-owner";
        const string mixedNamespace = "Cafe\u0301-R\u00e9sume\u0301-a\u0315\u0300-owner";
        const string composedFunction = "R\u00e9sum\u00e9-Caf\u00e9Job-\u00e0\u0315";
        const string mixedFunction = "Re\u0301sum\u00e9-Cafe\u0301Job-a\u0315\u0300";
        var canonicalKey = CronSeedIdentity.SeedKey(composedNamespace, composedFunction);
        var manifest = OwnedManifest(composedNamespace,
            new DefinedCronTickerSeed(composedFunction, "*/5 * * * *"));
        var desired = Assert.Single(manifest.Seeds);
        var owned = SeededRow(mixedFunction);
        owned.Expression = desired.Expression;
        owned.SeedOwnerNamespace = mixedNamespace;
        owned.SeedKey = PreNfcV2SeedKey(mixedNamespace, mixedFunction);
        owned.RequestContractVersion = desired.RequestContractVersion;
        owned.RequestContractFingerprint = desired.RequestContractFingerprint;
        owned.Retries = desired.Retries;
        owned.RetryIntervals = desired.RetryIntervals;
        owned.TimeoutSeconds = desired.TimeoutSeconds;
        await Provider.InsertCronTickers([owned], CancellationToken.None);
        var pending = NewOccurrence(owned.Id);
        await Provider.InsertCronTickerOccurrences([pending], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(manifest, CancellationToken.None);

        var rows = (await Provider.GetCronTickers(_ => true, CancellationToken.None))
            .Where(row => CronSeedIdentity.CanonicallyEquals(row.Function, composedFunction)).ToArray();
        var canonical = Assert.Single(rows);
        Assert.Equal(owned.Id, canonical.Id);
        Assert.Equal(composedNamespace, canonical.SeedOwnerNamespace);
        Assert.Equal(canonicalKey, canonical.SeedKey);
        var persistedOccurrence = Assert.Single(await Provider.GetAllCronTickerOccurrences(
            occurrence => occurrence.Id == pending.Id, CancellationToken.None));
        Assert.Equal(TickerStatus.Idle, persistedOccurrence.Status);
        Assert.Null(persistedOccurrence.SkippedReason);
    }

    private static string PreNfcV2SeedKey(string applicationNamespace, string stableDefinitionId)
    {
        var application = Encoding.UTF8.GetBytes(applicationNamespace);
        var definition = Encoding.UTF8.GetBytes(stableDefinitionId);
        var framed = new byte[1 + 4 + application.Length + 4 + definition.Length];
        framed[0] = 2;
        BinaryPrimitives.WriteInt32BigEndian(framed.AsSpan(1, 4), application.Length);
        application.CopyTo(framed.AsSpan(5));
        var definitionOffset = 5 + application.Length;
        BinaryPrimitives.WriteInt32BigEndian(framed.AsSpan(definitionOffset, 4), definition.Length);
        definition.CopyTo(framed.AsSpan(definitionOffset + 4));
        return "tq:cron-seed:v2:" + Convert.ToHexString(SHA256.HashData(framed)).ToLowerInvariant();
    }

    [Fact]
    public async Task Namespaced_UnclaimedLegacyCandidate_FailsBeforeAnyMutation()
    {
        const string function = "namespaced-unclaimed-legacy";
        var legacy = SeededRow(function);
        legacy.SeedKey = function;
        await Provider.InsertCronTickers([legacy], CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Provider.MigrateDefinedCronTickers(OwnedManifest("unclaimed-app",
                new DefinedCronTickerSeed(function, "*/7 * * * *")), CancellationToken.None));

        Assert.Contains("explicit legacy ownership", error.Message, StringComparison.OrdinalIgnoreCase);
        var row = Assert.Single(await Provider.GetCronTickers(r => r.Function == function, CancellationToken.None));
        Assert.Equal(legacy.Id, row.Id);
        Assert.Null(row.SeedOwnerNamespace);
        Assert.Equal(function, row.SeedKey);
    }

    [Fact]
    public async Task DefinitionRevision_NewSeedIsPositive_AndSemanticChangesIncrementExactlyOnce()
    {
        const string function = "definition-revision-semantic";
        var initial = OwnedManifest("revision-contract",
            new DefinedCronTickerSeed(function, "*/5 * * * *", 1, "sha256:v1"));
        await Provider.MigrateDefinedCronTickers(initial, CancellationToken.None);

        var created = Assert.Single(await Provider.GetCronTickers(
            x => x.Function == function, CancellationToken.None));
        Assert.True(created.DefinitionRevision > 0);
        var firstRevision = created.DefinitionRevision;

        // Last-seen/bookkeeping reconciliation is not a semantic publication.
        await Provider.MigrateDefinedCronTickers(initial, CancellationToken.None);
        var unchanged = Assert.Single(await Provider.GetCronTickers(
            x => x.Function == function, CancellationToken.None));
        Assert.Equal(firstRevision, unchanged.DefinitionRevision);

        await Provider.MigrateDefinedCronTickers(OwnedManifest("revision-contract",
            new DefinedCronTickerSeed(function, "*/9 * * * *", 2, "sha256:v2")), CancellationToken.None);
        var changed = Assert.Single(await Provider.GetCronTickers(
            x => x.Function == function, CancellationToken.None));
        Assert.Equal(firstRevision + 1, changed.DefinitionRevision);

        await Provider.MigrateDefinedCronTickers(OwnedManifest("revision-contract",
            new DefinedCronTickerSeed(function, "*/9 * * * *", 2, "sha256:v2")), CancellationToken.None);
        var repeated = Assert.Single(await Provider.GetCronTickers(
            x => x.Function == function, CancellationToken.None));
        Assert.Equal(changed.DefinitionRevision, repeated.DefinitionRevision);
    }

    [Fact]
    public async Task DefinitionRevision_EachCodeDefinedExecutionPolicyField_IncrementsExactlyOnce()
    {
        const string function = "definition-revision-execution-policy";
        var seed = new DefinedCronTickerSeed(function, "*/5 * * * *", 1, "sha256:v1",
            retries: 1, retryIntervals: [5], timeoutSeconds: 30);
        await Provider.MigrateDefinedCronTickers(OwnedManifest("revision-policy", seed), CancellationToken.None);

        var row = Assert.Single(await Provider.GetCronTickers(x => x.Function == function, CancellationToken.None));
        var revision = row.DefinitionRevision;
        Assert.Equal(1, row.Retries);
        Assert.Equal([5], row.RetryIntervals);
        Assert.Equal(30, row.TimeoutSeconds);

        async Task AssertSingleIncrement(DefinedCronTickerSeed changed)
        {
            await Provider.MigrateDefinedCronTickers(OwnedManifest("revision-policy", changed), CancellationToken.None);
            var updated = Assert.Single(await Provider.GetCronTickers(x => x.Function == function, CancellationToken.None));
            Assert.Equal(++revision, updated.DefinitionRevision);

            await Provider.MigrateDefinedCronTickers(OwnedManifest("revision-policy", changed), CancellationToken.None);
            var replayed = Assert.Single(await Provider.GetCronTickers(x => x.Function == function, CancellationToken.None));
            Assert.Equal(revision, replayed.DefinitionRevision);
        }

        await AssertSingleIncrement(new DefinedCronTickerSeed(function, "*/5 * * * *", 1, "sha256:v1",
            retries: 2, retryIntervals: [5], timeoutSeconds: 30));
        await AssertSingleIncrement(new DefinedCronTickerSeed(function, "*/5 * * * *", 1, "sha256:v1",
            retries: 2, retryIntervals: [5, 10], timeoutSeconds: 30));
        await AssertSingleIncrement(new DefinedCronTickerSeed(function, "*/5 * * * *", 1, "sha256:v1",
            retries: 2, retryIntervals: [5, 10], timeoutSeconds: 60));
    }

    [Fact]
    public async Task Namespaced_SameFunctionInTwoApplications_CreatesIndependentRows()
    {
        const string function = "namespaced-shared-function";
        await Provider.MigrateDefinedCronTickers(
            OwnedManifest("contract-app-a", new DefinedCronTickerSeed(function, "*/5 * * * *")), CancellationToken.None);
        await Provider.MigrateDefinedCronTickers(
            OwnedManifest("contract-app-b", new DefinedCronTickerSeed(function, "*/9 * * * *")), CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == function, CancellationToken.None);
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, r => r.SeedOwnerNamespace == "contract-app-a"
            && r.SeedKey == CronSeedIdentity.SeedKey("contract-app-a", function));
        Assert.Contains(rows, r => r.SeedOwnerNamespace == "contract-app-b"
            && r.SeedKey == CronSeedIdentity.SeedKey("contract-app-b", function));
    }

    [Fact]
    public async Task Namespaced_RetirementIsIsolatedToManifestOwner()
    {
        const string function = "namespaced-retirement-isolation";
        await Provider.MigrateDefinedCronTickers(
            OwnedManifest("retire-app-a", new DefinedCronTickerSeed(function, "*/5 * * * *")), CancellationToken.None);
        await Provider.MigrateDefinedCronTickers(
            OwnedManifest("retire-app-b", new DefinedCronTickerSeed(function, "*/9 * * * *")), CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(OwnedManifest("retire-app-a"), CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == function, CancellationToken.None);
        Assert.NotNull(Assert.Single(rows, r => r.SeedOwnerNamespace == "retire-app-a").RetirementRequestedAt);
        Assert.Null(Assert.Single(rows, r => r.SeedOwnerNamespace == "retire-app-b").RetirementRequestedAt);
    }

    [Fact]
    public async Task Namespaced_SingleLegacyGlobalRow_IsAdoptedInPlace()
    {
        const string function = "namespaced-legacy-adoption";
        var legacy = SeededRow(function);
        legacy.SeedKey = function;
        await Provider.InsertCronTickers([legacy], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            OwnedManifestWithLegacyOwner("legacy-owner", function,
                new DefinedCronTickerSeed(function, "*/7 * * * *")), CancellationToken.None);

        var row = Assert.Single(await Provider.GetCronTickers(r => r.Function == function, CancellationToken.None));
        Assert.Equal(legacy.Id, row.Id);
        Assert.Equal("legacy-owner", row.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey("legacy-owner", function), row.SeedKey);
    }

    [Fact]
    public async Task Namespaced_AmbiguousLegacyRows_FailClosedWithoutMutation()
    {
        const string function = "namespaced-ambiguous-adoption";
        var a = SeededRow(function);
        a.SeedKey = function;
        var b = SeededRow(function);
        b.SeedKey = null;
        await Provider.InsertCronTickers([a, b], CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider.MigrateDefinedCronTickers(
            OwnedManifestWithLegacyOwner("ambiguous-owner", function,
                new DefinedCronTickerSeed(function, "*/7 * * * *")), CancellationToken.None));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
        var rows = await Provider.GetCronTickers(r => r.Function == function, CancellationToken.None);
        Assert.All(rows, row => Assert.Null(row.SeedOwnerNamespace));
    }

    [Fact]
    public async Task Namespaced_ConcurrentFirstAdoption_ByOneOwner_ConvergesWithoutDuplicate()
    {
        const string function = "namespaced-concurrent-adoption";
        var legacy = SeededRow(function);
        legacy.SeedKey = function;
        await Provider.InsertCronTickers([legacy], CancellationToken.None);
        var manifest = OwnedManifestWithLegacyOwner("concurrent-owner", function,
            new DefinedCronTickerSeed(function, "*/7 * * * *"));

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Provider.MigrateDefinedCronTickers(manifest, CancellationToken.None)));

        var row = Assert.Single(await Provider.GetCronTickers(r => r.Function == function, CancellationToken.None));
        Assert.Equal(legacy.Id, row.Id);
        Assert.Equal("concurrent-owner", row.SeedOwnerNamespace);
    }

    // ---------------------------------------------------------------------
    // Removing only a cron expression drops the function from the desired seed
    // manifest, but Slice-3 retirement is NON-DESTRUCTIVE: on the first pass the
    // stale seeded row enters the grace window (RetirementRequestedAt stamped,
    // still enabled), and its row + occurrences/history are preserved — never
    // deleted. (Updated from the Slice-1 deletion semantics.)
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CronExpressionRemoved_EntersGraceWithoutDelete_AndKeepsOccurrences()
    {
        var removed = SeededRow("removed-fn");
        var kept = SeededRow("kept-fn");
        await Provider.InsertCronTickers([removed, kept], CancellationToken.None);
        var occurrence = NewOccurrence(removed.Id);
        await Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        // Only "kept-fn" is still a desired seed; "removed-fn" dropped out of the manifest when its
        // cron expression was removed, even if its function is still invocable/registered elsewhere.
        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("kept-fn", "*/5 * * * *", 1, null)],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(_ => true, CancellationToken.None);

        // Phase A: retirement requested, but NOT deleted and still enabled during grace.
        var stale = Assert.Single(rows, r => r.Id == removed.Id);
        Assert.Equal(Now, stale.RetirementRequestedAt);
        Assert.Null(stale.RetiredAt);
        Assert.True(stale.IsEnabled);
        Assert.Contains(rows, r => r.Id == kept.Id);

        // History preserved — the occurrence is never removed by reconciliation.
        var occurrences = await Provider.GetAllCronTickerOccurrences(o => o.CronTickerId == removed.Id, CancellationToken.None);
        Assert.Single(occurrences);
    }

    // ---------------------------------------------------------------------
    // A blocked seed (required-request function that a code-empty payload can
    // never satisfy) retires IMMEDIATELY — no grace — because continuing to
    // schedule an unsatisfiable request is unsafe. The prior auto-seeded row is
    // disabled and marked retired (NOT deleted, occurrences preserved), while a
    // user/dashboard row sharing the function name survives untouched.
    // (Updated from the Slice-1 deletion semantics.)
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BlockedSeed_RetiresImmediately_PreservesRowOccurrencesAndUserRow()
    {
        var seeded = SeededRow("blocked-fn");
        var userRow = UserRow("blocked-fn");
        await Provider.InsertCronTickers([seeded, userRow], CancellationToken.None);
        // A TERMINAL occurrence is history and must survive immediate (Phase-B) retirement untouched —
        // only UNLEASED PENDING work is cleaned up (proven separately by the Slice-4 cases below).
        var occurrence = NewTerminalOccurrence(seeded.Id);
        await Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("blocked-fn", "*/5 * * * *", 2, "sha256:req", canSeed: false)],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(_ => true, CancellationToken.None);

        // Immediate retirement: disabled + both timestamps stamped now, prior enabled state saved.
        var retired = Assert.Single(rows, r => r.Id == seeded.Id);
        Assert.False(retired.IsEnabled);
        Assert.Equal(Now, retired.RetirementRequestedAt);
        Assert.Equal(Now, retired.RetiredAt);
        Assert.True(retired.SeedWasEnabledBeforeRetirement);

        // User row is never touched by reconciliation.
        var user = Assert.Single(rows, r => r.Id == userRow.Id);
        Assert.True(user.IsEnabled);
        Assert.Null(user.RetirementRequestedAt);
        Assert.Null(user.RetiredAt);

        var occurrences = await Provider.GetAllCronTickerOccurrences(o => o.CronTickerId == seeded.Id, CancellationToken.None);
        Assert.Single(occurrences); // terminal history preserved through immediate retirement
    }

    [Fact]
    public async Task CanonicallyEquivalentBlockedSeed_RetiresImmediately()
    {
        const string storedFunction = "blocked-canonical-a\u0315\u0300";
        const string manifestFunction = "blocked-canonical-a\u0300\u0315";
        var seeded = SeededRow(storedFunction);
        await Provider.InsertCronTickers([seeded], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(
                manifestFunction, "*/5 * * * *", 2, "sha256:req", canSeed: false)],
            CancellationToken.None);

        var retired = Assert.Single(await Provider.GetCronTickers(
            row => row.Id == seeded.Id, CancellationToken.None));
        Assert.False(retired.IsEnabled);
        Assert.Equal(Now, retired.RetirementRequestedAt);
        Assert.Equal(Now, retired.RetiredAt);
        Assert.True(retired.SeedWasEnabledBeforeRetirement);
    }

    // ---------------------------------------------------------------------
    // A user/dashboard row (no seed ownership) that shares a function name with
    // a desired seed is never claimed by reconciliation: it keeps its identity,
    // and the seed owns a separate freshly-inserted row.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task UserRowSharingFunctionName_Untouched_SeedInsertsSeparateRow()
    {
        var userRow = UserRow("shared-fn");
        userRow.Expression = "0 0 * * *";
        await Provider.InsertCronTickers([userRow], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("shared-fn", "*/5 * * * *", 9, "sha256:seed-only")],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "shared-fn", CancellationToken.None);

        var preserved = Assert.Single(rows, r => r.Id == userRow.Id);
        Assert.Equal("0 0 * * *", preserved.Expression);
        Assert.Null(preserved.RequestContractVersion);

        var seededRow = Assert.Single(rows, r => r.Id != userRow.Id);
        Assert.Equal(CronExpression.Parse("*/5 * * * *").Value, seededRow.Expression);
        Assert.Equal(9, seededRow.RequestContractVersion);
        Assert.StartsWith("MemoryTicker_Seeded_", seededRow.InitIdentifier);
    }

    // =====================================================================
    // Slice 2: stable seed ownership (SeedKey) and concurrent convergence.
    // =====================================================================

    // A newly created code-owned row is stamped with the stable seed key (derived from the function
    // identity, NOT the expression) and a deterministic primary key derived from that seed key.
    [Fact]
    public async Task NewSeededRow_StampsStableSeedKey_AndDeterministicPrimaryKey()
    {
        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("seedkey-fn", "*/5 * * * *", 1, null)],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "seedkey-fn", CancellationToken.None);
        var row = Assert.Single(rows);
        var expectedKey = CronSeedIdentity.SeedKeyForFunction("seedkey-fn");
        Assert.Equal(expectedKey, row.SeedKey);
        Assert.Equal(CronSeedIdentity.DeterministicId(expectedKey), row.Id);
    }

    // A legacy seeded row written before seed ownership (null SeedKey, arbitrary primary key that
    // occurrences already reference) adopts the SeedKey IN PLACE — its primary key never changes.
    [Fact]
    public async Task LegacySeededRow_AdoptsSeedKeyInPlace_WithoutChangingPrimaryKey()
    {
        var legacy = SeededRow("legacy-fn");
        legacy.SeedKey = null; // predates seed ownership
        await Provider.InsertCronTickers([legacy], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("legacy-fn", "*/7 * * * *", 1, null)],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "legacy-fn", CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal(legacy.Id, row.Id); // adopted in place — occurrences still resolve
        Assert.Equal(CronSeedIdentity.SeedKeyForFunction("legacy-fn"), row.SeedKey);
        Assert.Equal(CronExpression.Parse("*/7 * * * *").Value, row.Expression); // reconciled in place
    }

    // Repeated reconciliation is idempotent: never a second row for the same seed key.
    [Fact]
    public async Task RepeatedMigrations_ConvergeToSingleSeededRow()
    {
        var seed = new DefinedCronTickerSeed("idem-fn", "*/5 * * * *", 1, null);
        await Provider.MigrateDefinedCronTickers([seed], CancellationToken.None);
        await Provider.MigrateDefinedCronTickers([seed], CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "idem-fn", CancellationToken.None);
        Assert.Single(rows);
    }

    // Concurrent first-time reconciles converge to exactly one active row per seed key (deterministic
    // id forces a primary-key collision; the provider treats the losing race as converged, not fatal).
    [Fact]
    public async Task ConcurrentMigrations_ConvergeToSingleSeededRow()
    {
        var seed = new DefinedCronTickerSeed("race-fn", "*/5 * * * *", 1, null);
        var tasks = Enumerable.Range(0, 4)
            .Select(_ => Provider.MigrateDefinedCronTickers([seed], CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        var rows = await Provider.GetCronTickers(r => r.Function == "race-fn", CancellationToken.None);
        Assert.Single(rows);
    }

    // Pre-existing duplicate legacy seeded rows for one function are duplicate-tolerant: reconciliation
    // selects a deterministic canonical row that adopts the seed key, never crashes (e.g. a keyed
    // ToDictionary), and does NOT destructively delete history — the extra remains (retirement is
    // Slice 3). Only the canonical adopts a non-null SeedKey, so the unique SeedKey index is never
    // violated during legacy adoption (safe adoption path).
    [Fact]
    public async Task DuplicateLegacySeededRows_ConvergeCanonical_WithoutCrashOrDataLoss()
    {
        var a = SeededRow("dup-fn");
        a.SeedKey = null;
        var b = SeededRow("dup-fn");
        b.SeedKey = null;
        await Provider.InsertCronTickers([a, b], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("dup-fn", "*/5 * * * *", 1, null)],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "dup-fn", CancellationToken.None);
        Assert.Equal(2, rows.Length); // history preserved; no destructive delete in Slice 2
        var seedKey = CronSeedIdentity.SeedKeyForFunction("dup-fn");
        Assert.Single(rows, r => r.SeedKey == seedKey); // exactly one canonical owner
        Assert.Single(rows, r => r.SeedKey == null);    // extra stays null-keyed until Slice 3
    }

    // A user/dashboard row (null SeedKey, empty InitIdentifier) is never claimed by seed ownership.
    [Fact]
    public async Task UserRowNullSeedKey_RemainsUntouched()
    {
        var userRow = UserRow("owned-elsewhere");
        await Provider.InsertCronTickers([userRow], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("owned-elsewhere", "*/5 * * * *", 1, null)],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Id == userRow.Id, CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Null(row.SeedKey);
        Assert.Equal(string.Empty, row.InitIdentifier);
    }

    // =====================================================================
    // Slice 3: two-phase non-destructive retirement lifecycle.
    // Grace defaults to 24h; scenarios are driven at the fixed clock by
    // pre-setting the retirement markers a prior pass would have written.
    // =====================================================================

    // After the grace window elapses, a still-absent seed is disabled and marked retired — but its row
    // and occurrences are preserved, never deleted, and its prior enabled state is captured.
    [Fact]
    public async Task GraceExpired_DisablesAndMarksRetired_WithoutDelete()
    {
        var row = SeededRow("grace-expired-fn");
        row.RetirementRequestedAt = Now.AddHours(-25); // grace (24h) already elapsed
        row.RetiredAt = null;
        row.IsEnabled = true;
        await Provider.InsertCronTickers([row], CancellationToken.None);
        // A TERMINAL occurrence is history: Phase-B retirement preserves it (only unleased pending is cleaned).
        await Provider.InsertCronTickerOccurrences([NewTerminalOccurrence(row.Id)], CancellationToken.None);

        // Reconcile with a different desired seed so "grace-expired-fn" stays absent.
        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("other-fn", "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(
            await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.False(persisted.IsEnabled);
        Assert.Equal(Now, persisted.RetiredAt);
        Assert.Equal(Now.AddHours(-25), persisted.RetirementRequestedAt); // original request not disturbed
        Assert.True(persisted.SeedWasEnabledBeforeRetirement);

        var occurrences = await Provider.GetAllCronTickerOccurrences(o => o.CronTickerId == row.Id, CancellationToken.None);
        Assert.Single(occurrences); // terminal history preserved through Phase-B retirement
    }

    // RetirementRequestedAt is stamped once and never re-stamped while the seed stays absent, so the
    // grace window is measured from first absence, not reset on every reconcile.
    [Fact]
    public async Task RetirementTimestamp_StableAcrossRepeatedAbsentPasses()
    {
        var firstSeen = Now.AddHours(-2); // within the 24h grace
        var row = SeededRow("stable-ts-fn");
        row.RetirementRequestedAt = firstSeen;
        row.RetiredAt = null;
        await Provider.InsertCronTickers([row], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("other-fn", "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(
            await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.Equal(firstSeen, persisted.RetirementRequestedAt); // NOT reset to Now
        Assert.Null(persisted.RetiredAt);                          // still within grace
        Assert.True(persisted.IsEnabled);
    }

    // A seed that reappears while still inside its grace window has its retirement request cleared and
    // keeps its prior enabled state intact (no disable ever happened).
    [Fact]
    public async Task ReappearanceBeforeGrace_CancelsRetirement()
    {
        var row = SeededRow("reappear-early-fn");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("reappear-early-fn");
        row.RetirementRequestedAt = Now.AddHours(-2); // in grace, not yet retired
        row.RetiredAt = null;
        row.IsEnabled = true;
        await Provider.InsertCronTickers([row], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("reappear-early-fn", "*/9 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(
            await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.Null(persisted.RetirementRequestedAt);
        Assert.Null(persisted.RetiredAt);
        Assert.True(persisted.IsEnabled);
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, persisted.Expression); // refreshed
        Assert.Equal(Now, persisted.SeedLastSeenAt);
    }

    // A seed that reappears AFTER framework retirement is re-enabled only because it was enabled before
    // retirement; the saved state is cleared once restoration is applied.
    [Fact]
    public async Task ReappearanceAfterRetirement_RestoresFrameworkDisabledState()
    {
        var row = SeededRow("restore-fn");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("restore-fn");
        row.RetirementRequestedAt = Now.AddHours(-25);
        row.RetiredAt = Now.AddHours(-25);
        row.IsEnabled = false;                       // framework disabled it
        row.SeedWasEnabledBeforeRetirement = true;   // ...but it was enabled before that
        await Provider.InsertCronTickers([row], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("restore-fn", "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(
            await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.True(persisted.IsEnabled);
        Assert.Null(persisted.RetirementRequestedAt);
        Assert.Null(persisted.RetiredAt);
        Assert.Null(persisted.SeedWasEnabledBeforeRetirement); // cleared after restoration
    }

    // A seed the user had already disabled before framework retirement stays disabled on reappearance —
    // only framework-disabled seeds are restored.
    [Fact]
    public async Task ReappearanceAfterRetirement_PreservesUserDisabledState()
    {
        var row = SeededRow("keep-disabled-fn");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("keep-disabled-fn");
        row.RetirementRequestedAt = Now.AddHours(-25);
        row.RetiredAt = Now.AddHours(-25);
        row.IsEnabled = false;
        row.SeedWasEnabledBeforeRetirement = false;  // user had disabled it before retirement
        await Provider.InsertCronTickers([row], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("keep-disabled-fn", "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(
            await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.False(persisted.IsEnabled);           // preserved
        Assert.Null(persisted.RetirementRequestedAt);
        Assert.Null(persisted.RetiredAt);
        Assert.Null(persisted.SeedWasEnabledBeforeRetirement);
    }

    // A desired, active seed the user has disabled (no framework retirement in play) is refreshed but
    // never force-enabled by reconciliation.
    [Fact]
    public async Task UserDisabledDesiredSeed_RemainsDisabled()
    {
        var row = SeededRow("user-disabled-fn");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("user-disabled-fn");
        row.IsEnabled = false;   // user disabled an actively-desired seed
        await Provider.InsertCronTickers([row], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("user-disabled-fn", "*/11 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(
            await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.False(persisted.IsEnabled);           // never force-enabled
        Assert.Equal(CronExpression.Parse("*/11 * * * *").Value, persisted.Expression); // still refreshed
    }

    // Duplicate legacy code-owned rows converge to exactly one enabled canonical (lowest id) row; every
    // redundant duplicate is disabled and marked retired but preserved (never deleted).
    [Fact]
    public async Task DuplicateLegacyRows_LeaveExactlyOneEnabled_AndPreserveAllRows()
    {
        var a = SeededRow("dup-retire-fn");
        a.SeedKey = null;
        var b = SeededRow("dup-retire-fn");
        b.SeedKey = null;
        await Provider.InsertCronTickers([a, b], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("dup-retire-fn", "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "dup-retire-fn", CancellationToken.None);
        Assert.Equal(2, rows.Length); // both preserved

        var seedKey = CronSeedIdentity.SeedKeyForFunction("dup-retire-fn");
        var canonical = Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.True(canonical.IsEnabled);
        Assert.Null(canonical.RetiredAt);

        var redundant = Assert.Single(rows, r => r.SeedKey == null);
        Assert.False(redundant.IsEnabled);      // exactly one enabled row remains
        Assert.NotNull(redundant.RetiredAt);    // redundant duplicate retired in place
    }

    // ---------------------------------------------------------------------
    // Finding 1: a function already owns a keyed canonical row (its stable
    // SeedKey adopted) at a HIGHER id, and a lower-id legacy null-key duplicate
    // still exists. Canonical selection must prefer the ALREADY-KEYED row — not
    // the lowest id — otherwise it would try to re-assign the in-use SeedKey to
    // the legacy row (unique-index violation / wrong ownership) and retire the
    // real owner. The keyed row must stay canonical and enabled; the legacy
    // duplicate is retired in place, never re-keyed and never deleted.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task KeyedCanonicalWithLowerIdLegacyDuplicate_KeepsKeyedRowCanonical_NoKeyMovement()
    {
        var seedKey = CronSeedIdentity.SeedKeyForFunction("keyed-canon-fn");

        // Ids differ only in the final byte, so .NET Guid ordering puts the legacy row first.
        var legacy = SeededRow("keyed-canon-fn");
        legacy.Id = new Guid("11111111-1111-1111-1111-111111111111"); // lower id
        legacy.SeedKey = null;                                        // predates seed ownership

        var keyed = SeededRow("keyed-canon-fn");
        keyed.Id = new Guid("11111111-1111-1111-1111-111111111112");  // higher id
        keyed.SeedKey = seedKey;                                      // already owns the desired key

        await Provider.InsertCronTickers([legacy, keyed], CancellationToken.None);

        // Must not throw (no attempt to duplicate the in-use SeedKey onto the legacy row).
        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("keyed-canon-fn", "*/5 * * * *", 1, null)],
            CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "keyed-canon-fn", CancellationToken.None);
        Assert.Equal(2, rows.Length); // both rows preserved

        // The already-keyed higher-id row stays the single canonical owner and enabled — key never moves.
        var canonical = Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.Equal(keyed.Id, canonical.Id);
        Assert.True(canonical.IsEnabled);
        Assert.Null(canonical.RetiredAt);

        // The lower-id legacy duplicate is retired in place and stays null-keyed.
        var redundant = Assert.Single(rows, r => r.Id == legacy.Id);
        Assert.Null(redundant.SeedKey);
        Assert.False(redundant.IsEnabled);
        Assert.NotNull(redundant.RetiredAt);

        // Exactly one enabled owner overall.
        Assert.Single(rows, r => r.IsEnabled);
    }

    // =====================================================================
    // Slice 4: expression / contract reconciliation with durable pending quarantine.
    // When the canonical code-owned row's Expression or request-contract identity
    // changes — or the seed retires to Phase B — only UNLEASED PENDING occurrences
    // for that CronTicker are quarantined as durable Skipped evidence. Quarantinable is
    // defined conservatively: Idle or Queued, empty/null LockHolder, null AcquisitionToken,
    // and no live LeaseUntil. Leased/owned Queued and InProgress survive unchanged while
    // their lease is live; every terminal occurrence survives unchanged. Stale leased work
    // is revision-fenced from execution and transitions only when lease recovery makes it safe.
    // =====================================================================

    [Fact]
    public async Task ExpressionChange_QuarantinesUnleasedPendingOccurrence_AndPreservesPayloadEvidence()
    {
        var row = SeededRow("expr-change-fn", "*/5 * * * *");
        row.Request = [11, 22, 33];
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("expr-change-fn");
        await Provider.InsertCronTickers([row], CancellationToken.None);
        var pending = NewOccurrence(row.Id);
        await Provider.InsertCronTickerOccurrences([pending], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("expr-change-fn", "*/9 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, persisted.Expression);
        var occurrence = Assert.Single(await Provider.GetAllCronTickerOccurrences(
            o => o.CronTickerId == row.Id, CancellationToken.None));
        AssertQuarantined(pending, occurrence);
        Assert.Equal(row.Request,
            await Provider.GetCronTickerOccurrenceRequest(pending.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ContractOnlyChange_QuarantinesUnleasedPendingOccurrence()
    {
        var row = SeededRow("contract-change-fn", "*/5 * * * *");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("contract-change-fn");
        row.RequestContractVersion = 1;
        row.RequestContractFingerprint = "sha256:old";
        await Provider.InsertCronTickers([row], CancellationToken.None);
        var pending = NewOccurrence(row.Id);
        await Provider.InsertCronTickerOccurrences([pending], CancellationToken.None);

        // Same expression; only the request-contract identity changes.
        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("contract-change-fn", "*/5 * * * *", 2, "sha256:new")], CancellationToken.None);

        var persisted = Assert.Single(await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.Equal(2, persisted.RequestContractVersion);
        Assert.Equal("sha256:new", persisted.RequestContractFingerprint);
        var occurrence = Assert.Single(await Provider.GetAllCronTickerOccurrences(
            o => o.CronTickerId == row.Id, CancellationToken.None));
        AssertQuarantined(pending, occurrence);
    }

    [Fact]
    public async Task UnchangedReconcile_KeepsPendingOccurrence()
    {
        var row = SeededRow("noop-fn", "*/5 * * * *");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("noop-fn");
        row.RequestContractVersion = 1;
        row.RequestContractFingerprint = "sha256:same";
        await Provider.InsertCronTickers([row], CancellationToken.None);
        await Provider.InsertCronTickerOccurrences([NewOccurrence(row.Id)], CancellationToken.None);

        // Neither expression nor contract identity changes — nothing to reconcile, pending untouched.
        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("noop-fn", "*/5 * * * *", 1, "sha256:same")], CancellationToken.None);

        var occurrences = await Provider.GetAllCronTickerOccurrences(o => o.CronTickerId == row.Id, CancellationToken.None);
        Assert.Single(occurrences);
    }

    [Fact]
    public async Task PhaseAGrace_RetainsUnleasedPendingOccurrence()
    {
        var row = SeededRow("grace-keep-pending-fn");
        row.RetirementRequestedAt = Now.AddHours(-2); // still inside the 24h grace window
        await Provider.InsertCronTickers([row], CancellationToken.None);
        await Provider.InsertCronTickerOccurrences([NewOccurrence(row.Id)], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("other-fn", "*/5 * * * *", 1, null)], CancellationToken.None);

        // Grace has not elapsed: the definition is still enabled and pending work is NOT touched.
        var persisted = Assert.Single(await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.Null(persisted.RetiredAt);
        var occurrences = await Provider.GetAllCronTickerOccurrences(o => o.CronTickerId == row.Id, CancellationToken.None);
        Assert.Single(occurrences);
    }

    [Fact]
    public async Task PhaseBRetirement_GraceExpired_QuarantinesUnleasedPendingOccurrence()
    {
        var row = SeededRow("grace-b-pending-fn");
        row.RetirementRequestedAt = Now.AddHours(-25); // grace elapsed → Phase B this pass
        await Provider.InsertCronTickers([row], CancellationToken.None);
        var pending = NewOccurrence(row.Id);
        await Provider.InsertCronTickerOccurrences([pending], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("other-fn", "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.Equal(Now, persisted.RetiredAt);
        var occurrence = Assert.Single(await Provider.GetAllCronTickerOccurrences(
            o => o.CronTickerId == row.Id, CancellationToken.None));
        AssertQuarantined(pending, occurrence);
    }

    [Fact]
    public async Task BlockedSeed_QuarantinesUnleasedPendingOccurrence()
    {
        var row = SeededRow("blocked-pending-fn");
        await Provider.InsertCronTickers([row], CancellationToken.None);
        var pending = NewOccurrence(row.Id);
        await Provider.InsertCronTickerOccurrences([pending], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("blocked-pending-fn", "*/5 * * * *", 2, "sha256:req", canSeed: false)],
            CancellationToken.None);

        var persisted = Assert.Single(await Provider.GetCronTickers(r => r.Id == row.Id, CancellationToken.None));
        Assert.Equal(Now, persisted.RetiredAt); // immediate (blocked) retirement is Phase B
        var occurrence = Assert.Single(await Provider.GetAllCronTickerOccurrences(
            o => o.CronTickerId == row.Id, CancellationToken.None));
        AssertQuarantined(pending, occurrence);
    }

    [Fact]
    public async Task ExpressionChange_QuarantinesUnleasedPending_PreservesLiveLeasesAndTerminalEvidence()
    {
        var row = SeededRow("preserve-fn", "*/5 * * * *");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("preserve-fn");
        await Provider.InsertCronTickers([row], CancellationToken.None);

        var pending = NewOccurrence(row.Id);
        var leasedQueued = NewLeasedOccurrence(row.Id, TickerStatus.Queued);
        var inProgress = NewLeasedOccurrence(row.Id, TickerStatus.InProgress);
        var terminal = NewTerminalOccurrence(row.Id);
        await Provider.InsertCronTickerOccurrences(
            [pending, leasedQueued, inProgress, terminal], CancellationToken.None);

        await Provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("preserve-fn", "*/9 * * * *", 1, null)], CancellationToken.None);

        var occurrences = await Provider.GetAllCronTickerOccurrences(o => o.CronTickerId == row.Id, CancellationToken.None);
        Assert.Equal(4, occurrences.Length);
        AssertQuarantined(pending, Assert.Single(occurrences, o => o.Id == pending.Id));

        var storedQueued = Assert.Single(occurrences, o => o.Id == leasedQueued.Id);
        Assert.Equal(TickerStatus.Queued, storedQueued.Status);
        Assert.Equal(leasedQueued.LockHolder, storedQueued.LockHolder);
        Assert.Equal(leasedQueued.AcquisitionToken, storedQueued.AcquisitionToken);
        Assert.Equal(leasedQueued.LeaseUntil, storedQueued.LeaseUntil);

        var storedInProgress = Assert.Single(occurrences, o => o.Id == inProgress.Id);
        Assert.Equal(TickerStatus.InProgress, storedInProgress.Status);
        Assert.Equal(inProgress.LockHolder, storedInProgress.LockHolder);
        Assert.Equal(inProgress.AcquisitionToken, storedInProgress.AcquisitionToken);
        Assert.Equal(inProgress.LeaseUntil, storedInProgress.LeaseUntil);

        var storedTerminal = Assert.Single(occurrences, o => o.Id == terminal.Id);
        Assert.Equal(TickerStatus.Done, storedTerminal.Status);
        Assert.Equal(terminal.ExecutedAt, storedTerminal.ExecutedAt);
    }

    [Fact]
    public async Task RepeatedExpressionReconcile_IsIdempotent_AfterPendingQuarantine()
    {
        var row = SeededRow("idem-clean-fn", "*/5 * * * *");
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction("idem-clean-fn");
        await Provider.InsertCronTickers([row], CancellationToken.None);
        var pending = NewOccurrence(row.Id);
        await Provider.InsertCronTickerOccurrences([pending], CancellationToken.None);

        var reconcile = new DefinedCronTickerSeed("idem-clean-fn", "*/9 * * * *", 1, null);
        await Provider.MigrateDefinedCronTickers([reconcile], CancellationToken.None);
        await Provider.MigrateDefinedCronTickers([reconcile], CancellationToken.None);

        var rows = await Provider.GetCronTickers(r => r.Function == "idem-clean-fn", CancellationToken.None);
        var persisted = Assert.Single(rows);
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, persisted.Expression);
        var occurrence = Assert.Single(await Provider.GetAllCronTickerOccurrences(
            o => o.CronTickerId == row.Id, CancellationToken.None));
        AssertQuarantined(pending, occurrence);
    }

    private CronTickerOccurrenceEntity<CronTickerEntity> NewOccurrence(Guid cronTickerId)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = Now.AddMinutes(5),
            Status = TickerStatus.Idle,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private static void AssertQuarantined(
        CronTickerOccurrenceEntity<CronTickerEntity> original,
        CronTickerOccurrenceEntity<CronTickerEntity> stored)
    {
        Assert.Equal(original.Id, stored.Id);
        Assert.Equal(original.CronTickerId, stored.CronTickerId);
        Assert.Equal(original.DefinitionRevision, stored.DefinitionRevision);
        Assert.Equal(original.ExecutionTime, stored.ExecutionTime);
        Assert.Equal(original.CreatedAt, stored.CreatedAt);
        Assert.Equal(TickerStatus.Skipped, stored.Status);
        Assert.Contains("revision", stored.SkippedReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(stored.ExecutedAt);
    }

    // A leased/owned occurrence carrying ownership + a live lease — never removable.
    private CronTickerOccurrenceEntity<CronTickerEntity> NewLeasedOccurrence(Guid cronTickerId, TickerStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = Now.AddMinutes(status == TickerStatus.Queued ? 6 : 7),
            Status = status,
            LockHolder = "owner-node",
            LockedAt = Now,
            AcquisitionToken = Guid.NewGuid(),
            LeaseUntil = Now.AddMinutes(5),
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    // A terminal occurrence — history that reconciliation must never mutate or delete.
    private CronTickerOccurrenceEntity<CronTickerEntity> NewTerminalOccurrence(Guid cronTickerId)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = Now.AddMinutes(-5),
            Status = TickerStatus.Done,
            ExecutedAt = Now.AddMinutes(-4),
            CreatedAt = Now.AddMinutes(-6),
            UpdatedAt = Now.AddMinutes(-4),
        };

    private CronTickerEntity SeededRow(string function, string expression = "*/5 * * * *")
        => new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = expression,
            InitIdentifier = $"MemoryTicker_Seeded_{function}",
            Request = Array.Empty<byte>(),
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1),
            IsEnabled = true,
        };

    private CronTickerEntity UserRow(string function, string expression = "*/5 * * * *")
        => new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = expression,
            InitIdentifier = string.Empty,   // user/dashboard row: no seed ownership
            Request = Array.Empty<byte>(),
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1),
            IsEnabled = true,
        };
}
