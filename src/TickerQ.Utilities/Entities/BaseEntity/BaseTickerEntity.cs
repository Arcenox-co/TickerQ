using System;
using System.Text.Json.Serialization;

namespace TickerQ.Utilities.Entities.BaseEntity
{
    public class BaseTickerEntity
    {
        public virtual Guid Id { get; set; } = Guid.NewGuid();
        public virtual string Function { get; set; }
        public virtual string Description { get; set; }
        [JsonInclude]
        public virtual string InitIdentifier { get; protected internal set; }
        [JsonInclude]
        public virtual DateTime CreatedAt { get; protected internal set; } = DateTime.UtcNow;
        [JsonInclude]
        public virtual DateTime UpdatedAt { get; protected internal set; } = DateTime.UtcNow;
    }
}
