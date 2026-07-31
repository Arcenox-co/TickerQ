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
        public virtual TickerStatus Status { get; protected internal set; }
        [JsonInclude]
        public virtual string LockHolder { get; protected internal set; }
        public virtual byte[] Request { get; set; }
        public virtual DateTime? ExecutionTime { get; set; }
        [JsonInclude]
        public virtual DateTime? LockedAt { get; protected internal set; }
        [JsonInclude]
        public virtual DateTime? ExecutedAt { get; protected internal set; }
        [JsonInclude]
        public virtual string ExceptionMessage { get; protected internal set; }
        [JsonInclude]
        public virtual string SkippedReason { get; protected internal set; }
        [JsonInclude]
        public virtual long ElapsedTime { get; protected internal set; }
        public virtual int Retries { get; set; }
        [JsonInclude]
        public virtual int RetryCount { get; protected internal set; }
        public virtual int[] RetryIntervals { get; set; }
        [JsonInclude]
        public virtual Guid? ParentId { get; protected internal set; }
        [JsonIgnore]
        public virtual TTicker Parent { get; protected internal set; }
        public virtual ICollection<TTicker> Children { get; set; } = new List<TTicker>();
        public virtual RunCondition? RunCondition { get; set; }
    }
}