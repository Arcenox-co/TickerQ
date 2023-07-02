using System.Collections.Generic;
using TickerQ.EntityFrameworkCore.Entities.BaseEntity;

namespace TickerQ.EntityFrameworkCore.Entities
{
    public class CronTicker : BaseTickerEntity
    {
        public virtual string Expression { get; set; }
        public virtual byte[] Request { get; set; }
    }
}
