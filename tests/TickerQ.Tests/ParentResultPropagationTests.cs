using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Exceptions;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

// End-to-end parent-result propagation across the real execution handler, internal manager, and the
// in-memory persistence provider. Each test class instance gets a distinct closed generic provider
// type (via its own Fake entity types), so the provider's static stores are isolated per class.
[Collection("TickerCancellationTokenState")]
public sealed class ParentResultPropagationTests : IDisposable
{
    public sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public sealed class FakeCronTicker : CronTickerEntity { }

    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;
    private readonly IInternalTickerManager _manager;
    private readonly TickerExecutionTaskHandler _handler;
    private readonly List<Guid> _created = new();

    public ParentResultPropagationTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "result-test-node" };

        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(options);
        var sp = services.BuildServiceProvider();

        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(sp);

        var hub = Substitute.For<ITickerQNotificationHubSender>();
        hub.UpdateTimeTickerFromInternalFunctionContext<FakeTimeTicker>(Arg.Any<InternalFunctionContext>())
            .Returns(Task.CompletedTask);
        hub.UpdateCronOccurrenceFromInternalFunctionContext<FakeCronTicker>(Arg.Any<InternalFunctionContext>())
            .Returns(Task.CompletedTask);

        var managerType = typeof(IInternalTickerManager).Assembly
            .GetType("TickerQ.Utilities.Managers.InternalTickerManager`2")!
            .MakeGenericType(typeof(FakeTimeTicker), typeof(FakeCronTicker));
        _manager = (IInternalTickerManager)Activator.CreateInstance(
            managerType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { _provider, clock, hub, options },
            culture: null)!;

        var instrumentation = Substitute.For<ITickerQInstrumentation>();
        _handler = new TickerExecutionTaskHandler(
            sp, clock, instrumentation, _manager, options,
            Substitute.For<ITickerQFailureNotifier>(),
            descriptorResolver: _ => null);
    }

    public void Dispose()
    {
        if (_created.Count > 0)
            _provider.RemoveTimeTickers(_created.ToArray(), CancellationToken.None).GetAwaiter().GetResult();
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<Guid> AddOwnedTicker(Guid? parentId = null)
    {
        var id = Guid.NewGuid();
        _created.Add(id);
        await _provider.AddTimeTickers(new[]
        {
            new FakeTimeTicker
            {
                Id = id,
                Function = "Fn",
                Status = TickerStatus.Idle,
                ParentId = parentId,
                ExecutionTime = _now,
                RetryIntervals = Array.Empty<int>(),
            }
        }, CancellationToken.None);
        return id;
    }

    private async Task<InternalFunctionContext> BuildOwnedContext(
        Guid id, TickerFunctionDelegate del, Guid? parentId = null, int retries = 0, int[] retryIntervals = null)
    {
        // Acquire through the provider so the row is InProgress and owned by this provider's holder,
        // giving the root terminal write a matching acquisition token to win fencing.
        var acquired = await _provider.AcquireImmediateTimeTickersAsync(new[] { id }, CancellationToken.None);
        var token = acquired[0].AcquisitionToken;

        return new InternalFunctionContext
        {
            TickerId = id,
            FunctionName = "Fn",
            Type = TickerType.TimeTicker,
            ParentId = parentId,
            AcquisitionToken = token,
            ExecutionTime = _now,
            Retries = retries,
            RetryCount = 0,
            RetryIntervals = retryIntervals ?? Array.Empty<int>(),
            Status = TickerStatus.InProgress,
            CachedDelegate = del,
            TimeTickerChildren = new List<InternalFunctionContext>(),
        };
    }

    private Task Run(InternalFunctionContext ctx)
        => _handler.ExecuteRegisteredTaskAsync(ctx, isDue: false, registeredSource: null, CancellationToken.None);

    private static TickerFunctionDelegate PublishDelegate(ResultPayload payload)
        => (_, _, ctx) =>
        {
            ctx.SetResult(payload, ResultJsonContext.Default.ResultPayload);
            return Task.CompletedTask;
        };

    private ResultPayload ReadResult(Guid id)
    {
        var env = _provider.GetTimeTickerResultAsync(id).GetAwaiter().GetResult();
        return env is null ? null : JsonSerializer.Deserialize(env.Payload.Span, ResultJsonContext.Default.ResultPayload);
    }

    // ---- success visibility & ordering ---------------------------------------------------------

    [Fact]
    public async Task Success_Publishes_Result_Visible_After_TerminalWrite()
    {
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, PublishDelegate(new ResultPayload { Value = 42, Label = "done" }));

        await Run(ctx);

        Assert.Equal(TickerStatus.Done, ctx.Status);
        var result = ReadResult(id);
        Assert.NotNull(result);
        Assert.Equal(42, result.Value);
        Assert.Equal("done", result.Label);
    }

    [Fact]
    public async Task NoResult_Run_Leaves_ParentResult_Absent()
    {
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, (_, _, _) => Task.CompletedTask);

        await Run(ctx);

        Assert.Equal(TickerStatus.Done, ctx.Status);
        Assert.Null(await _provider.GetTimeTickerResultAsync(id));
    }

    [Fact]
    public async Task JsonNull_Result_Is_Present_And_Distinct_From_Absence()
    {
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, (_, _, c) =>
        {
            c.SetResult<ResultPayload>(null, ResultJsonContext.Default.ResultPayload);
            return Task.CompletedTask;
        });

        await Run(ctx);

        // A published JSON null IS a committed result envelope (absence would return null envelope).
        var env = await _provider.GetTimeTickerResultAsync(id);
        Assert.NotNull(env);
        Assert.Null(JsonSerializer.Deserialize(env.Payload.Span, ResultJsonContext.Default.ResultPayload));
    }

    // ---- child reads direct parent -------------------------------------------------------------

    [Fact]
    public async Task Child_Reads_DirectParent_CommittedResult()
    {
        var parentId = await AddOwnedTicker();
        var parentCtx = await BuildOwnedContext(parentId, PublishDelegate(new ResultPayload { Value = 100, Label = "parent" }));
        await Run(parentCtx);

        ResultPayload seenByChild = null;
        var childId = await AddOwnedTicker(parentId);
        var childCtx = await BuildOwnedContext(childId, (_, _, c) =>
        {
            Assert.True(c.HasParentResult);
            seenByChild = c.GetParentResult(ResultJsonContext.Default.ResultPayload);
            return Task.CompletedTask;
        }, parentId: parentId);

        await Run(childCtx);

        Assert.NotNull(seenByChild);
        Assert.Equal(100, seenByChild.Value);
        Assert.Equal("parent", seenByChild.Label);
    }

    [Fact]
    public async Task Root_Has_No_ParentResult()
    {
        var id = await AddOwnedTicker();
        var sawParent = true;
        var ctx = await BuildOwnedContext(id, (_, _, c) =>
        {
            sawParent = c.HasParentResult;
            return Task.CompletedTask;
        });

        await Run(ctx);

        Assert.False(sawParent);
    }

    [Fact]
    public async Task Deep_Descendant_Sees_Only_DirectParent_Result()
    {
        // grandparent -> parent -> child, each publishing a distinct result.
        var gpId = await AddOwnedTicker();
        await Run(await BuildOwnedContext(gpId, PublishDelegate(new ResultPayload { Value = 1, Label = "gp" })));

        var pId = await AddOwnedTicker(gpId);
        await Run(await BuildOwnedContext(pId, PublishDelegate(new ResultPayload { Value = 2, Label = "p" }), parentId: gpId));

        ResultPayload seen = null;
        var cId = await AddOwnedTicker(pId);
        await Run(await BuildOwnedContext(cId, (_, _, c) =>
        {
            seen = c.GetParentResult(ResultJsonContext.Default.ResultPayload);
            return Task.CompletedTask;
        }, parentId: pId));

        // The child sees its direct parent (p), never the grandparent.
        Assert.Equal(2, seen.Value);
        Assert.Equal("p", seen.Label);
    }

    // ---- failure / retry / cancel / skip never publish -----------------------------------------

    [Fact]
    public async Task FailedExecution_DoesNotPublish_Result()
    {
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, (_, _, c) =>
        {
            c.SetResult(new ResultPayload { Value = 7 }, ResultJsonContext.Default.ResultPayload);
            throw new InvalidOperationException("boom");
        });

        await Run(ctx);

        Assert.Equal(TickerStatus.Failed, ctx.Status);
        Assert.Null(await _provider.GetTimeTickerResultAsync(id));
    }

    [Fact]
    public async Task Retry_Publishes_Only_The_Successful_Attempt_Result()
    {
        var attempt = 0;
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, (_, _, c) =>
        {
            if (attempt++ == 0)
            {
                c.SetResult(new ResultPayload { Value = -1, Label = "attempt-0" }, ResultJsonContext.Default.ResultPayload);
                throw new InvalidOperationException("first attempt fails");
            }
            c.SetResult(new ResultPayload { Value = 2, Label = "attempt-1" }, ResultJsonContext.Default.ResultPayload);
            return Task.CompletedTask;
        }, retries: 1, retryIntervals: new[] { 0 });

        await Run(ctx);

        Assert.Equal(TickerStatus.Done, ctx.Status);
        var result = ReadResult(id);
        Assert.Equal(2, result.Value);
        Assert.Equal("attempt-1", result.Label);
    }

    [Fact]
    public async Task Retry_That_Succeeds_Without_Publishing_Leaves_No_Result()
    {
        var attempt = 0;
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, (_, _, c) =>
        {
            if (attempt++ == 0)
            {
                c.SetResult(new ResultPayload { Value = 99, Label = "stale" }, ResultJsonContext.Default.ResultPayload);
                throw new InvalidOperationException("first attempt fails");
            }
            return Task.CompletedTask; // succeeds but publishes nothing
        }, retries: 1, retryIntervals: new[] { 0 });

        await Run(ctx);

        Assert.Equal(TickerStatus.Done, ctx.Status);
        // The failed attempt's result must never leak into the final terminal state.
        Assert.Null(await _provider.GetTimeTickerResultAsync(id));
    }

    [Fact]
    public async Task Cancelled_DoesNotPublish_Result()
    {
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, (_, _, c) =>
        {
            c.SetResult(new ResultPayload { Value = 7 }, ResultJsonContext.Default.ResultPayload);
            throw new TaskCanceledException();
        });

        await Run(ctx);

        Assert.Equal(TickerStatus.Cancelled, ctx.Status);
        Assert.Null(await _provider.GetTimeTickerResultAsync(id));
    }

    [Fact]
    public async Task Skipped_DoesNotPublish_Result()
    {
        var id = await AddOwnedTicker();
        var ctx = await BuildOwnedContext(id, (_, _, c) =>
        {
            c.SetResult(new ResultPayload { Value = 7 }, ResultJsonContext.Default.ResultPayload);
            throw new TerminateExecutionException("skip me");
        });

        await Run(ctx);

        Assert.Equal(TickerStatus.Skipped, ctx.Status);
        Assert.Null(await _provider.GetTimeTickerResultAsync(id));
    }

    // ---- ownership-fenced acknowledgement ------------------------------------------------------

    [Fact]
    public async Task StaleToken_Result_Write_Is_Not_Acknowledged_And_Throws()
    {
        var id = await AddOwnedTicker();
        // Acquire (mints the live generation) but drive the terminal write under a WRONG token.
        await _provider.AcquireImmediateTimeTickersAsync(new[] { id }, CancellationToken.None);

        var ctx = new InternalFunctionContext
        {
            TickerId = id,
            FunctionName = "Fn",
            Type = TickerType.TimeTicker,
            ParentId = null,
            AcquisitionToken = Guid.NewGuid(), // stale: not the live generation
            ResultEnvelope = null,
            RetryIntervals = Array.Empty<int>(),
        };
        ctx.SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ResultEnvelope,
                new TickerResultEnvelope(
                    JsonSerializer.SerializeToUtf8Bytes(new ResultPayload { Value = 1 }, ResultJsonContext.Default.ResultPayload),
                    TickerResultEnvelope.CurrentVersion, "application/json"));

        await Assert.ThrowsAsync<TickerResultNotAcknowledgedException>(
            () => _manager.UpdateTickerAsync(ctx, CancellationToken.None));

        // Nothing durable published under the stale write.
        Assert.Null(await _provider.GetTimeTickerResultAsync(id));
    }

    [Fact]
    public async Task StaleToken_Result_Write_Does_Not_Release_Deferred_Children()
    {
        var parentId = await AddOwnedTicker();
        // Mint the live generation, then run the parent under a stale token so its result write is fenced.
        await _provider.AcquireImmediateTimeTickersAsync(new[] { parentId }, CancellationToken.None);

        var childRan = false;
        var childId = await AddOwnedTicker(parentId);
        var child = new InternalFunctionContext
        {
            TickerId = childId,
            FunctionName = "Fn",
            Type = TickerType.TimeTicker,
            ParentId = parentId,
            RunCondition = RunCondition.OnSuccess, // deferred: runs only after parent success is released
            ExecutionTime = _now,
            RetryIntervals = Array.Empty<int>(),
            Status = TickerStatus.InProgress,
            CachedDelegate = (_, _, _) => { childRan = true; return Task.CompletedTask; },
            TimeTickerChildren = new List<InternalFunctionContext>(),
        };

        var parent = new InternalFunctionContext
        {
            TickerId = parentId,
            FunctionName = "Fn",
            Type = TickerType.TimeTicker,
            ParentId = null,
            AcquisitionToken = Guid.NewGuid(), // stale
            ExecutionTime = _now,
            RetryIntervals = Array.Empty<int>(),
            Status = TickerStatus.InProgress,
            CachedDelegate = PublishDelegate(new ResultPayload { Value = 7, Label = "unacked" }),
            TimeTickerChildren = new List<InternalFunctionContext> { child },
        };

        await Assert.ThrowsAsync<TickerResultNotAcknowledgedException>(() => Run(parent));

        Assert.False(childRan); // deferred child must NOT be released when the result write was not acknowledged
        Assert.Null(await _provider.GetTimeTickerResultAsync(parentId));
    }

    // ---- immutable bytes end-to-end ------------------------------------------------------------

    [Fact]
    public async Task Committed_Result_Bytes_Are_Immutable_Across_Reads()
    {
        var id = await AddOwnedTicker();
        await Run(await BuildOwnedContext(id, PublishDelegate(new ResultPayload { Value = 5, Label = "immutable" })));

        var env = await _provider.GetTimeTickerResultAsync(id);
        var bytes = env.ToPayloadArray();
        for (var i = 0; i < bytes.Length; i++) bytes[i] = 0; // attempt to corrupt

        var again = ReadResult(id);
        Assert.Equal(5, again.Value);
        Assert.Equal("immutable", again.Label);
    }
}
