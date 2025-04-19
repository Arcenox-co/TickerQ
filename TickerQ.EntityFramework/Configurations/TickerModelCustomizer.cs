using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    internal class TickerModelCustomizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer
        where TTimeTicker : TimeTicker where TCronTicker : CronTicker
    {
        public TickerModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            builder.Entity<TTimeTicker>(timeTicker =>
            {
                timeTicker.HasKey("Id");

                timeTicker.HasIndex("ExecutionTime")
                    .HasName("IX_TimeTicker_ExecutionTime");

                timeTicker.HasIndex("Status", "ExecutionTime")
                    .HasName("IX_TimeTicker_Status_ExecutionTime");

                timeTicker.ToTable("TimeTickers", "ticker");
            });

            builder.Entity<TCronTicker>(cronTicker =>
            {
                cronTicker.HasKey("Id");

                cronTicker.HasIndex("Expression")
                    .HasName("IX_CronTickers_Expression");

                cronTicker.ToTable("CronTickers", "ticker");
            });

            builder.Entity<CronTickerOccurrence<TCronTicker>>(cronTickerOccurrence =>
            {
                cronTickerOccurrence.HasKey("Id");

                cronTickerOccurrence.HasIndex("CronTickerId")
                    .HasName("IX_CronTickerOccurrence_CronTickerId");

                cronTickerOccurrence.HasIndex("ExecutionTime")
                    .HasName("IX_CronTickerOccurrence_ExecutionTime");

                cronTickerOccurrence.HasIndex("Status", "ExecutionTime")
                    .HasName("IX_CronTickerOccurrence_Status_ExecutionTime");

                cronTickerOccurrence.HasOne(x => x.CronTicker)
                    .WithMany()
                    .HasForeignKey("CronTickerId")
                    .OnDelete(DeleteBehavior.Cascade);

                cronTickerOccurrence.ToTable("CronTickerOccurrences", "ticker");
            });

            base.Customize(builder, context);
        }
    }
}