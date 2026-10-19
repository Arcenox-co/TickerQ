using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Regressions for code-defined cron seeding carrying request-contract identity (typed-request-contracts).
/// Before the fix, <c>MigrateDefinedCronTickers</c> inserted/updated seeded crons without ever stamping
/// <see cref="TickerQ.Utilities.Entities.BaseEntity.BaseTickerEntity.RequestContractVersion"/>/
/// <c>RequestContractFingerprint</c>, so seeded crons silently bypassed execution-time drift enforcement.
/// These tests pin: new rows are stamped, existing seeded rows are reconciled in place, and genuinely
/// legacy (non-seeded) rows keep their own identity.
/// </summary>
public class DefinedCronTickerSeedingTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TestTickerQDbContext> _options = null!;
    private TestableProvider _provider = null!;
    private ITickerClock _clock = null!;
    private ITickerQRedisContext _redisContext = null!;
    private DateTime _now;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _now = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);

        _redisContext = Substitute.For<ITickerQRedisContext>();
        _redisContext.HasRedisConnection.Returns(false);

        _options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var seed = new TestTickerQDbContext(_options))
            await seed.Database.EnsureCreatedAsync();

        var schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = "seed-test-node" };
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        var serviceProvider = services.BuildServiceProvider();

        _provider = new TestableProvider(serviceProvider, _clock, schedulerOptions, _redisContext);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private TestTickerQDbContext Ctx() => new(_options);

    [Fact]
    public async Task NewSeededRow_StampsAuthoritativeContractIdentity()
    {
        var seed = new DefinedCronTickerSeed("Reqless", "*/5 * * * *", 4, "sha256:abc123");

        await _provider.MigrateDefinedCronTickers(new[] { seed }, CancellationToken.None);

        using var ctx = Ctx();
        var row = await ctx.Set<CronTickerEntity>().AsNoTracking().SingleAsync(c => c.Function == "Reqless");
        Assert.Equal(4, row.RequestContractVersion);
        Assert.Equal("sha256:abc123", row.RequestContractFingerprint);
        Assert.StartsWith("MemoryTicker_Seeded_", row.InitIdentifier);
    }

    [Fact]
    public async Task ExistingSeededRow_ReconcilesDriftedIdentity_InPlace()
    {
        var id = Guid.NewGuid();
        using (var ctx = Ctx())
        {
            ctx.Set<CronTickerEntity>().Add(new CronTickerEntity
            {
                Id = id,
                Function = "Optional",
                Expression = "*/5 * * * *",
                InitIdentifier = "MemoryTicker_Seeded_Optional",
                CreatedAt = _now.AddDays(-1),
                UpdatedAt = _now.AddDays(-1),
                Request = Array.Empty<byte>(),
                RequestContractVersion = null,        // legacy: identity was never stamped
                RequestContractFingerprint = null
            });
            await ctx.SaveChangesAsync();
        }

        // Register the function so seeding's orphan cleanup spares the existing seeded row.
        TickerFunctionProvider.ReplaceFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                ["Optional"] = ("*/5 * * * *", TickerTaskPriority.Normal, null!, 1)
            });
        try
        {
            var seed = new DefinedCronTickerSeed("Optional", "*/5 * * * *", 2, "sha256:optfp");
            await _provider.MigrateDefinedCronTickers(new[] { seed }, CancellationToken.None);
        }
        finally
        {
            TickerFunctionProvider.ReplaceFunctions(
                new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>());
        }

        using var verify = Ctx();
        var rows = await verify.Set<CronTickerEntity>().AsNoTracking()
            .Where(c => c.Function == "Optional").ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(id, row.Id);   // reconciled in place, not delete + reinsert
        Assert.Equal(2, row.RequestContractVersion);
        Assert.Equal("sha256:optfp", row.RequestContractFingerprint);
        Assert.Equal(_now, row.UpdatedAt);
    }

    [Fact]
    public async Task BlockedSeeds_RemovePriorAutoSeededRows_ButPreserveUserRows()
    {
        var seededId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using (var ctx = Ctx())
        {
            ctx.Set<CronTickerEntity>().AddRange(
                new CronTickerEntity
                {
                    Id = seededId,
                    Function = "RequiredSeed",
                    Expression = "*/5 * * * *",
                    InitIdentifier = "MemoryTicker_Seeded_RequiredSeed",
                    CreatedAt = _now,
                    UpdatedAt = _now,
                    Request = Array.Empty<byte>()
                },
                new CronTickerEntity
                {
                    Id = userId,
                    Function = "RequiredUser",
                    Expression = "*/5 * * * *",
                    InitIdentifier = "",
                    CreatedAt = _now,
                    UpdatedAt = _now,
                    Request = Array.Empty<byte>()
                });
            await ctx.SaveChangesAsync();
        }

        TickerFunctionProvider.ReplaceFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                ["RequiredSeed"] = ("*/5 * * * *", TickerTaskPriority.Normal, null!, 1),
                ["RequiredUser"] = ("*/5 * * * *", TickerTaskPriority.Normal, null!, 1)
            });
        try
        {
            await _provider.MigrateDefinedCronTickers(
                new[]
                {
                    new DefinedCronTickerSeed("RequiredSeed", "*/5 * * * *", 2, "sha256:req", canSeed: false),
                    new DefinedCronTickerSeed("RequiredUser", "*/5 * * * *", 2, "sha256:req", canSeed: false)
                },
                CancellationToken.None);
        }
        finally
        {
            TickerFunctionProvider.ReplaceFunctions(
                new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>());
        }

        using var verify = Ctx();
        // Slice 3: a blocked required-contract seed retires IMMEDIATELY but non-destructively — the row is
        // disabled and marked retired in place, never deleted. The user row is untouched.
        var seededRow = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync(c => c.Id == seededId);
        Assert.False(seededRow.IsEnabled);
        Assert.Equal(_now, seededRow.RetirementRequestedAt);
        Assert.Equal(_now, seededRow.RetiredAt);
        Assert.True(seededRow.SeedWasEnabledBeforeRetirement);

        var userRow = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync(c => c.Id == userId);
        Assert.True(userRow.IsEnabled);
        Assert.Null(userRow.RetiredAt);
    }

    [Fact]
    public async Task CronExpressionRemoved_ButFunctionStillRegistered_RetiresSeededRow()
    {
        // A code-defined cron whose expression was removed drops out of the desired seed manifest
        // even though the function itself stays registered (e.g. it is still invocable on demand).
        // Orphan detection must compare persisted seeded rows to the DESIRED SEED MANIFEST, not the
        // global runtime function registry — otherwise the stale seeded schedule keeps firing (#).
        var removedId = Guid.NewGuid();
        var keptId = Guid.NewGuid();
        using (var ctx = Ctx())
        {
            ctx.Set<CronTickerEntity>().AddRange(
                new CronTickerEntity
                {
                    Id = removedId,
                    Function = "Removed",
                    Expression = "*/5 * * * *",
                    InitIdentifier = "MemoryTicker_Seeded_Removed",
                    CreatedAt = _now.AddDays(-1),
                    UpdatedAt = _now.AddDays(-1),
                    Request = Array.Empty<byte>()
                },
                new CronTickerEntity
                {
                    Id = keptId,
                    Function = "Kept",
                    Expression = "*/5 * * * *",
                    InitIdentifier = "MemoryTicker_Seeded_Kept",
                    CreatedAt = _now.AddDays(-1),
                    UpdatedAt = _now.AddDays(-1),
                    Request = Array.Empty<byte>()
                });
            await ctx.SaveChangesAsync();
        }

        // Both functions remain registered in the runtime registry; only "Kept" still has a cron
        // expression, so only "Kept" appears in the desired seed manifest handed to the provider.
        TickerFunctionProvider.ReplaceFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                ["Removed"] = ("", TickerTaskPriority.Normal, null!, 1),
                ["Kept"] = ("*/5 * * * *", TickerTaskPriority.Normal, null!, 1)
            });
        try
        {
            await _provider.MigrateDefinedCronTickers(
                new[] { new DefinedCronTickerSeed("Kept", "*/5 * * * *", 1, null) },
                CancellationToken.None);
        }
        finally
        {
            TickerFunctionProvider.ReplaceFunctions(
                new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>());
        }

        using var verify = Ctx();
        // Slice 3: the row whose expression was removed enters the grace window on this first absent pass —
        // retirement is requested and it stays enabled, but it is NEVER deleted (non-destructive).
        var removedRow = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync(c => c.Id == removedId);
        Assert.Equal(_now, removedRow.RetirementRequestedAt);
        Assert.Null(removedRow.RetiredAt);
        Assert.True(removedRow.IsEnabled);
        Assert.True(await verify.Set<CronTickerEntity>().AnyAsync(c => c.Id == keptId),
            "The still-desired seeded row must remain active.");
    }

    [Fact]
    public async Task LegacyNonSeededRow_IdentityPreserved()
    {
        var id = Guid.NewGuid();
        using (var ctx = Ctx())
        {
            ctx.Set<CronTickerEntity>().Add(new CronTickerEntity
            {
                Id = id,
                Function = "DashboardMade",
                Expression = "*/5 * * * *",
                InitIdentifier = "",                  // not a code-seeded row
                CreatedAt = _now.AddDays(-1),
                UpdatedAt = _now.AddDays(-1),
                Request = Array.Empty<byte>(),
                RequestContractVersion = null,
                RequestContractFingerprint = null
            });
            await ctx.SaveChangesAsync();
        }

        var seed = new DefinedCronTickerSeed("DashboardMade", "*/5 * * * *", 9, "sha256:should-not-apply");
        await _provider.MigrateDefinedCronTickers(new[] { seed }, CancellationToken.None);

        using var verify = Ctx();
        var row = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Null(row.RequestContractVersion);
        Assert.Null(row.RequestContractFingerprint);
    }

    [Fact]
    public async Task UserRowSharingFunctionName_ExpressionAndIdentityUntouched_SeededRowInsertedSeparately()
    {
        // A user/dashboard-created cron with a NULL seed identity (InitIdentifier == "")
        // that happens to share a function name with a code seed — AND whose expression
        // differs from the seed. Reconciliation must NOT match or mutate this row: neither
        // its expression nor its (null) contract identity nor its UpdatedAt may change.
        var userId = Guid.NewGuid();
        var userCreatedAt = _now.AddDays(-3);
        using (var ctx = Ctx())
        {
            ctx.Set<CronTickerEntity>().Add(new CronTickerEntity
            {
                Id = userId,
                Function = "Shared",
                Expression = "0 0 * * *",             // deliberately different from the seed
                InitIdentifier = "",                  // user row: no seed identity
                CreatedAt = userCreatedAt,
                UpdatedAt = userCreatedAt,
                Request = Array.Empty<byte>(),
                RequestContractVersion = null,
                RequestContractFingerprint = null
            });
            await ctx.SaveChangesAsync();
        }

        TickerFunctionProvider.ReplaceFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                ["Shared"] = ("*/5 * * * *", TickerTaskPriority.Normal, null!, 1)
            });
        try
        {
            var seed = new DefinedCronTickerSeed("Shared", "*/5 * * * *", 9, "sha256:seed-only");
            await _provider.MigrateDefinedCronTickers(new[] { seed }, CancellationToken.None);
        }
        finally
        {
            TickerFunctionProvider.ReplaceFunctions(
                new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>());
        }

        using var verify = Ctx();

        // The user row is entirely untouched.
        var userRow = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync(c => c.Id == userId);
        Assert.Equal("0 0 * * *", userRow.Expression);
        Assert.Null(userRow.RequestContractVersion);
        Assert.Null(userRow.RequestContractFingerprint);
        Assert.Equal("", userRow.InitIdentifier);
        Assert.Equal(userCreatedAt, userRow.UpdatedAt);

        // The seed owns a separate, freshly inserted seeded row carrying its identity.
        var seededRow = await verify.Set<CronTickerEntity>().AsNoTracking()
            .SingleAsync(c => c.Function == "Shared" && c.Id != userId);
        Assert.Equal(CronExpression.Parse("*/5 * * * *").Value, seededRow.Expression);
        Assert.Equal(9, seededRow.RequestContractVersion);
        Assert.Equal("sha256:seed-only", seededRow.RequestContractFingerprint);
        Assert.StartsWith("MemoryTicker_Seeded_", seededRow.InitIdentifier);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DocumentedLegacySeedKeys_AreAdoptedInPlace(bool namespacedLegacyKey)
    {
        const string function = "EfDocumentedLegacyKey";
        const string owner = "ef-legacy-key-owner";
        const string stableId = "stable-definition";
        var id = Guid.NewGuid();
        var legacyKey = CronSeedIdentity.LegacyAdoptionKeys(owner, stableId)[namespacedLegacyKey ? 0 : 1];
        using (var ctx = Ctx())
        {
            ctx.Set<CronTickerEntity>().Add(new CronTickerEntity
            {
                Id = id, Function = function, Expression = "*/5 * * * *", SeedKey = legacyKey,
                InitIdentifier = $"MemoryTicker_Seeded_{function}", CreatedAt = _now, UpdatedAt = _now,
                Request = Array.Empty<byte>()
            });
            await ctx.SaveChangesAsync();
        }

        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest(owner,
                [new DefinedCronTickerSeed(function, "*/7 * * * *", stableDefinitionId: stableId)],
                new Dictionary<string, string>(StringComparer.Ordinal) { [function] = owner }),
            CancellationToken.None);

        using var verify = Ctx();
        var row = await verify.Set<CronTickerEntity>().AsNoTracking().SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal(owner, row.SeedOwnerNamespace);
        Assert.Equal(CronSeedIdentity.SeedKey(owner, stableId), row.SeedKey);
    }
}
