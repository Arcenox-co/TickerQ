using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerOccurrenceConfigurations<TCronTicker> : IEntityTypeConfiguration<CronTickerOccurrenceEntity<TCronTicker>>
        where TCronTicker : CronTickerEntity
    {
        private readonly string _schema;

        public CronTickerOccurrenceConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;
        
        public void Configure(EntityTypeBuilder<CronTickerOccurrenceEntity<TCronTicker>> builder)
        {
            builder.HasKey("ApplicationNamespaceKey", "Id");
            builder.Property(e => e.ApplicationNamespaceKey).IsRequired().HasMaxLength(80);
            
            builder.Property(e => e.Id)
                .ValueGeneratedNever();
            
            builder.Property(x => x.LockHolder)
                .IsRequired(false);

            builder.Property(x => x.AcquisitionToken)
                .IsRequired(false);

            builder.Property(x => x.DefinitionRevision)
                .IsRequired()
                .HasDefaultValue(0L);

            builder.HasIndex("ApplicationNamespaceKey", "CronTickerId")
                .HasDatabaseName("IX_CronTickerOccurrence_CronTickerId");

            builder.HasIndex("ApplicationNamespaceKey", "ExecutionTime")
                .HasDatabaseName("IX_CronTickerOccurrence_ExecutionTime");

            builder.HasIndex("ApplicationNamespaceKey", "Status", "ExecutionTime")
                .HasDatabaseName("IX_CronTickerOccurrence_Status_ExecutionTime");

            // Index for retention sweeps: eligibility filters on terminal Status + ExecutedAt cutoff.
            builder.HasIndex("ApplicationNamespaceKey", "Status", "ExecutedAt")
                .HasDatabaseName("IX_CronTickerOccurrence_Status_ExecutedAt");

            builder.HasOne(x => x.CronTicker)
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationNamespaceKey, x.CronTickerId })
                .HasPrincipalKey(x => new { x.ApplicationNamespaceKey, x.Id })
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex("ApplicationNamespaceKey", "CronTickerId", "ExecutionTime")
                .IsUnique()
                .HasDatabaseName("UQ_CronTickerId_ExecutionTime");

            builder.HasIndex("ApplicationNamespaceKey", "CronTickerId", "DefinitionRevision", "Status")
                .HasDatabaseName("IX_CronOccurrence_DefinitionRevision_Status");

            builder.ToTable("CronTickerOccurrences", _schema);
        }
    }
}
