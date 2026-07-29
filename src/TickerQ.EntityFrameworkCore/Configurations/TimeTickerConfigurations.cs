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
            builder.HasKey(x => x.Id);

            builder.Property(x => x.LockHolder)
                .IsRequired(false);

            builder.Property(x => x.AcquisitionToken)
                .IsRequired(false);

            builder.Property(x => x.ChainRootId)
                .IsRequired(false);
            builder.Property(x => x.ChainGeneration)
                .IsRequired(false);
            builder.HasIndex(x => x.ChainRootId)
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
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
            
            builder.HasIndex("ExecutionTime")
                .HasDatabaseName("IX_TimeTicker_ExecutionTime");

            // Index for scheduler queries: many tickers can share the same status/time
            builder.HasIndex("Status", "ExecutionTime")
                .HasDatabaseName("IX_TimeTicker_Status_ExecutionTime");

            // Index for retention sweeps: eligibility filters on terminal Status + ExecutedAt cutoff.
            // (ParentId is already indexed by the self-referencing FK convention, covering the chain BFS.)
            builder.HasIndex("Status", "ExecutedAt")
                .HasDatabaseName("IX_TimeTicker_Status_ExecutedAt");

            builder.ToTable("TimeTickers", _schema);
        }
    }
}
