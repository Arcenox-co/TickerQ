using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Slice 2 seed-ownership contract for the in-memory provider: stable <see cref="CronTickerEntity.SeedKey"/>,
/// legacy adoption in place, deterministic identity, concurrent convergence, and duplicate tolerance.
///
/// The in-memory provider keeps its cron store in process-wide static state, so these tests use unique
/// function names and remove exactly the rows they create in <see cref="DisposeAsync"/> to stay isolated
/// from other suites that share that static store.
/// </summary>
public sealed class TickerInMemoryPersistenceProviderSeedOwnershipTests : IAsyncLifetime
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly string _prefix = "seedown-" + Guid.NewGuid().ToString("N") + "-";
    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;

    public TickerInMemoryPersistenceProviderSeedOwnershipTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);

        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(new SchedulerOptionsBuilder());
        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(services.BuildServiceProvider());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        var mine = await _provider.GetCronTickers(c => c.Function.StartsWith(_prefix), CancellationToken.None);
        if (mine.Length > 0)
            await _provider.RemoveCronTickers(mine.Select(c => c.Id).ToArray(), CancellationToken.None);
    }

    private string Fn(string name) => _prefix + name;

    private static DefinedCronSeedManifest Manifest(string applicationNamespace, params DefinedCronTickerSeed[] seeds)
        => new(applicationNamespace, seeds);

    private static DefinedCronSeedManifest AdoptionManifest(string applicationNamespace,
        params DefinedCronTickerSeed[] seeds)
        => new(applicationNamespace, seeds,
            seeds.ToDictionary(seed => seed.Function, _ => applicationNamespace, StringComparer.Ordinal));

    [Fact]
    public async Task SameFunction_InTwoApplicationNamespaces_CreatesIndependentDefinitions()
    {
        var fn = Fn("shared-function");

        await _provider.MigrateDefinedCronTickers(
            Manifest("app-a", new DefinedCronTickerSeed(fn, "*/5 * * * *")), CancellationToken.None);
        await _provider.MigrateDefinedCronTickers(
            Manifest("app-b", new DefinedCronTickerSeed(fn, "*/9 * * * *")), CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, r => r.SeedOwnerNamespace == "app-a"
            && r.SeedKey == CronSeedIdentity.SeedKey("app-a", fn)
            && r.Expression == "0 */5 * * * *");
        Assert.Contains(rows, r => r.SeedOwnerNamespace == "app-b"
            && r.SeedKey == CronSeedIdentity.SeedKey("app-b", fn)
            && r.Expression == "0 */9 * * * *");
    }

    [Fact]
    public async Task EmptyManifest_RetiresOnlyRowsOwnedByItsApplicationNamespace()
    {
        var fn = Fn("retirement-isolation");
        await _provider.MigrateDefinedCronTickers(
            Manifest("app-a", new DefinedCronTickerSeed(fn, "*/5 * * * *")), CancellationToken.None);
        await _provider.MigrateDefinedCronTickers(
            Manifest("app-b", new DefinedCronTickerSeed(fn, "*/9 * * * *")), CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(Manifest("app-a"), CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        Assert.NotNull(Assert.Single(rows, r => r.SeedOwnerNamespace == "app-a").RetirementRequestedAt);
        Assert.Null(Assert.Single(rows, r => r.SeedOwnerNamespace == "app-b").RetirementRequestedAt);
    }

    [Fact]
    public async Task SingleLegacyGlobalSeedKey_IsAdoptedInPlaceByOneNamespace()
    {
        var fn = Fn("legacy-global");
        var legacy = new FakeCronTicker
        {
            Id = Guid.NewGuid(), Function = fn, Expression = "*/5 * * * *", SeedKey = fn,
            InitIdentifier = $"MemoryTicker_Seeded_{fn}", CreatedAt = _now, UpdatedAt = _now,
            Request = Array.Empty<byte>()
        };
        await _provider.InsertCronTickers([legacy], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            AdoptionManifest("app-a", new DefinedCronTickerSeed(fn, "*/7 * * * *")), CancellationToken.None);

        var row = Assert.Single(await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None));
        Assert.Equal(legacy.Id, row.Id);
        Assert.Equal("app-a", row.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey("app-a", fn), row.SeedKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DocumentedLegacySeedKeys_AreAdoptedInPlace(bool namespacedLegacyKey)
    {
        var fn = Fn("documented-legacy-key-" + namespacedLegacyKey);
        const string owner = "legacy-key-owner";
        const string stableId = "stable-definition";
        var legacyKey = CronSeedIdentity.LegacyAdoptionKeys(owner, stableId)[namespacedLegacyKey ? 0 : 1];
        var legacy = new FakeCronTicker
        {
            Id = Guid.NewGuid(), Function = fn, Expression = "*/5 * * * *", SeedKey = legacyKey,
            InitIdentifier = $"MemoryTicker_Seeded_{fn}", CreatedAt = _now, UpdatedAt = _now,
            Request = Array.Empty<byte>()
        };
        await _provider.InsertCronTickers([legacy], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            AdoptionManifest(owner, new DefinedCronTickerSeed(fn, "*/7 * * * *", stableDefinitionId: stableId)),
            CancellationToken.None);

        var row = Assert.Single(await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None));
        Assert.Equal(legacy.Id, row.Id);
        Assert.Equal(owner, row.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey(owner, stableId), row.SeedKey);
    }

    [Fact]
    public async Task CanonicallyEquivalentBareLegacyKey_WithDistinctStableId_IsAdoptedInPlace()
    {
        var canonicalFunction = Fn("job-\u00e0\u0315");
        var rawFunction = Fn("job-a\u0315\u0300");
        const string owner = "bare-key-owner";
        const string stableId = "distinct-stable-definition";
        var legacy = new FakeCronTicker
        {
            Id = Guid.NewGuid(),
            Function = canonicalFunction,
            Expression = "*/5 * * * *",
            SeedKey = rawFunction,
            InitIdentifier = $"MemoryTicker_Seeded_{canonicalFunction}",
            CreatedAt = _now,
            UpdatedAt = _now,
            Request = Array.Empty<byte>()
        };
        await _provider.InsertCronTickers([legacy], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            AdoptionManifest(owner, new DefinedCronTickerSeed(canonicalFunction, "*/7 * * * *",
                stableDefinitionId: stableId)), CancellationToken.None);

        var rows = await _provider.GetCronTickers(
            row => CronSeedIdentity.CanonicallyEquals(row.Function, canonicalFunction), CancellationToken.None);
        var adopted = Assert.Single(rows);
        Assert.Equal(legacy.Id, adopted.Id);
        Assert.True(adopted.IsEnabled);
        Assert.Equal(owner, adopted.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey(owner, stableId), adopted.SeedKey);
    }

    [Fact]
    public async Task AmbiguousLegacyCandidates_FailClosedWithoutMutation()
    {
        var fn = Fn("ambiguous");
        var rows = new[]
        {
            new FakeCronTicker { Id = Guid.NewGuid(), Function = fn, Expression = "*/5 * * * *", SeedKey = fn,
                InitIdentifier = $"MemoryTicker_Seeded_{fn}", CreatedAt = _now, UpdatedAt = _now, Request = Array.Empty<byte>() },
            new FakeCronTicker { Id = Guid.NewGuid(), Function = fn, Expression = "*/6 * * * *",
                InitIdentifier = $"MemoryTicker_Seeded_{fn}", CreatedAt = _now, UpdatedAt = _now, Request = Array.Empty<byte>() }
        };
        await _provider.InsertCronTickers(rows, CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.MigrateDefinedCronTickers(
            Manifest("app-a", new DefinedCronTickerSeed(fn, "*/7 * * * *")), CancellationToken.None));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
        var persisted = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        Assert.All(persisted, row => Assert.Null(row.SeedOwnerNamespace));
        Assert.DoesNotContain(persisted, row => row.SeedKey == CronSeedIdentity.SeedKey("app-a", fn));
    }

    [Fact]
    public async Task ConcurrentFirstLegacyAdoption_BySameNamespace_ConvergesInPlace()
    {
        var fn = Fn("concurrent-adoption");
        var legacy = new FakeCronTicker
        {
            Id = Guid.NewGuid(), Function = fn, Expression = "*/5 * * * *", SeedKey = fn,
            InitIdentifier = $"MemoryTicker_Seeded_{fn}", CreatedAt = _now, UpdatedAt = _now,
            Request = Array.Empty<byte>()
        };
        await _provider.InsertCronTickers([legacy], CancellationToken.None);
        var manifest = AdoptionManifest("app-a", new DefinedCronTickerSeed(fn, "*/7 * * * *"));

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _provider.MigrateDefinedCronTickers(manifest, CancellationToken.None))));

        var row = Assert.Single(await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None));
        Assert.Equal(legacy.Id, row.Id);
        Assert.Equal("app-a", row.SeedOwnerNamespace);
    }

    [Fact]
    public async Task NewSeededRow_StampsSeedKey_AndDeterministicId()
    {
        var fn = Fn("new");
        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        var row = Assert.Single(rows);
        var expectedKey = CronSeedIdentity.SeedKeyForFunction(fn);
        Assert.Equal(expectedKey, row.SeedKey);
        Assert.Equal(CronSeedIdentity.DeterministicId(expectedKey), row.Id);
    }

    [Fact]
    public async Task LegacySeededRow_AdoptsSeedKeyInPlace_WithoutChangingId()
    {
        var fn = Fn("legacy");
        var legacyId = Guid.NewGuid();
        var legacy = new FakeCronTicker
        {
            Id = legacyId,
            Function = fn,
            Expression = "*/5 * * * *",
            InitIdentifier = "MemoryTicker_Seeded_legacy",
            CreatedAt = _now.AddDays(-1),
            UpdatedAt = _now.AddDays(-1),
            Request = Array.Empty<byte>()
        };
        await _provider.InsertCronTickers([legacy], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/9 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal(legacyId, row.Id); // adopted in place — occurrences still resolve
        Assert.Equal(CronSeedIdentity.SeedKeyForFunction(fn), row.SeedKey);
        Assert.Equal("0 */9 * * * *", row.Expression);
    }

    [Fact]
    public async Task ConcurrentMigrations_ConvergeToSingleSeededRow()
    {
        var fn = Fn("race");
        var seed = new DefinedCronTickerSeed(fn, "*/5 * * * *", 1, null);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _provider.MigrateDefinedCronTickers([seed], CancellationToken.None)))
            .ToArray();
        await Task.WhenAll(tasks);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        Assert.Single(rows);
    }

    // Finding 1: a function already owns a keyed canonical row at a HIGHER id, plus a lower-id legacy
    // null-key duplicate. Canonical selection must prefer the already-keyed row (never move the key onto
    // the lower-id legacy row); the legacy duplicate is retired in place and stays null-keyed.
    [Fact]
    public async Task KeyedCanonicalWithLowerIdLegacyDuplicate_KeepsKeyedRowCanonical()
    {
        var fn = Fn("keyed-canon");
        var seedKey = CronSeedIdentity.SeedKeyForFunction(fn);

        var legacy = new FakeCronTicker
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"), // lower id, null SeedKey
            Function = fn, Expression = "*/5 * * * *",
            InitIdentifier = "MemoryTicker_Seeded_keyed-canon",
            CreatedAt = _now, UpdatedAt = _now, Request = Array.Empty<byte>()
        };
        var keyed = new FakeCronTicker
        {
            Id = new Guid("11111111-1111-1111-1111-111111111112"), // higher id, already keyed
            Function = fn, Expression = "*/5 * * * *", SeedKey = seedKey,
            InitIdentifier = "MemoryTicker_Seeded_keyed-canon",
            CreatedAt = _now, UpdatedAt = _now, Request = Array.Empty<byte>()
        };
        await _provider.InsertCronTickers([legacy, keyed], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        Assert.Equal(2, rows.Length);

        var canonical = Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.Equal(keyed.Id, canonical.Id);
        Assert.True(canonical.IsEnabled);
        Assert.Null(canonical.RetiredAt);

        var redundant = Assert.Single(rows, r => r.Id == legacy.Id);
        Assert.Null(redundant.SeedKey);
        Assert.False(redundant.IsEnabled);
        Assert.NotNull(redundant.RetiredAt);

        Assert.Single(rows, r => r.IsEnabled);
    }

    [Fact]
    public void CanonicalSelection_PrefersEnabledUnretiredLegacyRow_BeforeLowestIdFallback()
    {
        var disabledLowerId = new FakeCronTicker
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            IsEnabled = false
        };
        var enabledHigherId = new FakeCronTicker
        {
            Id = new Guid("11111111-1111-1111-1111-111111111112"),
            IsEnabled = true
        };

        var (canonical, duplicates) = CronSeedCanonical.Select(
            new[] { disabledLowerId, enabledHigherId }, "seed:missing");

        Assert.Same(enabledHigherId, canonical);
        Assert.Equal(disabledLowerId, Assert.Single(duplicates));
    }

    [Fact]
    public async Task DuplicateLegacySeededRows_ConvergeCanonical_WithoutDataLoss()
    {
        var fn = Fn("dup");
        var a = new FakeCronTicker
        {
            Id = Guid.NewGuid(), Function = fn, Expression = "*/5 * * * *",
            InitIdentifier = "MemoryTicker_Seeded_dup", CreatedAt = _now, UpdatedAt = _now, Request = Array.Empty<byte>()
        };
        var b = new FakeCronTicker
        {
            Id = Guid.NewGuid(), Function = fn, Expression = "*/5 * * * *",
            InitIdentifier = "MemoryTicker_Seeded_dup", CreatedAt = _now, UpdatedAt = _now, Request = Array.Empty<byte>()
        };
        await _provider.InsertCronTickers([a, b], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        Assert.Equal(2, rows.Length); // history preserved (retirement is Slice 3)
        var seedKey = CronSeedIdentity.SeedKeyForFunction(fn);
        Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.Single(rows, r => r.SeedKey == null);
    }
}
