using System;
using TickerQ.Utilities.Entities;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// The provider-neutral state machine for two-phase, non-destructive retirement of code-defined
    /// ("seeded") cron tickers (Slice 3). Every durable provider (EF Core, MongoDB, Redis) and the
    /// in-memory provider apply the exact same transitions through these pure helpers, so retirement
    /// semantics cannot drift between stores. The helpers only ever mutate the retirement bookkeeping
    /// on the shared <see cref="CronTickerEntity"/> base — a row, its occurrences and its results are
    /// NEVER deleted here; historical cleanup is retention's job, not reconciliation's.
    /// </summary>
    public static class CronSeedRetirement
    {
        /// <summary>
        /// Applies retirement to a seeded row whose code definition is no longer desired.
        /// <para>
        /// Phase A (first observation): stamp <see cref="CronTickerEntity.RetirementRequestedAt"/> once and
        /// keep the row enabled through the grace window. The timestamp is never re-stamped on later
        /// absent passes, so grace is measured from first absence.
        /// </para>
        /// <para>
        /// Phase B (grace elapsed, or <paramref name="immediate"/> for a blocked required-contract seed):
        /// capture the current enabled state into <see cref="CronTickerEntity.SeedWasEnabledBeforeRetirement"/>,
        /// disable the row, and stamp <see cref="CronTickerEntity.RetiredAt"/>.
        /// </para>
        /// A row already retired is left untouched. Returns <c>true</c> when any field changed.
        /// </summary>
        public static bool ApplyRetirement(CronTickerEntity row, DateTime now, TimeSpan grace, bool immediate)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            // Already fully retired — do not re-stamp or re-capture prior state.
            if (row.RetiredAt != null)
                return false;

            var changed = false;

            if (row.RetirementRequestedAt == null)
            {
                row.RetirementRequestedAt = now;
                changed = true;
            }

            var graceElapsed = immediate || (now - row.RetirementRequestedAt.Value) >= grace;
            if (graceElapsed)
            {
                row.SeedWasEnabledBeforeRetirement = row.IsEnabled;
                row.IsEnabled = false;
                row.RetiredAt = now;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Clears framework retirement state from a desired seed that has (re)appeared.
        /// <para>
        /// If the seed reappears AFTER framework retirement, its enabled state is restored ONLY when it was
        /// enabled before retirement; a seed the user had disabled stays disabled. The saved state is then
        /// cleared. If the seed reappears BEFORE grace elapsed (request stamped but not yet retired), the
        /// request is simply cleared and the prior enabled state is left intact.
        /// </para>
        /// Returns <c>true</c> when any field changed.
        /// </summary>
        public static bool ClearRetirement(CronTickerEntity row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            if (row.RetirementRequestedAt == null && row.RetiredAt == null
                && row.SeedWasEnabledBeforeRetirement == null)
                return false;

            if (row.RetiredAt != null)
            {
                // Reappeared after framework retirement: restore only a framework-disabled seed.
                if (row.SeedWasEnabledBeforeRetirement == true)
                    row.IsEnabled = true;
            }

            row.SeedWasEnabledBeforeRetirement = null;
            row.RetirementRequestedAt = null;
            row.RetiredAt = null;
            return true;
        }

        /// <summary>
        /// Retires a redundant duplicate of a desired seed (a canonical row has already been chosen). The
        /// duplicate is disabled and marked retired in place — but preserved, never deleted, and it keeps a
        /// null <see cref="CronTickerEntity.SeedKey"/> so the unique seed-key index stays satisfied by the
        /// single canonical owner. Returns <c>true</c> when any field changed.
        /// </summary>
        public static bool RetireDuplicate(CronTickerEntity row, DateTime now)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            if (row.RetiredAt != null && !row.IsEnabled)
                return false;

            row.RetirementRequestedAt ??= now;
            row.SeedWasEnabledBeforeRetirement ??= row.IsEnabled;
            row.IsEnabled = false;
            row.RetiredAt = now;
            return true;
        }
    }
}
