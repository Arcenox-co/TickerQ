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
        public virtual TickerStatus Status { get; internal set; }
        public virtual string LockHolder { get; internal set; }
        public virtual byte[] Request { get; set; }
        public virtual DateTime? ExecutionTime { get; set; }
        public virtual DateTime? LockedAt { get; internal set; }
        public virtual DateTime? ExecutedAt { get; internal set; }
        public virtual string ExceptionMessage { get; internal set; }
        public virtual string SkippedReason { get; internal set; }
        public virtual long ElapsedTime { get; internal set; }
        public virtual int Retries { get; set; }
        public virtual int RetryCount { get; internal set; }
        public virtual int[] RetryIntervals { get; set; }
        public virtual Guid? ParentId { get; internal set; }
        [JsonIgnore]
        public virtual TTicker Parent { get; internal set; }
        public virtual ICollection<TTicker> Children { get; internal set; } = new List<TTicker>();
        public virtual RunCondition? RunCondition { get; internal set; }
    }
}