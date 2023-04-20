using System;

namespace TickerQ.EntityFrameworkCore.Entities.BaseEntity
{
    public class BaseTickerEntity
    {
        public Guid Id { get; set; }
        public string Function { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
