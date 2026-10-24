using System;
using System.Collections.Generic;

namespace TickerQ.Utilities
{
    /// <summary>
    /// The declaration list shared by <see cref="CronTickerSeedBuilder{TCronTicker}"/> and
    /// <see cref="TimeTickerSeedBuilder{TTimeTicker}"/>: each entry is a stable key and a chain-builder
    /// configuration. Building, key defaulting, and duplicate detection are the same for both kinds.
    /// </summary>
    internal sealed class TickerSeedEntries<TChainBuilder>
    {
        private readonly List<TickerSeedEntry<TChainBuilder>> _entries = new();

        internal IReadOnlyList<TickerSeedEntry<TChainBuilder>> Entries => _entries;

        internal void Add(string key, Action<TChainBuilder> configure, string kind)
        {
            if (key != null && string.IsNullOrWhiteSpace(key))
                throw new ArgumentException($"A seeded {kind} key cannot be empty.", nameof(key));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            _entries.Add(new TickerSeedEntry<TChainBuilder>(key?.Trim(), configure));
        }

        /// <summary>
        /// Builds every entry with a fresh chain builder. The key defaults to the root function name, and
        /// two entries with the same key are rejected.
        /// </summary>
        internal List<(string Key, TEntity Entity)> Build<TEntity>(
            string kind,
            Func<TChainBuilder> createBuilder,
            Func<TChainBuilder, TEntity> build,
            Func<TEntity, string> defaultKey)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var built = new List<(string Key, TEntity Entity)>(_entries.Count);
            foreach (var entry in _entries)
            {
                var entity = entry.Build(kind, createBuilder, build);
                var key = entry.Key ?? defaultKey(entity);
                if (!keys.Add(key))
                    throw new InvalidOperationException(
                        $"A {kind} with key '{key}' is already seeded. Use Add(\"key\", ...) to seed the same function more than once.");
                built.Add((key, entity));
            }

            return built;
        }
    }

    internal sealed record TickerSeedEntry<TChainBuilder>(string Key, Action<TChainBuilder> Configure)
    {
        internal TEntity Build<TEntity>(string kind, Func<TChainBuilder> createBuilder, Func<TChainBuilder, TEntity> build)
        {
            try
            {
                var builder = createBuilder();
                Configure(builder);
                return build(builder);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Seeded {kind} '{Key ?? "(unnamed)"}' could not be built: {exception.Message}", exception);
            }
        }
    }
}
