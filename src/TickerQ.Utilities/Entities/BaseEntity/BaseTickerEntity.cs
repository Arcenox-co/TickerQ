using System;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities.BaseEntity
{
    public class BaseTickerEntity
    {
        internal BaseTickerEntity CreateShallowSnapshot() => (BaseTickerEntity)MemberwiseClone();
        [JsonIgnore]
        public virtual string ApplicationNamespaceKey { get; set; } = TickerQRuntimePartition.LegacyGlobal.StorageKey;
        public virtual Guid Id { get; set; } = Guid.NewGuid();
        public virtual string Function { get; set; }
        public virtual string Description { get; set; }
        [JsonInclude]
        public virtual string InitIdentifier { get; internal set; }
        [JsonInclude]
        public virtual DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;
        [JsonInclude]
        public virtual DateTime UpdatedAt { get; internal set; } = DateTime.UtcNow;

        /// <summary>
        /// Contract version accepted when this ticker's request payload was persisted. Nullable so
        /// rows written by older TickerQ versions remain readable and can follow the legacy drift policy.
        /// </summary>
        [JsonInclude]
        public virtual int? RequestContractVersion { get; set; }

        /// <summary>Canonical request-schema fingerprint accepted at persistence time, when available.</summary>
        [JsonInclude]
        public virtual string RequestContractFingerprint { get; set; }

        /// <summary>Optional named availability policy evaluated before the root is scheduled.</summary>
        public virtual string SchedulePolicyName { get; set; }

        /// <summary>Policy version observed when this ticker was created or last edited.</summary>
        [JsonInclude]
        public virtual long? SchedulePolicyVersion { get; internal set; }

        /// <summary>Action taken when <see cref="SchedulePolicyName"/> blocks a requested instant.</summary>
        public virtual BlockedOccurrenceBehavior? BlockedOccurrenceBehavior { get; set; }

        /// <summary>Action taken when this root is acquired outside its allowed start window.</summary>
        public virtual MissedExecutionAction MissedExecutionAction { get; set; } = MissedExecutionAction.RunOnce;

        /// <summary>Maximum delay before <see cref="MissedExecutionAction.Skip"/> applies.</summary>
        public virtual TimeSpan? MaximumLateness { get; set; }
    }
}
