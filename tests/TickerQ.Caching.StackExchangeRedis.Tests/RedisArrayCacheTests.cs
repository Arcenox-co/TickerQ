using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.Tests;

public sealed class RedisArrayCacheTests
{
    private const string Key = "cron:expressions:test-partition";
    private static CronTickerEntity[] Rows(string function) => [new() { Function = function, Expression = "* * * * *" }];

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness
    {
        public MemoryDistributedCache Storage { get; } = new(Options.Create(new MemoryDistributedCacheOptions()));
        public IDistributedCache Cache { get; } = Substitute.For<IDistributedCache>();
        public Clock Clock { get; } = new();
        public ConcurrentDictionary<string, DistributedCacheEntryOptions> Writes { get; } = new();
        public TickerQRedisContext Context { get; }
        public Harness()
        {
            Cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(c => Storage.GetAsync(c.ArgAt<string>(0), c.ArgAt<CancellationToken>(1)));
            Cache.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>())
                .Returns(c =>
                {
                    Writes[c.ArgAt<string>(0)] = c.ArgAt<DistributedCacheEntryOptions>(2);
                    return Storage.SetAsync(c.ArgAt<string>(0), c.ArgAt<byte[]>(1), c.ArgAt<DistributedCacheEntryOptions>(2), c.ArgAt<CancellationToken>(3));
                });
            Cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(c => Storage.RemoveAsync(c.ArgAt<string>(0), c.ArgAt<CancellationToken>(1)));
            Cache.When(x => x.Set(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>()))
                .Do(c => Storage.Set(c.ArgAt<string>(0), c.ArgAt<byte[]>(1), c.ArgAt<DistributedCacheEntryOptions>(2)));
            Cache.When(x => x.Remove(Arg.Any<string>())).Do(c => Storage.Remove(c.ArgAt<string>(0)));
            Context = NewContext();
        }
        public TickerQRedisContext NewContext() => new(Cache, new SchedulerOptionsBuilder(),
            new ServiceExtension.TickerQRedisOptionBuilder(), Substitute.For<ITickerQNotificationHubSender>(),
            NullLogger<TickerQRedisContext>.Instance, Clock);
    }

    [Fact]
    public async Task LifetimeIsApplied_AndExpiredPayloadIsRejectedEvenIfBackendStillReturnsIt()
    {
        var h = new Harness();
        var calls = 0;
        Task<CronTickerEntity[]> Factory(CancellationToken _) => Task.FromResult(Rows($"fn-{++calls}"));
        var lifetime = TimeSpan.FromMinutes(2);
        await h.Context.GetOrSetArrayAsync(Key, Factory, lifetime);
        Assert.Equal(h.Clock.Now.Add(lifetime), h.Writes[Key].AbsoluteExpiration);
        Assert.Null(h.Writes[Key].SlidingExpiration);
        Assert.Equal("fn-1", Assert.Single(await h.Context.GetOrSetArrayAsync(Key, Factory, lifetime)).Function);
        h.Clock.Now += lifetime + TimeSpan.FromSeconds(1);
        Assert.NotNull(await h.Storage.GetAsync(Key)); // The fake clock advanced; the backend clock did not.
        Assert.Equal("fn-2", Assert.Single(await h.Context.GetOrSetArrayAsync(Key, Factory, lifetime)).Function);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("corrupt")]
    [InlineData("unsupported-format")]
    public async Task OldOrInvalidCacheEntry_IsReplacedWithBoundedFreshData(string kind)
    {
        var h = new Harness();
        var old = kind switch
        {
            "legacy" => JsonSerializer.SerializeToUtf8Bytes(Rows("removed-function")),
            "corrupt" => "not-json"u8.ToArray(),
            _ => "{\"format\":99,\"items\":[]}"u8.ToArray()
        };
        await h.Storage.SetAsync(Key, old); // Old versions did not set any expiry.
        var result = await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("new-function")));
        Assert.Equal("new-function", Assert.Single(result).Function);
        Assert.Equal(h.Clock.Now + TickerQRedisContext.DefaultArrayCacheLifetime, h.Writes[Key].AbsoluteExpiration);
        using var envelope = JsonDocument.Parse((await h.Storage.GetAsync(Key))!);
        Assert.Equal(1, envelope.RootElement.GetProperty("format").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentOldFillAfterInvalidation_CannotRestoreAnAcceptedStaleSnapshot(bool synchronousInvalidation)
    {
        var h = new Harness();
        var otherReplica = h.NewContext();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldFill = h.Context.GetOrSetArrayAsync(Key, async _ =>
        {
            readStarted.SetResult();
            await releaseOldRead.Task;
            return Rows("removed-function");
        });
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (synchronousInvalidation) otherReplica.DistributedCache.Remove(Key);
        else await otherReplica.DistributedCache.RemoveAsync(Key);
        await otherReplica.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("new-function")));
        releaseOldRead.SetResult();
        await oldFill; // Deliberately overwrites the newer payload with the old generation.
        var refreshes = 0;
        var current = await otherReplica.GetOrSetArrayAsync(Key, _ =>
        {
            refreshes++;
            return Task.FromResult(Rows("new-function"));
        });
        Assert.Equal("new-function", Assert.Single(current).Function);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task InvalidationGenerationProtectsReadersEvenIfPayloadRemovalFails()
    {
        var h = new Harness();
        await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("old")));
        h.Cache.RemoveAsync(Key, Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("remove failed")));
        await Assert.ThrowsAsync<IOException>(() => h.Context.DistributedCache.RemoveAsync(Key));
        var current = await h.NewContext().GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("new")));
        Assert.Equal("new", Assert.Single(current).Function);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("removal")]
    public async Task FailedInvalidation_BypassesOldData_AndRetryRepairsOtherReplicas(string failure)
    {
        var h = new Harness();
        await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("old")));
        var offline = true;
        if (failure == "generation")
            h.Cache.SetAsync(InvalidatingDistributedCache.GenerationKey(Key), Arg.Any<byte[]>(),
                    Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>())
                .Returns(c => offline ? Task.FromException(new IOException("offline"))
                    : h.Storage.SetAsync(c.ArgAt<string>(0), c.ArgAt<byte[]>(1), c.ArgAt<DistributedCacheEntryOptions>(2)));
        else
            h.Cache.RemoveAsync(Key, Arg.Any<CancellationToken>())
                .Returns(_ => offline ? Task.FromException(new IOException("offline")) : h.Storage.RemoveAsync(Key));
        await Assert.ThrowsAsync<IOException>(() => h.Context.DistributedCache.RemoveAsync(Key));
        Assert.Equal("fresh", Assert.Single(await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("fresh")))).Function);
        await h.Context.RetryPendingInvalidationsAsync(); // Failed retries remain pending; no exception escapes the worker.
        offline = false;
        await h.Context.RetryPendingInvalidationsAsync();
        Assert.Null(await h.Storage.GetAsync(Key));
        Assert.Equal("fresh", Assert.Single(await h.NewContext().GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("fresh")))).Function);
    }

    [Fact]
    public async Task OlderInvalidationCompletion_DoesNotDiscardNewerPendingInvalidation()
    {
        var h = new Harness();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        h.Cache.RemoveAsync(Key, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++calls > 1) return Task.FromException(new IOException("offline"));
            started.SetResult();
            return release.Task;
        });
        var old = h.Context.DistributedCache.RemoveAsync(Key);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<IOException>(() => h.Context.DistributedCache.RemoveAsync(Key));
        release.SetResult();
        await old;
        Assert.True(((InvalidatingDistributedCache)h.Context.DistributedCache).IsPending(Key));
    }

    [Theory]
    [InlineData("before-read")]
    [InlineData("read")]
    [InlineData("factory")]
    [InlineData("write")]
    public async Task CallerCancellationIsNeverSwallowed(string point)
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        var factoryCalls = 0;
        if (point == "before-read") cts.Cancel();
        if (point == "read")
            h.Cache.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                cts.Cancel();
                return Task.FromCanceled<byte[]>(cts.Token);
            });
        if (point == "write")
            h.Cache.SetAsync(Key, Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>())
                .Returns(_ => { cts.Cancel(); return Task.FromCanceled(cts.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Context.GetOrSetArrayAsync(Key, _ =>
        {
            factoryCalls++;
            if (point == "factory") cts.Cancel();
            return Task.FromResult(Rows("fn"));
        }, cancellationToken: cts.Token));
        Assert.Equal(point is "read" or "before-read" ? 0 : 1, factoryCalls);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("read")]
    [InlineData("write")]
    public async Task CacheOutageReturnsPersistenceData(string point)
    {
        var h = new Harness();
        if (point is "generation" or "read")
            h.Cache.GetAsync(point == "read" ? Key : InvalidatingDistributedCache.GenerationKey(Key), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<byte[]>(new IOException("cache unavailable")));
        else
            h.Cache.SetAsync(Key, Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new IOException("cache unavailable")));
        var result = await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("from-database")));
        Assert.Equal("from-database", Assert.Single(result).Function);
        if (point == "generation") Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task FactoryFailurePropagates_AndNullIsNotCached()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Context.GetOrSetArrayAsync<CronTickerEntity>(Key,
            _ => throw new InvalidOperationException("database failure")));
        Assert.Null(await h.Context.GetOrSetArrayAsync<CronTickerEntity>(Key, _ => Task.FromResult<CronTickerEntity[]>(null!)));
        Assert.False(h.Writes.ContainsKey(Key));
    }

    [Fact]
    public async Task EvictedGenerationMarker_DoesNotMakeAnOldPayloadValidAgain()
    {
        var h = new Harness();
        await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("old")));
        var payload = await h.Storage.GetAsync(Key);
        await h.Context.DistributedCache.RemoveAsync(Key);
        // Simulate an old fill completing after invalidation, then Redis evicting the marker.
        await h.Storage.SetAsync(Key, payload!);
        await h.Storage.RemoveAsync(InvalidatingDistributedCache.GenerationKey(Key));
        var current = await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("fresh")));
        Assert.Equal("fresh", Assert.Single(current).Function);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveExpirationBypassesCaching(int seconds)
    {
        var h = new Harness();
        var result = await h.Context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("uncached")), TimeSpan.FromSeconds(seconds));
        Assert.Equal("uncached", Assert.Single(result).Function);
        Assert.Empty(h.Cache.ReceivedCalls());
    }

    [Fact]
    public async Task EmptyArrayIsCached_AndInvalidationDoesNotAffectOtherPartitions()
    {
        var h = new Harness();
        await h.Context.GetOrSetArrayAsync<CronTickerEntity>(Key, _ => Task.FromResult(Array.Empty<CronTickerEntity>()));
        await h.Context.GetOrSetArrayAsync("other-partition", _ => Task.FromResult(Rows("other")));
        Assert.Empty(await h.Context.GetOrSetArrayAsync<CronTickerEntity>(Key, _ => throw new Exception("must hit cache")));
        await h.Context.DistributedCache.RemoveAsync(Key);
        Assert.Single(await h.Context.GetOrSetArrayAsync<CronTickerEntity>("other-partition", _ => throw new Exception("must hit cache")));
    }
}
