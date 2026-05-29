using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class PeriodicTickerConfigurations<TPeriodicTicker> : IEntityTypeConfiguration<TPeriodicTicker>
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private static readonly JsonSerializerOptions ChainJsonOptions = new JsonSerializerOptions();

        private readonly string _schema;

        public PeriodicTickerConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        private static string SerializeChain(PeriodicChainStep[] value)
            => value == null ? null : JsonSerializer.Serialize<PeriodicChainStep[]>(value, ChainJsonOptions);

        private static PeriodicChainStep[] DeserializeChain(string value)
            => string.IsNullOrEmpty(value) ? null : JsonSerializer.Deserialize<PeriodicChainStep[]>(value, ChainJsonOptions);

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

            // Persist the optional chain template as a JSON column, mirroring how complex value
            // collections are serialized elsewhere. A ValueComparer is supplied so EF change-tracking
            // treats the array by value (otherwise reference comparison would miss in-place edits).
            var jsonConverter = new ValueConverter<PeriodicChainStep[], string>(
                v => SerializeChain(v),
                v => DeserializeChain(v));

            var jsonComparer = new ValueComparer<PeriodicChainStep[]>(
                (a, b) => SerializeChain(a) == SerializeChain(b),
                v => v == null ? 0 : SerializeChain(v).GetHashCode(),
                v => DeserializeChain(SerializeChain(v)));

            builder.Property(e => e.ChainTemplate)
                .HasConversion(jsonConverter, jsonComparer);

            builder.Property(e => e.ChainOverlapBehavior)
                .HasConversion<int>()
                .IsRequired()
                .HasDefaultValue(Utilities.Enums.ChainOverlapBehavior.Allow);

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

