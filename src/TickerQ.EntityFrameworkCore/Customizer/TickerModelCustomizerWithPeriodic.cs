using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Customizer
{
    /// <summary>
    /// Variant of <see cref="TickerModelCustomizer{TTimeTicker, TCronTicker}"/> that additionally
    /// registers <see cref="PeriodicTickerEntity"/> mappings. Selected when periodic support is
    /// enabled via <c>EnablePeriodic&lt;TPeriodicTicker&gt;()</c>.
    /// </summary>
    internal class TickerModelCustomizerWithPeriodic<TTimeTicker, TCronTicker, TPeriodicTicker> : RelationalModelCustomizer
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        public TickerModelCustomizerWithPeriodic(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            string schema;
            try
            {
                schema = context.GetService<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>()?.Schema ?? Constants.DefaultSchema;
            }
            catch (InvalidOperationException)
            {
                schema = Constants.DefaultSchema;
            }

            builder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>(schema));
            builder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>(schema));
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>(schema));
            builder.ApplyConfiguration(new PeriodicTickerConfigurations<TPeriodicTicker>(schema));
            builder.ApplyConfiguration(new PeriodicTickerOccurrenceConfigurations<TPeriodicTicker>(schema));

            base.Customize(builder, context);
        }
    }
}

