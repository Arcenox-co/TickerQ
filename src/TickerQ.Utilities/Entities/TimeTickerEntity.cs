using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    public class TimeTickerEntity : TimeTickerEntity<TimeTickerEntity>
    { }

    public class TimeTickerEntity<TTicker> : BaseTickerEntity where TTicker : TimeTickerEntity<TTicker>
    {
        [JsonInclude]
        public virtual TickerStatus Status { get; internal set; }
        [JsonInclude]
        public virtual string LockHolder { get; internal set; }
        public virtual byte[] Request { get; set; }
        public virtual DateTime? ExecutionTime { get; set; }
        [JsonInclude]
        public virtual DateTime? LockedAt { get; internal set; }
        [JsonInclude]
        public virtual DateTime? ExecutedAt { get; internal set; }
        [JsonInclude]
        public virtual string ExceptionMessage { get; internal set; }
        [JsonInclude]
        public virtual string SkippedReason { get; internal set; }
        [JsonInclude]
        public virtual long ElapsedTime { get; internal set; }
        public virtual int Retries { get; set; }
        [JsonInclude]
        public virtual int RetryCount { get; internal set; }
        public virtual int[] RetryIntervals { get; set; }
        [JsonInclude]
        public virtual Guid? ParentId { get; internal set; }
        [JsonIgnore]
        public virtual TTicker Parent { get; internal set; }
        public virtual ICollection<TTicker> Children { get; set; } = new List<TTicker>();
        public virtual RunCondition? RunCondition { get; set; }

        /// <summary>
        /// When this time ticker was materialized as part of a periodic ticker's chain (Variant A
        /// job-chaining), this is the id of the originating periodic ticker. Set on every node of the
        /// materialized chain so <see cref="Enums.ChainOverlapBehavior.Skip"/> can detect a still-running
        /// previous chain by querying persisted state — which works across nodes and process restarts,
        /// unlike the previous in-memory-only tracking. Null for ordinary (non-periodic) time tickers.
        /// </summary>
        [JsonInclude]
        public virtual Guid? OriginPeriodicTickerId { get; internal set; }
    }
}