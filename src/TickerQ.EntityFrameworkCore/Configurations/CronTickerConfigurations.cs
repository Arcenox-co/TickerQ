using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerConfigurations<TCronTicker> : IEntityTypeConfiguration<TCronTicker>
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly string _schema;

        public CronTickerConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<TCronTicker> builder)
        {
            builder.HasKey("Id");
                
            builder.Property(e => e.Id)
                .ValueGeneratedNever();

            builder.HasIndex("Expression")
                .HasDatabaseName("IX_CronTickers_Expression");

            // Index for common lookups by function + expression
            builder.HasIndex("Function", "Expression")
                .HasDatabaseName("IX_Function_Expression");

            builder.Property(e => e.IsEnabled)
                .IsRequired();

            // Auto-pause flag set by the worker-stream connect/disconnect
            // monitor. Defaults false so existing rows from older databases
            // act as "not paused" without needing data backfill.
            builder.Property(e => e.IsSystemPaused)
                .IsRequired()
                .HasDefaultValue(false);

            builder.ToTable("CronTickers", _schema);
        }
    }
}
