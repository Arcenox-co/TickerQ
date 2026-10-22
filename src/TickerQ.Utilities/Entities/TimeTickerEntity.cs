using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    public class TimeTickerEntity : TimeTickerEntity<TimeTickerEntity>
    { }

    public class TimeTickerEntity<TTicker> : BaseTickerEntity where TTicker : TimeTickerEntity<TTicker>
    {
        // Conventional backing field keeps EF field-access materialization/fixup working.
        // 0 = untouched, 1 = initializing/assigning, 2 = explicitly initialized.
        // This also preserves Children = null without allocating a lock for every job.
        private ICollection<TTicker> _children;
        private int _childrenState;

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
        /// <summary>Last durable progress value reported by the active execution (0-100).</summary>
        [JsonInclude]
        public virtual double? ProgressPercent { get; internal set; }
        /// <summary>Optional human-readable progress detail, limited to 500 characters.</summary>
        [JsonInclude]
        public virtual string ProgressMessage { get; internal set; }
        [JsonInclude]
        public virtual DateTime? ProgressUpdatedAt { get; internal set; }
        public virtual int Retries { get; set; }
        [JsonInclude]
        public virtual int RetryCount { get; internal set; }
        public virtual int[] RetryIntervals { get; set; }
        [JsonInclude]
        public virtual Guid? ParentId { get; internal set; }
        [JsonIgnore]
        public virtual TTicker Parent { get; internal set; }
        public virtual ICollection<TTicker> Children
        {
            get
            {
                var children = Volatile.Read(ref _children);
                return children ?? InitializeChildren();
            }
            set
            {
                var spinner = new SpinWait();
                while (true)
                {
                    var state = Volatile.Read(ref _childrenState);
                    if (state != 1 && Interlocked.CompareExchange(ref _childrenState, 1, state) == state)
                        break;
                    spinner.SpinOnce();
                }
                Volatile.Write(ref _children, value);
                Volatile.Write(ref _childrenState, 2);
            }
        }

        private ICollection<TTicker> InitializeChildren()
        {
            var spinner = new SpinWait();
            while (true)
            {
                var state = Interlocked.CompareExchange(ref _childrenState, 1, 0);
                if (state == 2)
                    return Volatile.Read(ref _children);
                if (state == 0)
                {
                    try
                    {
                        // EF can populate the backing field directly.
                        var children = _children ?? new List<TTicker>();
                        Volatile.Write(ref _children, children);
                        Volatile.Write(ref _childrenState, 2);
                        return children;
                    }
                    catch
                    {
                        Volatile.Write(ref _childrenState, 0);
                        throw;
                    }
                }
                spinner.SpinOnce();
            }
        }

        // Internal read-only traversal must not initialize an empty collection for every
        // standalone job. Respect overridden navigation accessors on custom/proxy entities.
        internal ICollection<TTicker> GetChildrenForRead()
        {
            if (GetType() != typeof(TimeTickerEntity))
                return Children;
            var children = Volatile.Read(ref _children);
            if (children != null)
                return children;
            var state = Volatile.Read(ref _childrenState);
            return state == 0 ? Array.Empty<TTicker>()
                : state == 2 ? Volatile.Read(ref _children) : Children;
        }

        internal void ResetChildrenForSnapshot()
        {
            // A snapshot may have copied an initialization in progress on the source.
            _childrenState = 0;
            if (GetType() == typeof(TimeTickerEntity))
            {
                _children = null;
            }
            else
            {
                // Keep the virtual setter contract for custom entity navigations.
                Children = new List<TTicker>();
            }
        }

        // Preserve derived entity fields while rebuilding query relationships without
        // exposing or changing the provider's live navigation collections.
        internal TTicker CreateQuerySnapshot()
        {
            var snapshot = (TTicker)MemberwiseClone();
            snapshot.ResetChildrenForSnapshot();
            snapshot.Parent = null;
            return snapshot;
        }
        public virtual RunCondition? RunCondition { get; set; }
        /// <summary>
        /// Root-level policy for explicit deletion, chain replacement, and retention.
        /// Restrict preserves the whole chain until every descendant reaches a terminal status. Descendant values are ignored.
        /// Existing jobs default to Cascade. This does not change job execution or cancellation.
        /// </summary>
        public virtual ChainDeleteBehavior DeleteBehavior { get; set; } = ChainDeleteBehavior.Cascade;
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
        /// <summary>Origin schedule for every node in a recurring chain; null for ordinary time tickers.</summary>
        public virtual Guid? OriginCronTickerId { get; set; }
        /// <summary>
        /// Generation shared by every descendant of the currently acquired root. Unlike
        /// <see cref="AcquisitionToken"/>, this remains after root completion so delayed
        /// child writes can still be rejected after the root is rerun.
        /// </summary>
        [JsonInclude]
        public virtual Guid? ChainGeneration { get; set; }
        /// <summary>
        /// Selected step for a durable in-place chain retry. Stored on the root so stale recovery
        /// can rebuild the same pass-through execution graph after a process interruption.
        /// </summary>
        [JsonInclude]
        public virtual Guid? ChainRetryFromId { get; set; }
        /// <summary>The root outcome that existed before the current durable chain retry.</summary>
        [JsonInclude]
        public virtual TickerStatus? ChainRetryPreviousStatus { get; set; }
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
