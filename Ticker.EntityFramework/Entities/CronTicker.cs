using System.Collections.Generic;
using TickerQ.EntityFrameworkCore.Entities.BaseEntity;

namespace TickerQ.EntityFrameworkCore.Entities
{
    public class CronTicker : BaseTickerEntity
    {
        public string Expression { get; set; }
        public byte[] Request { get; set; }
        public ICollection<CronTickerOccurrence> CronTickerOccurences { get; set; }
    }
}
