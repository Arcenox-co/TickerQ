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
        /// Lease expiry for the node currently executing this ticker. Stamped when the
        /// ticker goes InProgress and renewed periodically by the running node; an
        /// InProgress ticker whose lease is in the past is considered stale (node dead).
        /// </summary>
        [JsonInclude]
        public virtual DateTime? LeaseUntil { get; set; }
        /// <summary>
        /// Unique marker for the immediate-acquisition invocation that most recently
        /// transitioned this ticker to InProgress. Used to disambiguate commit outcomes
        /// when a retrying relational provider reports a transient commit failure.
        /// </summary>
        [JsonInclude]
        public virtual Guid? AcquisitionToken { get; set; }
        /// <summary>The durable root of the execution graph containing this ticker.</summary>
        [JsonInclude]
        public virtual Guid? ChainRootId { get; set; }
        /// <summary>
        /// Generation shared by every descendant of the currently acquired root. Unlike
        /// <see cref="AcquisitionToken"/>, this remains after root completion so delayed
        /// child writes can still be rejected after the root is rerun.
        /// </summary>
        [JsonInclude]
        public virtual Guid? ChainGeneration { get; set; }
        /// <summary>What the stale-job watchdog does with this ticker if it goes stale.</summary>
        public virtual StaleAction OnStale { get; set; }
        [JsonInclude]
        public virtual int StaleRestartCount { get; set; }
        /// <summary>
        /// Max execution time per attempt, in seconds. Exceeding it cancels the
        /// execution and lands the ticker on Cancelled with a timeout reason.
        /// Null inherits <c>SchedulerOptionsBuilder.DefaultExecutionTimeout</c>;
        /// zero or negative disables the timeout for this ticker explicitly.
        /// </summary>
        public virtual int? TimeoutSeconds { get; set; }
    }
}