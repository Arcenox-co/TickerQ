using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfSavedDefinitionStoreTests
{
    [Fact]
    public async Task Definitions_are_persisted_and_use_versioned_activation_claims()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(connection).Options;
        await using (var db = new TestTickerQDbContext(options))
            await db.Database.EnsureCreatedAsync();

        using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options))
            .BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        var now = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);
        ITickerDefinitionStore store = new TestableProvider(
            services, clock, new SchedulerOptionsBuilder(), Substitute.For<ITickerQRedisContext>());

        var draft = await store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Draft,
            Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = "Month end",
            DefinitionJson = "{\"root\":{\"function\":\"Close.Month\"}}",
        });
        var token = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var claimed = await store.TryClaimDraftActivationAsync(
            draft.Id, draft.Version, token, rootId, now.AddSeconds(1));

        Assert.NotNull(claimed);
        Assert.Null(await store.TryClaimDraftActivationAsync(
            draft.Id, draft.Version, Guid.NewGuid(), Guid.NewGuid(), now.AddSeconds(1)));
        Assert.True(await store.CompleteDraftActivationAsync(
            draft.Id, token, rootId, now.AddSeconds(2)));
        var stored = await store.GetDefinitionAsync(draft.Id);
        Assert.Equal(rootId, stored.ActivatedRootTickerId);
        Assert.Equal(3, stored.Version);
    }

    [Fact]
    public async Task Template_name_key_is_unique_and_case_insensitive()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(connection).Options;
        await using (var db = new TestTickerQDbContext(options))
            await db.Database.EnsureCreatedAsync();

        using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options))
            .BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));
        ITickerDefinitionStore store = new TestableProvider(
            services, clock, new SchedulerOptionsBuilder(), Substitute.For<ITickerQRedisContext>());

        var first = await store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template,
            Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = "Invoice workflow",
            DefinitionJson = "{}",
        });

        await Assert.ThrowsAsync<DuplicateSavedTickerDefinitionNameException>(() =>
            store.CreateDefinitionAsync(new SavedTickerDefinition
            {
                Kind = SavedTickerDefinitionKind.Template,
                Target = SavedTickerDefinitionTarget.TimeTicker,
                Name = " invoice   WORKFLOW ",
                DefinitionJson = "{}",
            }));
        Assert.False(string.IsNullOrWhiteSpace(first.NameKey));
    }
}
