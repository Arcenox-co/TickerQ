using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    internal class TickerModelCustomizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer
        where TTimeTicker : TimeTickerEntity where TCronTicker : CronTickerEntity
    {
        public TickerModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            builder.ApplyConfiguration(new TimeTickerConfigurations());
            builder.ApplyConfiguration(new CronTickerConfigurations());
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations());

            base.Customize(builder, context);
        }
    }
}