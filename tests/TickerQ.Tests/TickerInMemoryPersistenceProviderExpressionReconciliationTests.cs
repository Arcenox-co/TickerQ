using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
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
/// Slice 4 expression/contract reconciliation contract for the in-memory provider: when the canonical
/// code-owned row's Expression or request-contract identity changes — or the seed retires to Phase B —
/// only UNLEASED PENDING occurrences for that CronTicker are removed, and the removal clears the
/// process-wide <c>CronOccurrenceIndex</c> uniqueness entry so a fresh occurrence at the same
/// (CronTickerId, ExecutionTime) can be regenerated. Leased/owned Queued, InProgress, and terminal
/// occurrences always survive. Unique per-instance function prefixes isolate these cases from the
/// static in-memory store.
/// </summary>
public sealed class TickerInMemoryPersistenceProviderExpressionReconciliationTests : IAsyncLifetime
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly string _prefix = "expr-" + Guid.NewGuid().ToString("N") + "-";
    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;

    public TickerInMemoryPersistenceProviderExpressionReconciliationTests()
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
        var occ = await _provider.GetAllCronTickerOccurrences(_ => true, CancellationToken.None);
        var mineOcc = occ.Where(o => mine.Any(m => m.Id == o.CronTickerId)).Select(o => o.Id).ToArray();
        if (mineOcc.Length > 0)
            await _provider.RemoveCronTickerOccurrences(mineOcc, CancellationToken.None);
        if (mine.Length > 0)
            await _provider.RemoveCronTickers(mine.Select(c => c.Id).ToArray(), CancellationToken.None);
    }

    private string Fn(string name) => _prefix + name;

    private FakeCronTicker Seeded(string fn, string expression = "*/5 * * * *")
    {
        var cron = new FakeCronTicker
        {
            Id = Guid.NewGuid(),
            Function = fn,
            Expression = expression,
            InitIdentifier = $"MemoryTicker_Seeded_{fn}",
            SeedKey = CronSeedIdentity.SeedKeyForFunction(fn),
            CreatedAt = _now.AddDays(-1),
            UpdatedAt = _now.AddDays(-1),
            Request = Array.Empty<byte>(),
            IsEnabled = true,
        };
        return cron;
    }

    private CronTickerOccurrenceEntity<FakeCronTicker> Pending(Guid cronId, DateTime? time = null)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronId,
            ExecutionTime = time ?? _now.AddMinutes(5),
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now,
        };

    private CronTickerOccurrenceEntity<FakeCronTicker> Leased(Guid cronId, TickerStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronId,
            ExecutionTime = _now.AddMinutes(6),
            Status = status,
            LockHolder = "owner-node",
            LockedAt = _now,
            AcquisitionToken = Guid.NewGuid(),
            LeaseUntil = _now.AddMinutes(5),
            CreatedAt = _now,
            UpdatedAt = _now,
        };

    private CronTickerOccurrenceEntity<FakeCronTicker> Terminal(Guid cronId)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronId,
            ExecutionTime = _now.AddMinutes(-5),
            Status = TickerStatus.Done,
            ExecutedAt = _now.AddMinutes(-4),
            CreatedAt = _now.AddMinutes(-6),
            UpdatedAt = _now.AddMinutes(-4),
        };

    [Fact]
    public async Task ExpressionChange_QuarantinesUnleasedPending_AndClearsUniquenessIndex()
    {
        var fn = Fn("expr");
        var cron = Seeded(fn);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var executionTime = _now.AddMinutes(5);
        await _provider.InsertCronTickerOccurrences([Pending(cron.Id, executionTime)], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/9 * * * *", 1, null)], CancellationToken.None);

        var occ = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        var quarantined = Assert.Single(occ);
        Assert.Equal(TickerStatus.Skipped, quarantined.Status);

        // The uniqueness index entry for (CronTickerId, ExecutionTime) must be cleared so a regenerated
        // occurrence at the SAME slot inserts successfully while the quarantined evidence remains durable.
        var regenerated = Pending(cron.Id, executionTime);
        Assert.Equal(1, await _provider.InsertCronTickerOccurrences([regenerated], CancellationToken.None));
        var after = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(2, after.Length);
        Assert.Contains(after, x => x.Id == regenerated.Id && x.Status == TickerStatus.Idle);
        Assert.Contains(after, x => x.Id == quarantined.Id && x.Status == TickerStatus.Skipped);
    }

    [Fact]
    public async Task ExpressionChange_QuarantinesResultBackedPendingHistory_WithoutDeletingResult()
    {
        var fn = Fn("result-backed");
        var cron = Seeded(fn);
        var occurrence = Pending(cron.Id);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var results = _provider.CronOccurrenceResultsForTests;
        results[occurrence.Id] = new TickerResultEnvelope([1, 2, 3], 1, "application/json");

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/9 * * * *", 1, null)], CancellationToken.None);

        var persisted = Assert.Single(await _provider.GetAllCronTickerOccurrences(
            x => x.Id == occurrence.Id, CancellationToken.None));
        Assert.Equal(TickerStatus.Skipped, persisted.Status);
        Assert.True(results.ContainsKey(occurrence.Id));
    }

    [Fact]
    public async Task ContractOnlyChange_QuarantinesUnleasedPending()
    {
        var fn = Fn("contract");
        var cron = Seeded(fn);
        cron.RequestContractVersion = 1;
        cron.RequestContractFingerprint = "sha256:old";
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        await _provider.InsertCronTickerOccurrences([Pending(cron.Id)], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/5 * * * *", 2, "sha256:new")], CancellationToken.None);

        var occ = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Skipped, Assert.Single(occ).Status);
    }

    [Fact]
    public async Task PhaseAGrace_RetainsUnleasedPending()
    {
        var fn = Fn("grace-a");
        var cron = Seeded(fn);
        cron.RetirementRequestedAt = _now.AddHours(-2);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        await _provider.InsertCronTickerOccurrences([Pending(cron.Id)], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(Fn("other"), "*/5 * * * *", 1, null)], CancellationToken.None);

        var occ = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Single(occ);
    }

    [Fact]
    public async Task PhaseBRetirement_QuarantinesUnleasedPending()
    {
        var fn = Fn("grace-b");
        var cron = Seeded(fn);
        cron.RetirementRequestedAt = _now.AddHours(-25);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        await _provider.InsertCronTickerOccurrences([Pending(cron.Id)], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(Fn("other"), "*/5 * * * *", 1, null)], CancellationToken.None);

        var occ = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Skipped, Assert.Single(occ).Status);
    }

    [Fact]
    public async Task ExpressionChange_PreservesLeasedQueued_InProgress_AndTerminal()
    {
        var fn = Fn("preserve");
        var cron = Seeded(fn);
        await _provider.InsertCronTickers([cron], CancellationToken.None);

        var pending = Pending(cron.Id);
        var leasedQueued = Leased(cron.Id, TickerStatus.Queued);
        var inProgress = Leased(cron.Id, TickerStatus.InProgress);
        inProgress.ExecutionTime = _now.AddMinutes(7);
        var terminal = Terminal(cron.Id);
        await _provider.InsertCronTickerOccurrences(
            [pending, leasedQueued, inProgress, terminal], CancellationToken.None);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(fn, "*/9 * * * *", 1, null)], CancellationToken.None);

        var survivors = await _provider.GetAllCronTickerOccurrences(
            o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Skipped, survivors.Single(x => x.Id == pending.Id).Status);
        Assert.Equal(TickerStatus.Queued, survivors.Single(x => x.Id == leasedQueued.Id).Status);
        Assert.Equal(TickerStatus.InProgress, survivors.Single(x => x.Id == inProgress.Id).Status);
        Assert.Equal(TickerStatus.Done, survivors.Single(x => x.Id == terminal.Id).Status);
    }

    [Fact]
    public async Task RepeatedExpressionReconcile_IsIdempotent()
    {
        var fn = Fn("idem");
        var cron = Seeded(fn);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        await _provider.InsertCronTickerOccurrences([Pending(cron.Id)], CancellationToken.None);

        var reconcile = new DefinedCronTickerSeed(fn, "*/9 * * * *", 1, null);
        await _provider.MigrateDefinedCronTickers([reconcile], CancellationToken.None);
        await _provider.MigrateDefinedCronTickers([reconcile], CancellationToken.None);

        var row = Assert.Single(await _provider.GetCronTickers(c => c.Function == fn, CancellationToken.None));
        Assert.Equal("0 */9 * * * *", row.Expression);
        var occ = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Skipped, Assert.Single(occ).Status);
    }
}
