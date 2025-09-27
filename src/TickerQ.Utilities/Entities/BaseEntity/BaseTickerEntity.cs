using System;

namespace TickerQ.Utilities.Entities.BaseEntity
{
    public class BaseTickerEntity
    {
        public virtual Guid Id { get; set; } = Guid.NewGuid();
        public virtual string Function { get; set; }
        public virtual string Description { get; set; }
        public virtual string InitIdentifier { get; internal set; }
        public virtual DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;
        public virtual DateTime UpdatedAt { get; internal set; } = DateTime.UtcNow;
    }
}
