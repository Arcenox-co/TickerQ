using TickerQ.EntityFrameworkCore.Entities.BaseEntity;

namespace TickerQ.EntityFrameworkCore.Entities
{
    public class CronTickerEntity : BaseTickerEntity
    {
        public virtual string Expression { get; set; }
        public virtual byte[] Request { get; set; }
        public int Retries { get; set; }
        public int[] RetryIntervals { get; set; }
    }
}