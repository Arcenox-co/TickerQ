using System.Text;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class ExecutionLogTests
{
    [Fact]
    public void Health_counts_only_unsaved_tails_as_dropped()
    {
        var options = new ExecutionLogOptions { PersistToDatabase = true, MaxBufferedExecutions = 1 };
        var store = new InMemoryTickerExecutionLogStore(options);
        var first = Guid.NewGuid(); store.Add(Line(first, "saved"));
        store.MarkPersisted(first, store.Snapshot(first).Version);
        store.Add(Line(Guid.NewGuid(), "pending"));
        Assert.Equal(0, store.Health.DroppedUnflushedExecutions);
        Assert.Equal(1, store.Health.PendingWrites);
        store.Add(Line(Guid.NewGuid(), "evicts unsaved"));
        Assert.Equal(1, store.Health.DroppedUnflushedExecutions);
    }

    [Fact]
    public async Task Batch_reader_makes_one_storage_call_and_isolates_corrupt_tails()
    {
        var options = new ExecutionLogOptions { PersistToDatabase = true };
        var local = new InMemoryTickerExecutionLogStore(options);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        local.Add(Line(first, "first")); local.Add(Line(second, "local fallback"));
        var valid = ExecutionLogCodec.Encode(local.Snapshot(first), TickerQRuntimePartition.LegacyGlobal.StorageKey, options.Retention);
        var corrupt = new ExecutionLogArchive { TickerId = second, Payload = [1, 2, 3] };
        var db = Substitute.For<ITickerExecutionLogPersistence>();
        db.ReadBatchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new[] { valid, corrupt });
        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var reader = new ExecutionLogReader(local, options, new SchedulerOptionsBuilder(), services);
        var result = await reader.ReadBatchAsync([first, second], CancellationToken.None);
        Assert.True(result[first].PersistenceAvailable); Assert.Single(result[first].Lines);
        Assert.False(result[second].PersistenceAvailable);
        Assert.Equal("local fallback", Assert.Single(result[second].Lines).Message);
        await db.Received(1).ReadBatchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await db.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.ReadBatchAsync(Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray(), CancellationToken.None));
    }

    [Fact]
    public void Decompression_is_bounded_even_when_a_small_archive_expands_excessively()
    {
        using var output = new MemoryStream();
        using (var zip = new GZipStream(output, CompressionLevel.Fastest, true))
            zip.Write(new byte[ExecutionLogCodec.MaximumPayloadBytes + 10]);
        Assert.Throws<InvalidDataException>(() => ExecutionLogCodec.Decode(new ExecutionLogArchive { Payload = output.ToArray() }));
    }

    [Fact]
    public void New_lines_during_a_flush_remain_pending_and_global_byte_limit_evicts_old_tails()
    {
        var options = new ExecutionLogOptions { MaxBytesPerExecution = 4096, MaxBufferedBytes = 4096 };
        var store = new InMemoryTickerExecutionLogStore(options); var first = Guid.NewGuid();
        store.Add(Line(first, new string('x', 1900)));
        var sent = Assert.Single(store.Pending(10));
        store.Add(Line(first, "arrived during write"));
        store.MarkPersisted(first, sent.Version);
        Assert.Single(store.Pending(10));
        for (var i = 0; i < 3; i++) store.Add(Line(Guid.NewGuid(), new string('x', 1900)));
        Assert.Null(store.Snapshot(first));
        Assert.True(store.Pending(10).Sum(x => x.Lines.Sum(ExecutionLogCodec.LineSize)) <= options.MaxBufferedBytes);
    }

    [Fact]
    public async Task Idle_tails_expire_on_read_without_another_log_write()
    {
        var store = new InMemoryTickerExecutionLogStore(new ExecutionLogOptions { MemoryRetention = TimeSpan.FromSeconds(1) });
        var id = Guid.NewGuid(); store.Add(Line(id, "expires"));
        await Task.Delay(1100);
        Assert.Empty(store.GetTail(id));
    }

    private static TickerExecutionLogLine Line(Guid id, string text) => new()
    { TickerId = id, Message = text, FunctionName = "Example", Category = "Test", UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Level = 2 };

    [Fact]
    public void Tail_is_bounded_by_lines_message_bytes_and_total_execution_count()
    {
        var options = new ExecutionLogOptions { MaxLinesPerExecution = 2, MaxBufferedExecutions = 2, MaxMessageBytes = 128 };
        var store = new InMemoryTickerExecutionLogStore(options);
        var id = Guid.NewGuid();
        for (var i = 0; i < 4; i++) store.Add(Line(id, new string('界', 500)));
        var tail = store.Snapshot(id);
        Assert.Equal(2, tail.Lines.Count); Assert.True(tail.Truncated);
        Assert.All(tail.Lines, line => Assert.InRange(Encoding.UTF8.GetByteCount(line.Message), 1, 128));
        Assert.DoesNotContain("�", tail.Lines[0].Message);
        store.Add(Line(Guid.NewGuid(), "next")); store.Add(Line(Guid.NewGuid(), "another"));
        Assert.Equal(2, store.Pending(100).Length); Assert.Equal(1, store.EvictedExecutions);
        Assert.Null(store.Snapshot(id));
    }

    [Fact]
    public void Encoded_payload_budget_is_enforced_and_compression_roundtrips()
    {
        var options = new ExecutionLogOptions { MaxBytesPerExecution = 4096, MaxBufferedBytes = 4096 };
        var store = new InMemoryTickerExecutionLogStore(options); var id = Guid.NewGuid();
        for (var i = 0; i < 20; i++) store.Add(Line(id, new string('x', 1000)));
        var snapshot = store.Snapshot(id);
        Assert.True(snapshot.Truncated);
        Assert.True(snapshot.Lines.Sum(ExecutionLogCodec.LineSize) + 8 <= 4096);
        var encoded = ExecutionLogCodec.Encode(snapshot, "partition", options.Retention);
        Assert.True(encoded.Payload.Length < 1000);
        var decoded = ExecutionLogCodec.Decode(encoded);
        Assert.Equal(snapshot.Lines.Select(x => x.Id), decoded.Select(x => x.Id));
        Assert.Equal(snapshot.Lines.Select(x => x.Message), decoded.Select(x => x.Message));
    }

    [Fact]
    public void Invalid_archives_and_unsafe_option_limits_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionLogOptions { MaxStoredExecutions = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionLogOptions { Retention = TimeSpan.Zero }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionLogOptions { MaxBytesPerExecution = 4096, MaxMessageBytes = 16384 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ExecutionLogOptions { Enabled = false, PersistToDatabase = true }.Validate());
        Assert.ThrowsAny<Exception>(() => ExecutionLogCodec.Decode(new ExecutionLogArchive { Payload = [1, 2, 3] }));
        Assert.Throws<InvalidDataException>(() => ExecutionLogCodec.Decode(new ExecutionLogArchive { Payload = new byte[ExecutionLogCodec.MaximumPayloadBytes + 1025] }));
    }

    [Fact]
    public void Redaction_and_level_filter_run_before_storage_and_capture_does_not_throw()
    {
        var options = new ExecutionLogOptions { MinimumLevel = LogLevel.Warning, Redact = text => text.Replace("secret", "[redacted]") };
        var store = new InMemoryTickerExecutionLogStore(options);
        var logger = new TickerExecutionLoggerProvider(store, options).CreateLogger("Example"); var id = Guid.NewGuid();
        using (TickerExecutionLogScope.Push(id, "Example"))
        {
            logger.LogInformation("secret"); logger.LogWarning("secret");
            options.Redact = _ => throw new InvalidOperationException();
            logger.LogError("must not break job");
        }
        Assert.Equal("[redacted]", Assert.Single(store.GetTail(id)).Message);
    }

    [Fact]
    public async Task Flush_failure_preserves_pending_data_and_retry_is_acknowledged_only_after_success()
    {
        var options = new ExecutionLogOptions { PersistToDatabase = true };
        var store = new InMemoryTickerExecutionLogStore(options); var id = Guid.NewGuid(); store.Add(Line(id, "persist me"));
        var db = Substitute.For<ITickerExecutionLogPersistence>();
        db.WriteAsync(Arg.Any<IReadOnlyList<ExecutionLogArchive>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("unavailable"));
        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        using var health = new TickerQRuntimeDiagnostics(store);
        var writer = new TickerQExecutionLogBackgroundService(store, options, new SchedulerOptionsBuilder(), services,
            Substitute.For<ITickerQActivationGate>(), NullLogger<TickerQExecutionLogBackgroundService>.Instance, health);
        await Assert.ThrowsAsync<IOException>(() => writer.FlushAsync(CancellationToken.None));
        Assert.Single(store.Pending(10));
        Assert.False(health.LogWritesAvailable); Assert.Equal(1, health.LogWriteFailures);
        Assert.Null(health.LastSuccessfulLogFlushUtc);
        db.WriteAsync(Arg.Any<IReadOnlyList<ExecutionLogArchive>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await writer.FlushAsync(CancellationToken.None);
        Assert.Empty(store.Pending(10)); Assert.Single(store.GetTail(id));
        Assert.True(health.LogWritesAvailable); Assert.NotNull(health.LastSuccessfulLogFlushUtc);
        Assert.Equal(1, health.LogWriteFailures);
    }

    [Fact]
    public async Task Reader_uses_database_after_local_restart_and_marks_storage_failures()
    {
        var options = new ExecutionLogOptions { PersistToDatabase = true };
        var id = Guid.NewGuid(); var first = new InMemoryTickerExecutionLogStore(options); first.Add(Line(id, "survives restart"));
        var archive = ExecutionLogCodec.Encode(first.Snapshot(id), TickerQRuntimePartition.LegacyGlobal.StorageKey, options.Retention);
        var db = Substitute.For<ITickerExecutionLogPersistence>();
        db.ReadAsync(Arg.Any<string>(), id, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(archive);
        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var local = new InMemoryTickerExecutionLogStore(options);
        var reader = new ExecutionLogReader(local, options, new SchedulerOptionsBuilder(), services);
        Assert.Equal("survives restart", Assert.Single((await reader.ReadAsync(id, CancellationToken.None)).Lines).Message);
        local.Add(Line(id, "local still works"));
        db.ReadAsync(Arg.Any<string>(), id, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<ExecutionLogArchive>(new IOException()));
        var degraded = await reader.ReadAsync(id, CancellationToken.None);
        Assert.False(degraded.PersistenceAvailable); Assert.Equal("local still works", Assert.Single(degraded.Lines).Message);
    }

    [Fact]
    public async Task Persistence_without_a_supported_provider_fails_explicitly()
    {
        var options = new ExecutionLogOptions { PersistToDatabase = true };
        using var services = new ServiceCollection().BuildServiceProvider();
        var writer = new TickerQExecutionLogBackgroundService(new(options), options, new SchedulerOptionsBuilder(), services,
            Substitute.For<ITickerQActivationGate>(), NullLogger<TickerQExecutionLogBackgroundService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync(CancellationToken.None));
    }
}
