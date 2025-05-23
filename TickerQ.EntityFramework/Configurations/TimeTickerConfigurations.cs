using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class TimeTickerConfigurations : IEntityTypeConfiguration<TimeTickerEntity>
    {
        public void Configure(EntityTypeBuilder<TimeTickerEntity> builder)
        {
          
            builder.HasKey("Id");

            builder.HasIndex("ExecutionTime")
                    .HasName("IX_TimeTicker_ExecutionTime");

            builder.HasIndex("Status", "ExecutionTime")
                    .HasName("IX_TimeTicker_Status_ExecutionTime");

            builder.ToTable("TimeTickers", "ticker");
        }
    }
}