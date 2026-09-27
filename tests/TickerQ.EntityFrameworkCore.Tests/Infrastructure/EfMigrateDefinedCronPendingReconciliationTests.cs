using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

using Xunit;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Slice 4: EF Core reconciles the cron DEFINITION change and the removal of its unleased pending
/// occurrences (plus any orphan result rows) in ONE <c>SaveChangesAsync</c> — a single transaction. The
/// happy path proves they commit together; the injected-fault path proves a failing save rolls BOTH the
/// definition update and the pending deletion back atomically and leaves the cache untouched. Terminal
/// occurrence history and its result row are always preserved.
/// </summary>
public sealed class EfMigrateDefinedCronPendingReconciliationTests : IAsyncLifetime
{
    private const string CronExpressionsCacheKey = "cron:expressions";

    private SqliteConnection _connection = null!;
    private TestableProvider _provider = null!;
    private IDistributedCache _distributedCache = null!;
    private DbContextOptions<TestTickerQDbContext> _dbOptions = null!;
    private readonly ThrowingSaveInterceptor _fault = new();
    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
                throw new InvalidOperationException("Injected reconcile fault.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);

        _distributedCache = Substitute.For<IDistributedCache>();
        var redisContext = Substitute.For<ITickerQRedisContext>();
        redisContext.HasRedisConnection.Returns(true);
        redisContext.DistributedCache.Returns(_distributedCache);

        _dbOptions = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_fault)
            .Options;

        await using (var seedContext = new TestTickerQDbContext(_dbOptions))
            await seedContext.Database.EnsureCreatedAsync();

        var options = new SchedulerOptionsBuilder { NodeIdentifier = "pending-reconcile-node" };

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_dbOptions));
        _provider = new TestableProvider(services.BuildServiceProvider(), clock, options, redisContext);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private CronTickerEntity SeededRow(string function, string expression)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = expression,
            SeedKey = CronSeedIdentity.SeedKeyForFunction(function),
            InitIdentifier = $"MemoryTicker_Seeded_{function}",
            Request = Array.Empty<byte>(),
            CreatedAt = _now.AddDays(-1),
            UpdatedAt = _now.AddDays(-1),
            IsEnabled = true,
        };

    private CronTickerOccurrenceEntity<CronTickerEntity> Occurrence(Guid cronId, TickerStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronId,
            ExecutionTime = status == TickerStatus.Done ? _now.AddMinutes(-5) : _now.AddMinutes(5),
            Status = status,
            ExecutedAt = status == TickerStatus.Done ? _now.AddMinutes(-4) : null,
            CreatedAt = _now,
            UpdatedAt = _now,
        };

    private async Task SeedResultAsync(Guid occurrenceId)
    {
        await using var ctx = new TestTickerQDbContext(_dbOptions);
        ctx.Set<CronTickerOccurrenceResultEntity<CronTickerEntity>>().Add(new()
        {
            TickerId = occurrenceId,
            Payload = [1, 2, 3],
            EnvelopeVersion = 1,
            MediaType = "application/json"
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ExpressionChange_PreservesResultBackedPendingHistory_AndTerminalHistory()
    {
        var cron = SeededRow("commit-fn", "*/5 * * * *");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var pending = Occurrence(cron.Id, TickerStatus.Idle);
        var terminal = Occurrence(cron.Id, TickerStatus.Done);
        await _provider.InsertCronTickerOccurrences([pending, terminal], CancellationToken.None);
        await SeedResultAsync(pending.Id);   // durable history evidence despite inconsistent pending status
        await SeedResultAsync(terminal.Id);  // legitimate terminal history result

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("commit-fn", "*/9 * * * *", 1, null)], CancellationToken.None);

        await using var verify = new TestTickerQDbContext(_dbOptions);
        var row = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == cron.Id);
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, row.Expression); // definition updated

        var occurrences = await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().Where(x => x.CronTickerId == cron.Id).ToListAsync();
        var quarantined = Assert.Single(occurrences, x => x.Id == pending.Id);
        Assert.Equal(TickerStatus.Skipped, quarantined.Status); // durable history retained but non-runnable
        Assert.Contains(occurrences, x => x.Id == terminal.Id); // terminal preserved

        var results = verify.Set<CronTickerOccurrenceResultEntity<CronTickerEntity>>().AsNoTracking();
        Assert.True(await results.AnyAsync(x => x.TickerId == pending.Id));   // durable history preserved
        Assert.True(await results.AnyAsync(x => x.TickerId == terminal.Id));  // terminal result preserved
    }

    [Fact]
    public async Task InjectedSaveFault_RollsBackDefinitionAndPendingDeletion_Together_AndLeavesCacheIntact()
    {
        var cron = SeededRow("rollback-fn", "*/5 * * * *");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var pending = Occurrence(cron.Id, TickerStatus.Idle);
        await _provider.InsertCronTickerOccurrences([pending], CancellationToken.None);
        await SeedResultAsync(pending.Id);

        _distributedCache.ClearReceivedCalls();
        _fault.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.MigrateDefinedCronTickers(
                [new DefinedCronTickerSeed("rollback-fn", "*/9 * * * *", 1, null)], CancellationToken.None));
        _fault.Armed = false;

        await using var verify = new TestTickerQDbContext(_dbOptions);
        var row = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == cron.Id);
        Assert.Equal("*/5 * * * *", row.Expression); // definition rolled back

        var occurrences = await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().Where(x => x.CronTickerId == cron.Id).Select(x => x.Id).ToListAsync();
        Assert.Contains(pending.Id, occurrences); // pending deletion rolled back
        Assert.True(await verify.Set<CronTickerOccurrenceResultEntity<CronTickerEntity>>()
            .AsNoTracking().AnyAsync(x => x.TickerId == pending.Id));

        await _distributedCache.DidNotReceive().RemoveAsync(CronExpressionsCacheKey, Arg.Any<CancellationToken>());
    }
}
