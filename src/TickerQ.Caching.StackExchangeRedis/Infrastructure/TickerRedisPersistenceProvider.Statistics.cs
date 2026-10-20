using StackExchange.Redis;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker> : ITickerDashboardStatisticsProvider
{
    public async Task<IReadOnlyDictionary<TickerStatus, int>> GetOverallJobStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<TickerStatus, int>();
        // Redis dashboard time queries expose aggregate roots. Keep that established meaning.
        await CountAsync(TimeTickerIdsKey, TimeTickerKey).ConfigureAwait(false);
        await CountAsync(CronOccurrenceIdsKey, CronOccurrenceKey).ConfigureAwait(false);
        return counts;

        async Task CountAsync(RedisKey idsKey, Func<Guid, string> documentKey)
        {
            const string script = """
                local counts = {}
                for _, key in ipairs(KEYS) do
                    local raw = redis.call('GET', key)
                    if raw then
                        local ok, row = pcall(cjson.decode, raw)
                        if ok and type(row) == 'table' and type(row.Status) == 'number' then
                            counts[row.Status] = (counts[row.Status] or 0) + 1
                        end
                    end
                end
                local result = {}
                for status, count in pairs(counts) do
                    result[#result + 1] = status
                    result[#result + 1] = count
                end
                return result
                """;
            var batchSize = IsClusterTopology(Db) ? 1 : 256;
            var keys = new List<RedisKey>(batchSize);
            // SSCAN can repeat members while the set changes; deduplicate ids without retaining payloads.
            var seen = new HashSet<Guid>();
            await foreach (var value in Db.SetScanAsync(idsKey, pageSize: 256).WithCancellation(cancellationToken))
            {
                if (!Guid.TryParse(value.ToString(), out var id) || !seen.Add(id)) continue;
                keys.Add(documentKey(id));
                if (keys.Count == batchSize) await FlushAsync().ConfigureAwait(false);
            }
            if (keys.Count > 0) await FlushAsync().ConfigureAwait(false);
            async Task FlushAsync()
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = (RedisResult[])await Db.ScriptEvaluateAsync(script, keys.ToArray()).ConfigureAwait(false);
                for (var index = 0; index < result.Length; index += 2)
                {
                    var status = (TickerStatus)(int)(long)result[index];
                    counts[status] = counts.GetValueOrDefault(status) + (int)(long)result[index + 1];
                }
                keys.Clear();
            }
        }
    }
}
