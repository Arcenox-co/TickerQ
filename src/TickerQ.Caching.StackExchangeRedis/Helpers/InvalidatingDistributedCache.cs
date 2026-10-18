using Microsoft.Extensions.Caching.Distributed;
using System.Collections.Concurrent;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

// Invalidation changes the generation before deleting the payload. An in-flight reader
// can still finish its old fill, but subsequent readers reject that obsolete generation.
// Keeping the marker separate also works when the payload was already evicted.
internal sealed class InvalidatingDistributedCache(IDistributedCache inner) : IDistributedCache
{
    private readonly ConcurrentDictionary<string, object> _pending = new(StringComparer.Ordinal);
    internal IEnumerable<string> PendingKeys => _pending.Keys;
    internal bool IsPending(string key) => _pending.ContainsKey(key);
    internal static string GenerationKey(string key) => $"{key}:generation:v1";

    public byte[] Get(string key) => inner.Get(key);
    public Task<byte[]> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        => inner.SetAsync(key, value, options, token);
    public void Refresh(string key) => inner.Refresh(key);
    public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

    public void Remove(string key)
    {
        var version = new object();
        _pending[key] = version;
        inner.SetString(GenerationKey(key), Guid.NewGuid().ToString("N"));
        inner.Remove(key);
        _pending.TryRemove(new KeyValuePair<string, object>(key, version));
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        var version = new object();
        _pending[key] = version;
        await InvalidateAsync(key, version, token).ConfigureAwait(false);
    }

    internal Task RetryAsync(string key, CancellationToken token)
        => _pending.TryGetValue(key, out var version) ? InvalidateAsync(key, version, token) : Task.CompletedTask;

    private async Task InvalidateAsync(string key, object version, CancellationToken token)
    {
        // Every attempt uses a fresh generation. Reusing one during a retry could
        // accidentally validate a fill that raced an earlier failed attempt.
        await inner.SetStringAsync(GenerationKey(key), Guid.NewGuid().ToString("N"), token).ConfigureAwait(false);
        await inner.RemoveAsync(key, token).ConfigureAwait(false);
        _pending.TryRemove(new KeyValuePair<string, object>(key, version));
    }
}
