using System.Text.Json.Serialization;
using TickerQ.Utilities.Entities.BaseEntity;

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
    }
}