using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Entities;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Provider-neutral canonical-owner selection for the seeded rows of a single function during
    /// <c>MigrateDefinedCronTickers</c> reconciliation. Every durable provider (EF Core, MongoDB, Redis)
    /// and the in-memory provider select the canonical row through this one helper so the choice cannot
    /// drift between stores.
    /// <para>
    /// Selecting the lowest id unconditionally is unsafe: if a function already has a row that adopted the
    /// desired <see cref="CronTickerEntity.SeedKey"/> at a higher id while a lower-id legacy null-key
    /// duplicate remains, picking the legacy row would try to assign the already-owned SeedKey to it —
    /// violating the unique SeedKey index (EF/Mongo) or producing two enabled keyed owners
    /// (Redis/in-memory) and retiring the real owner. Preference order:
    /// </para>
    /// <list type="number">
    /// <item>the row that already carries the desired <paramref name="seedKey"/>;</item>
    /// <item>otherwise an enabled, unretired row;</item>
    /// <item>otherwise any unretired row;</item>
    /// <item>otherwise the deterministic lowest-id row.</item>
    /// </list>
    /// Every other row is a redundant duplicate to be retired in place (never deleted).
    /// </summary>
    public static class CronSeedCanonical
    {
        /// <summary>
        /// Selects the canonical owner from <paramref name="orderedById"/> (which the caller has already
        /// sorted by id ascending, so the lowest-id fallback and duplicate order are deterministic) and
        /// returns it together with the redundant duplicates in that same order.
        /// </summary>
        public static (T Canonical, List<T> Duplicates) Select<T>(IReadOnlyList<T> orderedById, string seedKey)
            where T : CronTickerEntity
        {
            if (orderedById == null) throw new ArgumentNullException(nameof(orderedById));
            if (orderedById.Count == 0)
                throw new ArgumentException("Cannot select a canonical row from an empty group.", nameof(orderedById));

            var canonical =
                orderedById.FirstOrDefault(c => string.Equals(c.SeedKey, seedKey, StringComparison.Ordinal))
                ?? orderedById.FirstOrDefault(c => c.RetiredAt == null && c.IsEnabled)
                ?? orderedById.FirstOrDefault(c => c.RetiredAt == null)
                ?? orderedById[0];

            var duplicates = new List<T>(orderedById.Count - 1);
            foreach (var row in orderedById)
                if (!ReferenceEquals(row, canonical))
                    duplicates.Add(row);

            return (canonical, duplicates);
        }
    }
}
