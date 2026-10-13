using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class TimeTickerConfigurations<TTimeTicker> : IEntityTypeConfiguration<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        private readonly string _schema;

        public TimeTickerConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<TTimeTicker> builder)
        {
            builder.HasKey(x => new { x.ApplicationNamespaceKey, x.Id });
            builder.Property(x => x.ApplicationNamespaceKey).IsRequired().HasMaxLength(80);

            builder.Property(x => x.LockHolder)
                .IsRequired(false);

            builder.Property(x => x.AcquisitionToken)
                .IsRequired(false);

            builder.Property(x => x.ChainRootId)
                .IsRequired(false);
            builder.Property(x => x.ChainGeneration)
                .IsRequired(false);
            builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.ChainRootId })
                .HasDatabaseName("IX_TimeTicker_ChainRootId");

            builder.Property(x => x.RequestContractVersion)
                .IsRequired(false);

            builder.Property(x => x.RequestContractFingerprint)
                .HasMaxLength(128)
                .IsRequired(false);
            
            builder.Property(x => x.ExecutionTime)
                .IsRequired(false);

            builder.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => new { x.ApplicationNamespaceKey, x.ParentId })
                .HasPrincipalKey(x => new { x.ApplicationNamespaceKey, x.Id })
                .OnDelete(DeleteBehavior.NoAction);
            
            builder.HasIndex("ApplicationNamespaceKey", "ExecutionTime")
                .HasDatabaseName("IX_TimeTicker_ExecutionTime");

            // Index for scheduler queries: many tickers can share the same status/time
            builder.HasIndex("ApplicationNamespaceKey", "Status", "ExecutionTime")
                .HasDatabaseName("IX_TimeTicker_Status_ExecutionTime");

            // Index for retention sweeps: eligibility filters on terminal Status + ExecutedAt cutoff.
            // (ParentId is already indexed by the self-referencing FK convention, covering the chain BFS.)
            builder.HasIndex("ApplicationNamespaceKey", "Status", "ExecutedAt")
                .HasDatabaseName("IX_TimeTicker_Status_ExecutedAt");

            builder.ToTable("TimeTickers", _schema);
        }
    }
}
