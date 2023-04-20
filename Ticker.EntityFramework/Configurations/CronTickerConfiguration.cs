using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerConfiguration : IEntityTypeConfiguration<CronTicker>
    {
        public void Configure(EntityTypeBuilder<CronTicker> builder)
        {
            builder.HasKey(x => x.Id);

            Relations(builder);

            builder.ToTable("CronTickers", "ticker");
        }

        private static void Relations(EntityTypeBuilder<CronTicker> builder)
        {
            builder.HasMany(x => x.CronTickerOccurences)
                .WithOne(x => x.CronTicker)
                .HasForeignKey(x => x.CronTickerId);
        }
    }
}
