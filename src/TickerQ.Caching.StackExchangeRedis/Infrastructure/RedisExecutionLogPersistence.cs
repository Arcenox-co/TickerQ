using System.Globalization;
using StackExchange.Redis;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed class RedisExecutionLogPersistence(IDatabase database, string instanceName) : ITickerExecutionLogPersistence
{
    private string Prefix(string partition) => $"{instanceName}execution-logs:{{{partition}}}:";
    private const string SaveScript = """
        local old = redis.call('HGET', KEYS[1], 'v')
        if old and old > ARGV[1] then return 0 end
        redis.call('HSET', KEYS[1], 'v', ARGV[1], 'e', ARGV[2], 'p', ARGV[3], 't', ARGV[4])
        redis.call('PEXPIREAT', KEYS[1], ARGV[5])
        redis.call('ZADD', KEYS[2], ARGV[6], ARGV[7])
        redis.call('ZADD', KEYS[3], ARGV[5], ARGV[7])
        return 1
        """;
    private const string PruneScript = """
        local expired = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', ARGV[1], 'LIMIT', 0, ARGV[3])
        for _, id in ipairs(expired) do
            redis.call('DEL', ARGV[4] .. id)
            redis.call('ZREM', KEYS[1], id)
            redis.call('ZREM', KEYS[2], id)
        end
        local over = redis.call('ZCARD', KEYS[1]) - tonumber(ARGV[2])
        if over > 0 then
            local oldest = redis.call('ZRANGE', KEYS[1], 0, math.min(over, tonumber(ARGV[3])) - 1)
            for _, id in ipairs(oldest) do
                redis.call('DEL', ARGV[4] .. id)
                redis.call('ZREM', KEYS[1], id)
                redis.call('ZREM', KEYS[2], id)
            end
        end
        return 1
        """;

    public async Task WriteAsync(IReadOnlyList<ExecutionLogArchive> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0) return;
        cancellationToken.ThrowIfCancellationRequested();
        var batch = database.CreateBatch();
        var writes = new List<Task<RedisResult>>();
        foreach (var row in records)
        {
            var prefix = Prefix(row.PartitionKey);
            writes.Add(batch.ScriptEvaluateAsync(SaveScript,
                new RedisKey[] { prefix + row.Id, prefix + "updated", prefix + "expires" },
                new RedisValue[] { row.UpdatedAtUtcTicks.ToString("D19", CultureInfo.InvariantCulture), row.ExpiresAtUtcTicks,
                    row.Payload, row.Truncated ? 1 : 0, UnixMs(row.ExpiresAtUtcTicks), UnixMs(row.UpdatedAtUtcTicks), row.Id }));
        }
        batch.Execute();
        await Task.WhenAll(writes).WaitAsync(cancellationToken);
    }

    public async Task<ExecutionLogArchive> ReadAsync(string partitionKey, Guid tickerId, long nowUtcTicks, CancellationToken cancellationToken)
    {
        var id = ExecutionLogArchive.StorageId(partitionKey, tickerId);
        var fields = await database.HashGetAsync(Prefix(partitionKey) + id,
            new RedisValue[] { "v", "e", "p", "t" }).WaitAsync(cancellationToken);
        if (fields[0].IsNull || fields[1].IsNull || (long)fields[1] <= nowUtcTicks) return null;
        return new ExecutionLogArchive
        {
            Id = id, PartitionKey = partitionKey, TickerId = tickerId, UpdatedAtUtcTicks = (long)fields[0],
            ExpiresAtUtcTicks = (long)fields[1], Payload = (byte[])fields[2], Truncated = fields[3] == "1"
        };
    }

    public async Task<IReadOnlyList<ExecutionLogArchive>> ReadBatchAsync(string partitionKey, IReadOnlyList<Guid> tickerIds,
        long nowUtcTicks, CancellationToken cancellationToken)
    {
        if (tickerIds.Count == 0) return Array.Empty<ExecutionLogArchive>();
        cancellationToken.ThrowIfCancellationRequested();
        var batch = database.CreateBatch();
        var reads = tickerIds.Select(id => batch.HashGetAsync(Prefix(partitionKey) + ExecutionLogArchive.StorageId(partitionKey, id),
            new RedisValue[] { "v", "e", "p", "t" })).ToArray();
        batch.Execute();
        var results = await Task.WhenAll(reads).WaitAsync(cancellationToken);
        var rows = new List<ExecutionLogArchive>();
        for (var i = 0; i < results.Length; i++)
        {
            var fields = results[i];
            if (fields[0].IsNull || fields[1].IsNull || (long)fields[1] <= nowUtcTicks) continue;
            rows.Add(new ExecutionLogArchive
            {
                Id = ExecutionLogArchive.StorageId(partitionKey, tickerIds[i]), PartitionKey = partitionKey, TickerId = tickerIds[i],
                UpdatedAtUtcTicks = (long)fields[0], ExpiresAtUtcTicks = (long)fields[1], Payload = (byte[])fields[2], Truncated = fields[3] == "1"
            });
        }
        return rows;
    }

    public async Task PruneAsync(string partitionKey, long nowUtcTicks, int maxExecutions, int batchSize, CancellationToken cancellationToken)
    {
        var prefix = Prefix(partitionKey);
        await database.ScriptEvaluateAsync(PruneScript, new RedisKey[] { prefix + "updated", prefix + "expires" },
            new RedisValue[] { UnixMs(nowUtcTicks), maxExecutions, batchSize, prefix }).WaitAsync(cancellationToken);
    }
    private static long UnixMs(long ticks) => new DateTimeOffset(ticks, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
