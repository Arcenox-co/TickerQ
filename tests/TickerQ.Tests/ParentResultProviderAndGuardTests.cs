using System;
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
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public sealed class ParentResultProviderAndGuardTests
{
    public sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public sealed class FakeCronTicker : CronTickerEntity { }

    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;

    public ParentResultProviderAndGuardTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(new SchedulerOptionsBuilder { NodeIdentifier = "guard-node" });
        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(
            services.BuildServiceProvider());
    }

    private static TickerResultEnvelope Envelope(string tag)
        => new(System.Text.Encoding.UTF8.GetBytes($"\"{tag}\""), TickerResultEnvelope.CurrentVersion, "application/json");

    [Fact]
    public async Task StaleToken_TerminalWrite_Neither_Stores_Result_Nor_Changes_Status()
    {
        var id = Guid.NewGuid();
        await _provider.AddTimeTickers(new[]
        {
            new FakeTimeTicker { Id = id, Function = "Fn", Status = TickerStatus.Idle, ExecutionTime = _now }
        }, CancellationToken.None);

        // Owned InProgress under a specific acquisition generation.
        _ = await _provider.AcquireImmediateTimeTickersAsync(new[] { id }, CancellationToken.None);

        // A terminal success write arriving under a STALE (wrong) acquisition token must be fenced out.
        var stale = new InternalFunctionContext
        {
            TickerId = id,
            Type = TickerType.TimeTicker,
            ParentId = null,
            AcquisitionToken = Guid.NewGuid(), // not the live generation
            ResultEnvelope = Envelope("stale-winner"),
        };
        stale.SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ResultEnvelope, stale.ResultEnvelope);

        var affected = await _provider.UpdateTimeTicker(stale, CancellationToken.None);

        Assert.Equal(0, affected);
        Assert.Null(await _provider.GetTimeTickerResultAsync(id));

        var row = await _provider.GetTimeTickerById(id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, row.Status);

        await _provider.RemoveTimeTickers(new[] { id }, CancellationToken.None);
    }

    [Fact]
    public async Task CronOccurrence_Success_Stores_And_Reads_Result()
    {
        // Build an owned InProgress occurrence, then commit a successful terminal result on it.
        var cronId = Guid.NewGuid();
        await _provider.InsertCronTickers(new[]
        {
            new FakeCronTicker { Id = cronId, Function = "Fn", Expression = "* * * * *", Request = Array.Empty<byte>() }
        }, CancellationToken.None);

        var occId = Guid.NewGuid();
        await _provider.InsertCronTickerOccurrences(new[]
        {
            new CronTickerOccurrenceEntity<FakeCronTicker>
            {
                Id = occId,
                CronTickerId = cronId,
                Status = TickerStatus.Idle,
                ExecutionTime = _now,
            }
        }, CancellationToken.None);

        // Own the occurrence so the terminal write wins fencing under a live acquisition token.
        var acquired = await _provider.AcquireImmediateCronOccurrencesAsync(new[] { occId }, CancellationToken.None);
        var token = acquired[0].AcquisitionToken;

        var ctx = new InternalFunctionContext
        {
            TickerId = occId,
            Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = token,
            ResultEnvelope = Envelope("cron-result"),
        };
        ctx.SetProperty(x => x.Status, TickerStatus.DueDone)
            .SetProperty(x => x.ResultEnvelope, ctx.ResultEnvelope);

        await _provider.UpdateCronTickerOccurrence(ctx, CancellationToken.None);

        var env = await _provider.GetCronTickerOccurrenceResultAsync(occId);
        Assert.NotNull(env);
        Assert.Equal("\"cron-result\"", System.Text.Encoding.UTF8.GetString(env.ToPayloadArray()));

        await _provider.RemoveCronTickerOccurrences(new[] { occId }, CancellationToken.None);
    }

    [Fact]
    public async Task Manager_Throws_When_Publishing_Against_Provider_Without_Result_Support()
    {
        // A substitute provider inherits SupportsResultPublication == false (never overridden).
        var provider = Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var hub = Substitute.For<ITickerQNotificationHubSender>();

        var managerType = typeof(IInternalTickerManager).Assembly
            .GetType("TickerQ.Utilities.Managers.InternalTickerManager`2")!
            .MakeGenericType(typeof(FakeTimeTicker), typeof(FakeCronTicker));
        var manager = (IInternalTickerManager)Activator.CreateInstance(
            managerType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { provider, clock, hub, new SchedulerOptionsBuilder() },
            culture: null)!;

        var ctx = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Fn",
            Type = TickerType.TimeTicker,
            ResultEnvelope = Envelope("dropped"),
        };
        ctx.SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ResultEnvelope, ctx.ResultEnvelope);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => manager.UpdateTickerAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Manager_Does_Not_Throw_For_Terminal_Write_Without_A_Result()
    {
        // Same unsupported provider, but no result published — the ordinary terminal write must pass.
        var provider = Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>();
        provider.UpdateTimeTicker(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var hub = Substitute.For<ITickerQNotificationHubSender>();
        hub.UpdateTimeTickerFromInternalFunctionContext<FakeTimeTicker>(Arg.Any<InternalFunctionContext>())
            .Returns(Task.CompletedTask);

        var managerType = typeof(IInternalTickerManager).Assembly
            .GetType("TickerQ.Utilities.Managers.InternalTickerManager`2")!
            .MakeGenericType(typeof(FakeTimeTicker), typeof(FakeCronTicker));
        var manager = (IInternalTickerManager)Activator.CreateInstance(
            managerType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { provider, clock, hub, new SchedulerOptionsBuilder() },
            culture: null)!;

        var ctx = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Fn",
            Type = TickerType.TimeTicker,
        };
        ctx.SetProperty(x => x.Status, TickerStatus.Done);

        await manager.UpdateTickerAsync(ctx, CancellationToken.None); // must not throw
    }
}
