using System.Collections.Concurrent;
using Google.Protobuf;
using TickerQ.RemoteExecutor.Logging;
using TickerQ.RemoteExecutor.Tunnel;
using TickerQ.RemoteExecutor.TunnelClient;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class RemoteLogMemoryTests
{
    [Fact]
    public void Many_executions_and_large_messages_remain_within_global_budgets()
    {
        var buffer = new TickerLogRingBuffer(capacity: 3, maxExecutions: 8, maxBytes: 20_000);
        var largeMessage = new string('x', 100_000);
        for (var i = 0; i < 2000; i++)
        {
            var id = Guid.NewGuid();
            buffer.Append(Line(id, largeMessage));
            Assert.InRange(buffer.ExecutionCount, 1, 8);
            Assert.InRange(buffer.BufferedBytes, 1, 20_000);
            Assert.InRange(Assert.Single(buffer.GetTail(id)).Message.Length, 1, TickerLogRingBuffer.MaximumMessageCharacters);
        }
        Assert.True(buffer.EvictedExecutions > 0);
        buffer.Sweep(DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(0, buffer.ExecutionCount);
        Assert.Equal(0, buffer.BufferedBytes);
    }

    [Fact]
    public void Sparse_executions_do_not_allocate_full_500_reference_rings()
    {
        var buffer = new TickerLogRingBuffer();
        buffer.Append(Line(Guid.NewGuid(), "warmup"));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) buffer.Append(Line(Guid.NewGuid(), "one line"));
        var perExecution = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
        Assert.True(perExecution < 2048, $"A sparse execution allocated {perExecution} bytes.");
        Assert.Equal(1000, buffer.ExecutionCount);
    }

    [Fact]
    public void Per_execution_tail_and_recently_written_eviction_preserve_order()
    {
        var buffer = new TickerLogRingBuffer(capacity: 2, maxExecutions: 2);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        buffer.Append(Line(a, "first")); buffer.Append(Line(b, "old"));
        buffer.Append(Line(a, "second")); buffer.Append(Line(a, "third"));
        buffer.Append(Line(c, "new"));
        Assert.Empty(buffer.GetTail(b));
        Assert.Equal(new[] { "second", "third" }, buffer.GetTail(a).Select(x => x.Message));
    }

    [Fact]
    public void Clipping_does_not_split_surrogate_pairs_and_caps_metadata()
    {
        var buffer = new TickerLogRingBuffer();
        var id = Guid.NewGuid();
        buffer.Append(new TickerLogLine
        {
            TickerId = id,
            Message = new string('x', TickerLogRingBuffer.MaximumMessageCharacters - 2) + "😀trailing",
            Category = new string('c', 10_000), FunctionName = new string('f', 10_000), Source = new string('s', 10_000)
        });
        var line = Assert.Single(buffer.GetTail(id));
        Assert.False(char.IsSurrogate(line.Message[^2]));
        Assert.Equal(TickerLogRingBuffer.MaximumMetadataCharacters, line.Category.Length);
        Assert.Equal(TickerLogRingBuffer.MaximumMetadataCharacters, line.FunctionName.Length);
        Assert.Equal(TickerLogRingBuffer.MaximumMetadataCharacters, line.Source.Length);
    }

    [Fact]
    public void Lifecycle_dedup_expires_and_allows_reused_ticker_ids()
    {
        var now = DateTime.UtcNow;
        var cache = new TickerLifecycleCache(8, TimeSpan.FromMinutes(1), () => now);
        var ctx = new InternalFunctionContext { TickerId = Guid.NewGuid(), Status = TickerStatus.Done, ExecutedAt = now };
        Assert.True(cache.TryTransition(ctx, out _));
        Assert.False(cache.TryTransition(ctx, out _));
        ctx.Status = TickerStatus.DueDone;
        Assert.False(cache.TryTransition(ctx, out _));
        ctx.Status = TickerStatus.InProgress;
        Assert.True(cache.TryTransition(ctx, out _));
        ctx.Status = TickerStatus.Done;
        Assert.True(cache.TryTransition(ctx, out _));
        ctx.ExecutedAt = now.AddSeconds(1);
        Assert.True(cache.TryTransition(ctx, out _));
        Assert.False(cache.TryTransition(ctx, out _));
        now = now.AddMinutes(2);
        cache.Sweep();
        Assert.Equal(0, cache.Count);
        Assert.True(cache.TryTransition(ctx, out _));
    }

    [Fact]
    public void Lifecycle_capacity_and_execution_generations_are_bounded()
    {
        var cache = new TickerLifecycleCache(capacity: 4);
        for (var i = 0; i < 1000; i++)
            cache.TryTransition(new InternalFunctionContext { TickerId = Guid.NewGuid(), Status = TickerStatus.Done }, out _);
        Assert.Equal(4, cache.Count);
        var ctx = new InternalFunctionContext { TickerId = Guid.NewGuid(), Status = TickerStatus.Done, AcquisitionToken = Guid.NewGuid() };
        Assert.True(cache.TryTransition(ctx, out _));
        Assert.False(cache.TryTransition(ctx, out _));
        ctx.AcquisitionToken = Guid.NewGuid();
        Assert.True(cache.TryTransition(ctx, out _));
    }

    [Fact]
    public async Task Slow_tunnel_limits_pending_work_preserves_fifo_and_counts_drops()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new ConcurrentQueue<long>();
        var dropped = 0;
        await using var queue = new TunnelOutboundQueue(async (frame, ct) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            sent.Enqueue(frame.Notification.Sequence);
            if (sent.Count == 3) drained.TrySetResult();
        }, () => Interlocked.Increment(ref dropped), CancellationToken.None, maxFrames: 3, maxBytes: 4096);
        Assert.True(queue.TryEnqueue(Frame(1)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(queue.TryEnqueue(Frame(2)));
        Assert.True(queue.TryEnqueue(Frame(3)));
        for (var i = 0; i < 1000; i++) Assert.False(queue.TryEnqueue(Frame(4)));
        Assert.Equal(3, queue.PendingFrames);
        Assert.InRange(queue.PendingBytes, 1, 4096);
        Assert.Equal(1000, dropped);
        release.TrySetResult();
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new long[] { 1, 2, 3 }, sent);
    }

    [Fact]
    public async Task Tunnel_byte_limit_and_shutdown_release_inflight_and_queued_frames()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropped = 0;
        var queue = new TunnelOutboundQueue(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, () => Interlocked.Increment(ref dropped), CancellationToken.None, maxFrames: 20, maxBytes: 1024);
        Assert.True(queue.TryEnqueue(Frame(1)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(queue.TryEnqueue(Frame(2)));
        var oversized = Frame(3);
        oversized.Notification.PayloadJson = ByteString.CopyFrom(new byte[1024]);
        Assert.False(queue.TryEnqueue(oversized));
        await queue.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, queue.PendingFrames);
        Assert.Equal(0, queue.PendingBytes);
        Assert.Equal(3, dropped);
        Assert.False(queue.TryEnqueue(Frame(4)));
    }

    private static TickerLogLine Line(Guid id, string text) => new() { TickerId = id, Message = text };
    private static SchedulerEvent Frame(long sequence) => new() { Notification = new NotificationEvent { Sequence = sequence } };
}
