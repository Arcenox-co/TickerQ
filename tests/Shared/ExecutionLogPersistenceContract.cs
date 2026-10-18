using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests.Shared;

public abstract class ExecutionLogPersistenceContract
{
    protected abstract ITickerExecutionLogPersistence NewStore();
    private readonly string _partition = new TickerQRuntimePartition("logs-" + Guid.NewGuid()).StorageKey;

    private ExecutionLogArchive Record(Guid id, long version, long expiry, byte value = 1, string? partition = null)
    {
        partition ??= _partition;
        return new() { Id = ExecutionLogArchive.StorageId(partition, id), PartitionKey = partition, TickerId = id,
            UpdatedAtUtcTicks = version, ExpiresAtUtcTicks = expiry, Payload = [value, 2, 3], Truncated = value == 2 };
    }

    [Fact]
    public async Task Batch_read_returns_only_requested_unexpired_records_in_the_exact_partition()
    {
        var store = NewStore(); var now = DateTime.UtcNow.Ticks;
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var expired = Guid.NewGuid(); var unrequested = Guid.NewGuid();
        await store.WriteAsync([Record(first, now, now + TimeSpan.TicksPerDay), Record(second, now, now + TimeSpan.TicksPerDay),
            Record(expired, now, now - 1), Record(unrequested, now, now + TimeSpan.TicksPerDay),
            Record(first, now, now + TimeSpan.TicksPerDay, 9, _partition + "-other")], CancellationToken.None);
        var rows = await store.ReadBatchAsync(_partition, [first, second, expired, Guid.NewGuid()], now, CancellationToken.None);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.TickerId == first); Assert.Contains(rows, row => row.TickerId == second);
        Assert.All(rows, row => { Assert.Equal(_partition, row.PartitionKey); Assert.Equal(1, row.Payload[0]); });
        Assert.Empty(await store.ReadBatchAsync(_partition, [], now, CancellationToken.None));
    }

    [Fact]
    public async Task Compressed_payload_survives_store_recreation_and_older_retries_cannot_replace_it()
    {
        var store = NewStore(); var now = DateTime.UtcNow.Ticks; var id = Guid.NewGuid();
        var first = Record(id, now, now + TimeSpan.TicksPerDay);
        var newer = Record(id, now + 100, now + TimeSpan.TicksPerDay, 2);
        await store.WriteAsync([first], CancellationToken.None);
        await store.WriteAsync([newer], CancellationToken.None);
        await store.WriteAsync([first], CancellationToken.None);
        var read = await NewStore().ReadAsync(_partition, id, now, CancellationToken.None);
        Assert.NotNull(read); Assert.Equal(newer.Payload, read.Payload); Assert.True(read.Truncated);
        Assert.Equal(newer.UpdatedAtUtcTicks, read.UpdatedAtUtcTicks);
        Assert.Null(await store.ReadAsync("another-partition", id, now, CancellationToken.None));
        Assert.Null(await store.ReadAsync(_partition, id, now + 2 * TimeSpan.TicksPerDay, CancellationToken.None));
    }

    [Fact]
    public async Task Cleanup_expires_and_caps_tails_without_touching_another_application()
    {
        var store = NewStore(); var now = DateTime.UtcNow.Ticks;
        var rows = Enumerable.Range(0, 5).Select(i => Record(Guid.NewGuid(), now + i * TimeSpan.TicksPerSecond, now + TimeSpan.TicksPerDay)).ToArray();
        var expired = Record(Guid.NewGuid(), now - TimeSpan.TicksPerDay, now - 1);
        var other = Record(Guid.NewGuid(), now, now + TimeSpan.TicksPerDay, partition: _partition + "-other");
        await store.WriteAsync([.. rows, expired, other], CancellationToken.None);
        await store.PruneAsync(_partition, now, 2, 100, CancellationToken.None);
        for (var i = 0; i < rows.Length; i++)
        {
            var result = await store.ReadAsync(_partition, rows[i].TickerId, now, CancellationToken.None);
            if (i < 3) Assert.Null(result); else Assert.NotNull(result);
        }
        Assert.Null(await store.ReadAsync(_partition, expired.TickerId, now, CancellationToken.None));
        Assert.NotNull(await store.ReadAsync(other.PartitionKey, other.TickerId, now, CancellationToken.None));
    }

    [Fact]
    public async Task Retrying_the_same_flush_is_idempotent()
    {
        var store = NewStore(); var now = DateTime.UtcNow.Ticks; var id = Guid.NewGuid();
        var row = Record(id, now, now + TimeSpan.TicksPerDay);
        await store.WriteAsync([row], CancellationToken.None);
        await store.WriteAsync([row], CancellationToken.None);
        await store.PruneAsync(_partition, now, 1, 100, CancellationToken.None);
        Assert.Equal(row.Payload, (await store.ReadAsync(_partition, id, now, CancellationToken.None)).Payload);
    }
}
