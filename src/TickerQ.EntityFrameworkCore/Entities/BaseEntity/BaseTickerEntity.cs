using System;

namespace TickerQ.EntityFrameworkCore.Entities.BaseEntity
{
    public class BaseTickerEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Function { get; set; }
        public string Description { get; set; }
        public string InitIdentifier { get; internal set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
