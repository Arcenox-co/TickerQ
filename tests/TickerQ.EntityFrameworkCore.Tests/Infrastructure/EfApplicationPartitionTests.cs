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

public sealed class EfApplicationPartitionTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TestTickerQDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        await using var context = new TestTickerQDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task IdenticalIds_AreIsolatedAcrossWriteReadAcquireAndDelete()
    {
        var id = Guid.NewGuid();
        var providerA = CreateProvider("ef-app-a");
        var providerB = CreateProvider("ef-app-b");
        await providerA.AddTimeTickers([Ticker(id, "function-a")], CancellationToken.None);
        await providerB.AddTimeTickers([Ticker(id, "function-b")], CancellationToken.None);

        Assert.Equal("function-a", (await providerA.GetTimeTickerById(id)).Function);
        Assert.Equal("function-b", (await providerB.GetTimeTickerById(id)).Function);
        Assert.Single(await providerA.AcquireImmediateTimeTickersAsync([id], CancellationToken.None));
        Assert.Single(await providerB.AcquireImmediateTimeTickersAsync([id], CancellationToken.None));
        Assert.Equal(1, await providerA.RemoveTimeTickers([id], CancellationToken.None));
        Assert.Null(await providerA.GetTimeTickerById(id));
        Assert.NotNull(await providerB.GetTimeTickerById(id));
    }

    [Fact]
    public async Task ExplicitLegacyAdoption_IsAtomicIdempotentAndOwnerFenced()
    {
        var id = Guid.NewGuid();
        var legacy = CreateProvider(null);
        var target = CreateProvider("ef-adopter");
        await legacy.AddTimeTickers([Ticker(id, "legacy")], CancellationToken.None);
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("ef-adopter"), 31);

        target.AfterLegacyAdoptionLeaseForTestAsync = _ =>
            throw new OperationCanceledException("deterministic interruption");
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            target.AdoptLegacyRuntimePartitionAsync(adoption));
        target.AfterLegacyAdoptionLeaseForTestAsync = null;
        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        await target.AdoptLegacyRuntimePartitionAsync(adoption);

        Assert.Equal("legacy", (await target.GetTimeTickerById(id))!.Function);
        Assert.Null(await legacy.GetTimeTickerById(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateProvider("ef-other").AdoptLegacyRuntimePartitionAsync(
                new LegacyRuntimePartitionAdoption(new TickerQRuntimePartition("ef-other"), 31)));
    }

    private TestableProvider CreateProvider(string? applicationNamespace)
    {
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope(applicationNamespace, 0, schedulerEnabled: false);
        var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(_options))
            .BuildServiceProvider();
        return new TestableProvider(services, Substitute.For<ITickerClock>(), scheduler,
            Substitute.For<ITickerQRedisContext>());
    }

    private static TimeTickerEntity Ticker(Guid id, string function) => new()
    {
        Id = id, Function = function, Status = TickerStatus.Idle,
        ExecutionTime = DateTime.UtcNow.AddMinutes(1), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
}
