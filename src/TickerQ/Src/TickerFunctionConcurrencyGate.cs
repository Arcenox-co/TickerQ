using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;

namespace TickerQ
{
    internal sealed class TickerFunctionConcurrencyGate : ITickerFunctionConcurrencyGate
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, Entry> _gates = new(StringComparer.Ordinal);

        private sealed class Entry(int limit)
        {
            internal readonly SemaphoreSlim Semaphore = new(limit, limit);
            internal int References;
            internal bool Pinned;
        }

        // Raw semaphore callers provide no ownership signal. Preserve their lifetime
        // contract; runtime execution uses the reclaimable owned permits below.
        public SemaphoreSlim GetSemaphoreOrNull(string functionName, int maxConcurrency)
        {
            if (maxConcurrency <= 0) return null;
            lock (_sync)
            {
                var entry = GetOrAdd(functionName, maxConcurrency);
                entry.Pinned = true;
                return entry.Semaphore;
            }
        }

        public async ValueTask<IDisposable> AcquireAsync(string functionName, int maxConcurrency,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (maxConcurrency <= 0) return null;
            Entry entry;
            lock (_sync)
            {
                entry = GetOrAdd(functionName, maxConcurrency);
                entry.References++;
            }
            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Permit(this, functionName, entry);
            }
            catch
            {
                Release(functionName, entry, acquired: false);
                throw;
            }
        }

        internal int RetainedGateCount { get { lock (_sync) return _gates.Count; } }

        private Entry GetOrAdd(string name, int limit)
        {
            if (!_gates.TryGetValue(name, out var entry)) _gates.Add(name, entry = new Entry(limit));
            return entry;
        }

        private void Release(string name, Entry entry, bool acquired)
        {
            lock (_sync)
            {
                if (acquired) entry.Semaphore.Release();
                if (--entry.References == 0 && !entry.Pinned)
                {
                    _gates.Remove(name);
                    entry.Semaphore.Dispose();
                }
            }
        }

        private sealed class Permit(TickerFunctionConcurrencyGate owner, string name, Entry entry) : IDisposable
        {
            private TickerFunctionConcurrencyGate _owner = owner;
            public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(name, entry, acquired: true);
        }
    }
}
