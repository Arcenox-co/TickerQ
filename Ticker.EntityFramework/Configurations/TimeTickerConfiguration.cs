using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class TimeTickerConfiguration : IEntityTypeConfiguration<TimeTicker>
    {
        public void Configure(EntityTypeBuilder<TimeTicker> builder)
        {
            builder.HasKey(x => x.Id);

            builder.ToTable("TimeTickers", "ticker");
        }
    }
}
