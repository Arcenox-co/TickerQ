using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Customizer
{
    internal class TickerModelCustomizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer
        where TTimeTicker : TimeTickerEntity
        where TCronTicker : CronTickerEntity
    {
        public TickerModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            if (typeof(TTimeTicker) != typeof(TimeTickerEntity))
            {
                // When a custom TimeTickerEntity is used, ignore the base entity to avoid EF Core mapping
                // TimeTickerEntity as an entity due to BatchParent and ChildJobs navigation properties.
                builder.Ignore<TimeTickerEntity>();
            }

            builder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>());
            builder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>());
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>());

            base.Customize(builder, context);
        }
    }
}
