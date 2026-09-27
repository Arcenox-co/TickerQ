using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Slice 3 two-phase non-destructive retirement contract for the in-memory provider. Scenarios are
/// driven at a fixed clock by pre-setting the retirement markers a prior pass would have written; the
/// grace window is the 24h default. Unique per-instance function prefixes isolate these cases from the
/// process-wide static in-memory store.
/// </summary>
public sealed class TickerInMemoryPersistenceProviderRetirementTests : IAsyncLifetime
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly string _prefix = "retire-" + Guid.NewGuid().ToString("N") + "-";
    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;

    public TickerInMemoryPersistenceProviderRetirementTests()
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
        var occ = await _provider.GetAllCronTickerOccurrences(_ => true, CancellationToken.None);
        var mineOcc = occ.Where(o => mine.Any(m => m.Id == o.CronTickerId)).Select(o => o.Id).ToArray();
        if (mineOcc.Length > 0)
            await _provider.RemoveCronTickerOccurrences(mineOcc, CancellationToken.None);
    }

    private string Fn(string name) => _prefix + name;

    private FakeCronTicker Seeded(string fn) => new()
    {
        Id = Guid.NewGuid(),
        Function = fn,
        Expression = "*/5 * * * *",
        InitIdentifier = $"MemoryTicker_Seeded_{fn}",
        CreatedAt = _now.AddDays(-1),
        UpdatedAt = _now.AddDays(-1),
        Request = Array.Empty<byte>(),
        IsEnabled = true,
    };

    private async Task<CronTickerEntity> Single(string fn)
        => Assert.Single(await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None));

    [Fact]
    public async Task RemovedSeed_EntersGraceWithoutDelete_AndKeepsOccurrence()
    {
        var fn = Fn("grace-enter");
        var row = Seeded(fn);
        await _provider.InsertCronTickers([row], CancellationToken.None);
        await _provider.InsertCronTickerOccurrences(
            [new CronTickerOccurrenceEntity<FakeCronTicker> { Id = Guid.NewGuid(), CronTickerId = row.Id, ExecutionTime = _now, Status = TickerStatus.Idle, CreatedAt = _now, UpdatedAt = _now }],
            CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(Fn("other"), "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = await Single(fn);
        Assert.Equal(_now, persisted.RetirementRequestedAt);
        Assert.Null(persisted.RetiredAt);
        Assert.True(persisted.IsEnabled);
        var occ = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == row.Id, CancellationToken.None);
        Assert.Single(occ);
    }

    [Fact]
    public async Task GraceExpired_DisablesAndRetires_WithoutDelete()
    {
        var fn = Fn("grace-expired");
        var row = Seeded(fn);
        row.RetirementRequestedAt = _now.AddHours(-25);
        await _provider.InsertCronTickers([row], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(Fn("other"), "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = await Single(fn);
        Assert.False(persisted.IsEnabled);
        Assert.Equal(_now, persisted.RetiredAt);
        Assert.Equal(_now.AddHours(-25), persisted.RetirementRequestedAt);
        Assert.True(persisted.SeedWasEnabledBeforeRetirement);
    }

    [Fact]
    public async Task RetirementTimestamp_StableAcrossRepeatedAbsentPasses()
    {
        var fn = Fn("stable");
        var row = Seeded(fn);
        row.RetirementRequestedAt = _now.AddHours(-2);
        await _provider.InsertCronTickers([row], CancellationToken.None);

        var absent = new[] { new DefinedCronTickerSeed(Fn("other"), "*/5 * * * *", 1, null) };
        await _provider.MigrateDefinedCronTickers(absent, CancellationToken.None);
        await _provider.MigrateDefinedCronTickers(absent, CancellationToken.None);

        var persisted = await Single(fn);
        Assert.Equal(_now.AddHours(-2), persisted.RetirementRequestedAt);
        Assert.Null(persisted.RetiredAt);
        Assert.True(persisted.IsEnabled);
    }

    [Fact]
    public async Task ReappearanceBeforeGrace_CancelsRetirement()
    {
        var fn = Fn("reappear-early");
        var row = Seeded(fn);
        row.SeedKey = CronSeedIdentity.SeedKeyForFunction(fn);
        row.RetirementRequestedAt = _now.AddHours(-2);
        await _provider.InsertCronTickers([row], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/9 * * * *", 1, null)], CancellationToken.None);

        var persisted = await Single(fn);
        Assert.Null(persisted.RetirementRequestedAt);
        Assert.Null(persisted.RetiredAt);
        Assert.True(persisted.IsEnabled);
        Assert.Equal("0 */9 * * * *", persisted.Expression);
    }

    [Fact]
    public async Task ReappearanceAfterRetirement_RestoresFrameworkDisabled_PreservesUserDisabled()
    {
        var restoreFn = Fn("restore");
        var restore = Seeded(restoreFn);
        restore.SeedKey = CronSeedIdentity.SeedKeyForFunction(restoreFn);
        restore.RetirementRequestedAt = _now.AddHours(-25);
        restore.RetiredAt = _now.AddHours(-25);
        restore.IsEnabled = false;
        restore.SeedWasEnabledBeforeRetirement = true;

        var keepFn = Fn("keep-disabled");
        var keep = Seeded(keepFn);
        keep.SeedKey = CronSeedIdentity.SeedKeyForFunction(keepFn);
        keep.RetirementRequestedAt = _now.AddHours(-25);
        keep.RetiredAt = _now.AddHours(-25);
        keep.IsEnabled = false;
        keep.SeedWasEnabledBeforeRetirement = false;

        await _provider.InsertCronTickers([restore, keep], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
        [
            new DefinedCronTickerSeed(restoreFn, "*/5 * * * *", 1, null),
            new DefinedCronTickerSeed(keepFn, "*/5 * * * *", 1, null)
        ], CancellationToken.None);

        var restored = await Single(restoreFn);
        Assert.True(restored.IsEnabled);
        Assert.Null(restored.RetiredAt);
        Assert.Null(restored.SeedWasEnabledBeforeRetirement);

        var kept = await Single(keepFn);
        Assert.False(kept.IsEnabled);
        Assert.Null(kept.RetiredAt);
        Assert.Null(kept.SeedWasEnabledBeforeRetirement);
    }

    [Fact]
    public async Task BlockedSeed_RetiresImmediately_WithoutDelete()
    {
        var fn = Fn("blocked");
        var row = Seeded(fn);
        await _provider.InsertCronTickers([row], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/5 * * * *", 2, "sha256:req", canSeed: false)], CancellationToken.None);

        var persisted = await Single(fn);
        Assert.False(persisted.IsEnabled);
        Assert.Equal(_now, persisted.RetirementRequestedAt);
        Assert.Equal(_now, persisted.RetiredAt);
        Assert.True(persisted.SeedWasEnabledBeforeRetirement);
    }

    [Fact]
    public async Task DuplicateLegacyRows_LeaveExactlyOneEnabled_PreserveAll()
    {
        var fn = Fn("dup");
        var a = Seeded(fn);
        var b = Seeded(fn);
        await _provider.InsertCronTickers([a, b], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None);
        Assert.Equal(2, rows.Length);
        var seedKey = CronSeedIdentity.SeedKeyForFunction(fn);
        var canonical = Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.True(canonical.IsEnabled);
        var redundant = Assert.Single(rows, r => r.SeedKey == null);
        Assert.False(redundant.IsEnabled);
        Assert.NotNull(redundant.RetiredAt);
    }

    [Fact]
    public async Task UserDashboardRow_Untouched()
    {
        var fn = Fn("user");
        var user = new FakeCronTicker
        {
            Id = Guid.NewGuid(),
            Function = fn,
            Expression = "0 0 * * *",
            InitIdentifier = string.Empty,
            CreatedAt = _now.AddDays(-1),
            UpdatedAt = _now.AddDays(-1),
            Request = Array.Empty<byte>(),
            IsEnabled = true,
        };
        await _provider.InsertCronTickers([user], CancellationToken.None);

        // Even when the function is absent from the manifest, a user row is never retired.
        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(Fn("other"), "*/5 * * * *", 1, null)], CancellationToken.None);

        var persisted = await Single(fn);
        Assert.Null(persisted.SeedKey);
        Assert.Null(persisted.RetirementRequestedAt);
        Assert.Null(persisted.RetiredAt);
        Assert.True(persisted.IsEnabled);
        Assert.Equal("0 0 * * *", persisted.Expression);
    }
}
