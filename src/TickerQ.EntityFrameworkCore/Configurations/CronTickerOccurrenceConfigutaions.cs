using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerOccurrenceConfigurations : CronTickerOccurrenceConfigurations<CronTickerEntity>
    {
    }

    public class CronTickerOccurrenceConfigurations<TCronTickerEntity> : IEntityTypeConfiguration<CronTickerOccurrenceEntity<TCronTickerEntity>>
        where TCronTickerEntity : CronTickerEntity
    {
        private readonly string _schema;
        private readonly string _tableName;

        public CronTickerOccurrenceConfigurations(string schema = Constants.DefaultSchema, string tableName = "CronTickerOccurrences")
        {
            _schema = schema;
            _tableName = tableName;
        }

        public virtual void Configure(EntityTypeBuilder<CronTickerOccurrenceEntity<TCronTickerEntity>> builder)
        {
            builder.HasKey("Id");

            builder.Property(x => x.LockHolder)
                .IsConcurrencyToken()
                .IsRequired(false);

            builder.HasIndex("CronTickerId")
                .HasName($"IX_{_tableName}_CronTickerId");

            builder.HasIndex("ExecutionTime")
                .HasName($"IX_{_tableName}_ExecutionTime");

            builder.HasIndex("Status", "ExecutionTime")
                .HasName($"IX_{_tableName}_Status_ExecutionTime");

            builder.HasOne(x => x.CronTicker)
                .WithMany()
                .HasForeignKey("CronTickerId")
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex("CronTickerId", "ExecutionTime")
                .IsUnique()
                .HasName("UQ_CronTickerId_ExecutionTime");

            builder.ToTable(_tableName, _schema);
        }
    }
}
