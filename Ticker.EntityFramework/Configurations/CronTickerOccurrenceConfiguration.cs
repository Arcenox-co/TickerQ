using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerOccurrenceConfiguration : IEntityTypeConfiguration<CronTickerOccurrence>
    {
        public void Configure(EntityTypeBuilder<CronTickerOccurrence> builder)
        {
            builder.HasKey(x => x.Id);

            Relations(builder);

            builder.ToTable("CronTickerOccurrences", "ticker");
        }

        private static void Relations(EntityTypeBuilder<CronTickerOccurrence> builder)
        {
            builder.HasOne(x => x.CronTicker)
                .WithMany(x => x.CronTickerOccurences)
                .HasForeignKey(x => x.CronTickerId);
        }
    }
}
