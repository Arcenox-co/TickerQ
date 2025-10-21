using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class TimeTickerConfigurations : TimeTickerConfigurations<TimeTickerEntity>
    {
    }

    public class TimeTickerConfigurations<TTimeTickerEntity> : IEntityTypeConfiguration<TTimeTickerEntity>
        where TTimeTickerEntity : TimeTickerEntity
    {
        private readonly string _schema;
        private readonly string _tableName;

        public TimeTickerConfigurations(string schema = Constants.DefaultSchema, string tableName = "TimeTickers")
        {
            _schema = schema;
            _tableName = tableName;
        }

        public virtual void Configure(EntityTypeBuilder<TTimeTickerEntity> builder)
        {
            builder.HasKey("Id");

            builder.Property(x => x.LockHolder)
                .IsConcurrencyToken()
                .IsRequired(false);

            builder.HasOne<TTimeTickerEntity>("ParentJob")
                .WithMany("ChildJobs")
                .HasForeignKey(x => x.BatchParent)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex("ExecutionTime")
                .HasName($"IX_{_tableName}_ExecutionTime");

            builder.HasIndex("Status", "ExecutionTime")
                .HasName($"IX_{_tableName}_Status_ExecutionTime");

            builder.ToTable(_tableName, _schema);
        }
    }
}
