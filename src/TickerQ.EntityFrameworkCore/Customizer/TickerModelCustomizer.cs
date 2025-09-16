using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Customizer
{
    internal class TickerModelCustomizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer
        where TTimeTicker : TimeTickerEntity, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public TickerModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            builder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>());
            builder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>());
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>());

            base.Customize(builder, context);
        }
    }
}