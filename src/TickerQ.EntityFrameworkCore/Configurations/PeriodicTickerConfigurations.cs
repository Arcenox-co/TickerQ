using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class PeriodicTickerConfigurations<TPeriodicTicker> : IEntityTypeConfiguration<TPeriodicTicker>
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private readonly string _schema;

        public PeriodicTickerConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<TPeriodicTicker> builder)
        {
            builder.HasKey("Id");

            builder.Property(e => e.Id)
                .ValueGeneratedNever();

            builder.Property(e => e.Interval)
                .IsRequired();

            builder.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            // Index for the scheduler scan: only active rows in the StartTime/EndTime window
            // are candidates for the next due execution.
            builder.HasIndex(nameof(PeriodicTickerEntity.IsActive), nameof(PeriodicTickerEntity.LastExecutedAt))
                .HasDatabaseName("IX_PeriodicTicker_Active_LastExecutedAt");

            builder.HasIndex(nameof(PeriodicTickerEntity.Function))
                .HasDatabaseName("IX_PeriodicTicker_Function");

            builder.ToTable("PeriodicTickers", _schema);
        }
    }
}

