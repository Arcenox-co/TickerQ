using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Customizer
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