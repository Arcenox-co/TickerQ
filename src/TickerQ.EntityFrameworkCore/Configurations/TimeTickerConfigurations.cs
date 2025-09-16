using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class TimeTickerConfigurations<TTimeTicker> : IEntityTypeConfiguration<TTimeTicker> where TTimeTicker : TimeTickerEntity, new()
    {
        private readonly string _schema;

        public TimeTickerConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<TTimeTicker> builder)
        {
            builder.HasKey("Id");

            builder.Property(x => x.LockHolder)
                .IsConcurrencyToken()
                .IsRequired(false);

            builder.HasOne<TTimeTicker>()
                .WithMany()
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
            
            builder.HasIndex("ExecutionTime")
                .HasDatabaseName("IX_TimeTicker_ExecutionTime");

            builder.HasIndex("Status", "ExecutionTime", "Request")
                .HasDatabaseName("IX_TimeTicker_Status_ExecutionTime")
                .IsUnique();

            builder.ToTable("TimeTickers", _schema);
        }
    }
}