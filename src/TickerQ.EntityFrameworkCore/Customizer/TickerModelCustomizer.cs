using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Customizer
{
    internal class TickerModelCustomizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public TickerModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            var schema = context.GetService<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>().Schema;

            builder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>(schema));
            builder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>(schema));
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>(schema));

            base.Customize(builder, context);
        }
    }
}