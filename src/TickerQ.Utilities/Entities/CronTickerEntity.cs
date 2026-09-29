using System;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    public class CronTickerEntity : BaseTickerEntity
    {
        /// <summary>
        /// Stable code-ownership key for a code-defined ("seeded") cron, deterministic from the local
        /// code definition identity (the function name — the expression is deliberately NOT part of
        /// identity, so changing a schedule never re-owns the row). Null for user/dashboard-created rows,
        /// which are never claimed by code reconciliation. Additive and nullable so a prior release can
        /// still read an upgraded store during a rolling rollout (invariant 9). This is the single
        /// convergence key: exactly one active seeded row exists per non-null <see cref="SeedKey"/>.
        /// </summary>
        [JsonInclude]
        public virtual string SeedKey { get; set; }

        /// <summary>Application namespace that owns this code-defined row; null for user and legacy rows.</summary>
        [JsonInclude]
        public virtual string SeedOwnerNamespace { get; set; }

        /// <summary>
        /// Monotonic desired-manifest epoch last applied by the owning application. Reconcilers must
        /// reject lower values so a restarted older rollout cannot overwrite a newer manifest.
        /// </summary>
        [JsonInclude]
        public virtual long? SeedManifestEpoch { get; set; }

        /// <summary>
        /// Monotonic semantic revision of this definition. Occurrences are stamped with this value;
        /// acquisition must reject a mismatch. Legacy definitions start at zero and are raised on the
        /// first authoritative reconciliation.
        /// </summary>
        [JsonInclude]
        public virtual long DefinitionRevision { get; set; }

        /// <summary>
        /// Last time the reconciler observed this seed's code definition as still desired. Advisory
        /// bookkeeping for the two-phase retirement introduced in Slice 3; additive/nullable here.
        /// </summary>
        [JsonInclude]
        public virtual DateTime? SeedLastSeenAt { get; set; }

        /// <summary>
        /// Phase-A retirement marker: when set, the reconciler has observed that this seed's code
        /// definition is no longer desired and is honoring the
        /// <c>SchedulerOptionsBuilder.DefinedCronRetirementGracePeriod</c> grace window before disabling
        /// it. Stamped once and never re-stamped while the seed stays absent; cleared if the seed
        /// reappears. Additive/nullable.
        /// </summary>
        [JsonInclude]
        public virtual DateTime? RetirementRequestedAt { get; set; }

        /// <summary>
        /// Phase-B retirement marker: when set, this seeded definition has been retired (disabled) after
        /// its grace window elapsed (or immediately, for a blocked required-contract seed). The row and
        /// all of its occurrences/results are preserved — retirement is never destructive. Cleared if the
        /// seed later reappears. Additive/nullable.
        /// </summary>
        [JsonInclude]
        public virtual DateTime? RetiredAt { get; set; }

        /// <summary>
        /// The seed's <see cref="IsEnabled"/> state captured at the moment framework retirement disabled
        /// it. On a later reappearance, only a seed that was enabled before retirement
        /// (<c>true</c>) is re-enabled; a value of <c>false</c> means the user had already disabled it, so
        /// that disabled state is preserved. Cleared back to null once restoration has been applied.
        /// Additive/nullable so a prior release can still read an upgraded store during a rolling rollout.
        /// </summary>
        [JsonInclude]
        public virtual bool? SeedWasEnabledBeforeRetirement { get; set; }

        public virtual string Expression { get; set; }
        public virtual byte[] Request { get; set; }
        public virtual int Retries { get; set; }
        public virtual int[] RetryIntervals { get; set; }
        public virtual bool IsEnabled { get; set; } = true;

        /// <summary>
        /// System-managed pause flag — set automatically when the SDK node
        /// owning this cron's Function disconnects, cleared on reconnect.
        /// Distinct from <see cref="IsEnabled"/> (user-managed) so the
        /// dashboard can distinguish "user disabled this" from "auto-paused
        /// while waiting for the SDK to come back". Polling skips a cron
        /// when this is true even if IsEnabled is true.
        /// </summary>
        public virtual bool IsSystemPaused { get; set; }

        /// <summary>
        /// What the stale-job watchdog does with an occurrence of this cron whose
        /// executing node died mid-run (lease expired while InProgress). Template
        /// level — applies to every occurrence.
        /// </summary>
        public virtual StaleAction OnStale { get; set; }

        /// <summary>
        /// Max execution time per attempt, in seconds — template level, applies to
        /// every occurrence. Null inherits the global default; zero or negative
        /// disables the timeout explicitly.
        /// </summary>
        public virtual int? TimeoutSeconds { get; set; }
    }
}