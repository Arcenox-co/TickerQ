using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    internal class TickerModelCostumizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer where TTimeTicker : TimeTicker where TCronTicker : CronTicker
    {
        public TickerModelCostumizer(ModelCustomizerDependencies dependencies)
           : base(dependencies) { }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            builder.Entity<TTimeTicker>(timeTicker =>
            {
                timeTicker.ToTable("TimeTickers", "Ticker");
            });

            builder.Entity<TCronTicker>(timeTicker =>
            {
                timeTicker.ToTable("CronTickers", "Ticker");
            });

            builder.Entity<CronTickerOccurrence<TCronTicker>>(timeTicker =>
            {
                timeTicker.HasOne(x => x.CronTicker)
                    .WithMany()
                    .HasForeignKey(x => x.CronTickerId)
                    .OnDelete(DeleteBehavior.Cascade);

                timeTicker.ToTable("CronTickerOccurrences", "Ticker");
            });

            base.Customize(builder, context);
        }
    }
}
