using System;
using System.Collections.Generic;
using System.Linq;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Phase of the durable reconciliation activation epoch. The epoch only ever moves forward through
    /// these phases (and forward across epoch numbers); a lower phase/epoch never overwrites a higher one.
    /// </summary>
    public enum ActivationEpochPhase
    {
        /// <summary>No epoch has been claimed on this store yet (pre-epoch, first adoption).</summary>
        Pending = 0,

        /// <summary>An epoch has been claimed and reconciliation is in progress but not yet durable.</summary>
        Activating = 1,

        /// <summary>
        /// The epoch is durably activated: schema upgrade, discoverability/chain repair, defined-Cron
        /// reconciliation, and durable finalization all completed. This is the single atomic boundary
        /// that old rolling nodes observe — they see either a pre-epoch/activating store or this one.
        /// </summary>
        Activated = 2
    }

    /// <summary>
    /// Immutable snapshot of a store's durable activation epoch. Persisted per provider (EF metadata
    /// singleton, Mongo metadata document, Redis epoch key, in-memory latch) and read at startup so the
    /// initializer can fence the scheduler until <see cref="ActivationEpochPhase.Activated"/> is reached.
    /// </summary>
    public sealed class ActivationEpochState
    {
        public long Epoch { get; init; }
        public ActivationEpochPhase Phase { get; init; }

        /// <summary>Opaque, provider-persisted resume token for the in-progress phase (idempotent restart).</summary>
        public string Checkpoint { get; init; }

        /// <summary>
        /// Fingerprint of the startup inputs (functions, request contracts, seeds, TickerQ version) of the
        /// deployment that claimed this epoch, or <see langword="null"/> when the claim did not record one.
        /// </summary>
        public string Manifest { get; init; }

        /// <summary>Fingerprints of earlier deployments, newest first, bounded to <see cref="ActivationManifests.MaxHistory"/>.</summary>
        public IReadOnlyList<string> ManifestHistory { get; init; } = Array.Empty<string>();

        /// <summary>True when the store is durably activated at or beyond <paramref name="targetEpoch"/>.</summary>
        public bool IsActivatedFor(long targetEpoch)
            => Phase == ActivationEpochPhase.Activated && Epoch >= targetEpoch;

        /// <summary>The initial state of a store that has never seen an activation epoch.</summary>
        public static ActivationEpochState PreEpoch { get; } =
            new() { Epoch = 0, Phase = ActivationEpochPhase.Pending, Checkpoint = null };

        public override string ToString() => $"epoch={Epoch}, phase={Phase}, checkpoint={Checkpoint ?? "<none>"}";
    }

    /// <summary>
    /// Shared bookkeeping for the startup-manifest fingerprints stored with an activation epoch. Every
    /// provider applies the same transitions, so the history means the same thing in every store.
    /// </summary>
    public static class ActivationManifests
    {
        /// <summary>Most earlier fingerprints kept with an activation record.</summary>
        public const int MaxHistory = 32;

        /// <summary>
        /// Maximum fingerprint length accepted by providers. Fingerprints are 35 characters; the bound keeps the
        /// stored history within 2,000 characters, the largest Oracle NVARCHAR2 column.
        /// </summary>
        public const int MaxLength = 48;

        /// <summary>Maximum length of the stored, comma-separated history.</summary>
        public const int MaxHistoryLength = MaxHistory * (MaxLength + 1);

        /// <summary>
        /// The history after <paramref name="next"/> claims a new epoch: the previous fingerprint moves to the
        /// front, <paramref name="next"/> is removed (it is current again), and the list is bounded.
        /// </summary>
        public static IReadOnlyList<string> Advance(string current, IReadOnlyList<string> history, string next)
        {
            var result = new List<string>(MaxHistory);
            if (!string.IsNullOrEmpty(current) && !string.Equals(current, next, StringComparison.Ordinal))
                result.Add(current);
            foreach (var entry in history ?? Array.Empty<string>())
            {
                if (result.Count == MaxHistory) break;
                if (string.IsNullOrEmpty(entry) || string.Equals(entry, next, StringComparison.Ordinal) ||
                    result.Contains(entry, StringComparer.Ordinal))
                    continue;
                result.Add(entry);
            }

            return result;
        }

        public static string JoinHistory(IReadOnlyList<string> history)
            => history is { Count: > 0 } ? string.Join(",", history) : null;

        public static IReadOnlyList<string> SplitHistory(string stored)
            => string.IsNullOrEmpty(stored)
                ? Array.Empty<string>()
                : stored.Split(',', StringSplitOptions.RemoveEmptyEntries);

        public static void Validate(string manifest)
        {
            if (manifest == null) return;
            if (manifest.Length == 0 || manifest.Length > MaxLength || manifest.Contains(','))
                throw new ArgumentException(
                    $"A startup manifest fingerprint must be 1-{MaxLength} characters without commas.", nameof(manifest));
        }
    }
}
