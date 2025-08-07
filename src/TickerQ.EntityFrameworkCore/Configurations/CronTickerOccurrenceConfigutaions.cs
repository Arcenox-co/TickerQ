using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerOccurrenceConfigurations : IEntityTypeConfiguration<CronTickerOccurrenceEntity<CronTickerEntity>>
    {
        private readonly string _schema;

        public CronTickerOccurrenceConfigurations(string schema = Constants.DefaultSchema)
        {
            _schema = schema;
        }
        
        public void Configure(EntityTypeBuilder<CronTickerOccurrenceEntity<CronTickerEntity>> builder)
        {
            builder.HasKey("Id");

            builder.HasIndex("CronTickerId")
                .HasName("IX_CronTickerOccurrence_CronTickerId");

            builder.HasIndex("ExecutionTime")
                .HasName("IX_CronTickerOccurrence_ExecutionTime");

            builder.HasIndex("Status", "ExecutionTime")
                .HasName("IX_CronTickerOccurrence_Status_ExecutionTime");

            builder.HasOne(x => x.CronTicker)
                .WithMany()
                .HasForeignKey("CronTickerId")
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex("CronTickerId", "ExecutionTime")
                .IsUnique()
                .HasName("UQ_CronTickerId_ExecutionTime");

            builder.ToTable("CronTickerOccurrences", _schema);
        }
    }
}