using System.Text.Json.Serialization;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    public class CronTickerEntity : BaseTickerEntity
    {
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