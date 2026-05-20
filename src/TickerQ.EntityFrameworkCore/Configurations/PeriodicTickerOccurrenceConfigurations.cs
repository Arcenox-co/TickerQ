using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class PeriodicTickerOccurrenceConfigurations<TPeriodicTicker>
        : IEntityTypeConfiguration<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>
        where TPeriodicTicker : PeriodicTickerEntity
    {
        private readonly string _schema;

        public PeriodicTickerOccurrenceConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> builder)
        {
            builder.HasKey("Id");

            builder.Property(e => e.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.LockHolder)
                .IsRequired(false);

            builder.HasIndex(nameof(PeriodicTickerOccurrenceEntity<TPeriodicTicker>.PeriodicTickerId))
                .HasDatabaseName("IX_PeriodicTickerOccurrence_PeriodicTickerId");

            builder.HasIndex(nameof(PeriodicTickerOccurrenceEntity<TPeriodicTicker>.ExecutionTime))
                .HasDatabaseName("IX_PeriodicTickerOccurrence_ExecutionTime");

            builder.HasIndex(nameof(PeriodicTickerOccurrenceEntity<TPeriodicTicker>.Status),
                              nameof(PeriodicTickerOccurrenceEntity<TPeriodicTicker>.ExecutionTime))
                .HasDatabaseName("IX_PeriodicTickerOccurrence_Status_ExecutionTime");

            builder.HasOne(x => x.PeriodicTicker)
                .WithMany()
                .HasForeignKey(x => x.PeriodicTickerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure at most one occurrence per (parent, scheduled time) — same idempotency
            // contract used for cron occurrences.
            builder.HasIndex(nameof(PeriodicTickerOccurrenceEntity<TPeriodicTicker>.PeriodicTickerId),
                              nameof(PeriodicTickerOccurrenceEntity<TPeriodicTicker>.ExecutionTime))
                .IsUnique()
                .HasDatabaseName("UQ_PeriodicTickerId_ExecutionTime");

            builder.ToTable("PeriodicTickerOccurrences", _schema);
        }
    }
}

