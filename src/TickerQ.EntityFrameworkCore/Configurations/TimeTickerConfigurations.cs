using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class TimeTickerConfigurations : IEntityTypeConfiguration<TimeTickerEntity>
    {
        private readonly string _schema;

        public TimeTickerConfigurations(string schema = Constants.DefaultSchema)
        {
            _schema = schema;
        }
        
        public void Configure(EntityTypeBuilder<TimeTickerEntity> builder)
        {
            builder.HasKey("Id");

            builder.Property(x => x.LockHolder)
                .IsConcurrencyToken()
                .IsRequired(false);

            builder.HasOne(e => e.ParentJob)
                .WithMany(x => x.ChildJobs)
                .HasForeignKey(x => x.BatchParent)
                .OnDelete(DeleteBehavior.Restrict);
            
            builder.HasIndex("ExecutionTime")
                .HasDatabaseName("IX_TimeTicker_ExecutionTime");

            builder.HasIndex("Status", "ExecutionTime")
                .HasDatabaseName("IX_TimeTicker_Status_ExecutionTime");

            builder.ToTable("TimeTickers", _schema);
        }
    }
}