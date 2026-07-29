using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

public static class TickerResultStorage
{
    public const int MaxPayloadBytes = 1024 * 1024;
}

public sealed class TimeTickerResultConfigurations<TTimeTicker>
    : IEntityTypeConfiguration<TimeTickerResultEntity<TTimeTicker>>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
{
    private readonly string _schema;
    public TimeTickerResultConfigurations(string schema = Constants.DefaultSchema) => _schema = schema;

    public void Configure(EntityTypeBuilder<TimeTickerResultEntity<TTimeTicker>> builder)
    {
        builder.HasKey(x => x.TickerId);
        ConfigureEnvelope(builder.Property(x => x.Payload), builder.Property(x => x.MediaType),
            builder.Property(x => x.ContractId), builder.Property(x => x.ContractType));
        builder.HasOne(x => x.Ticker).WithOne().HasForeignKey<TimeTickerResultEntity<TTimeTicker>>(x => x.TickerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("TimeTickerResults", _schema);
    }

    internal static void ConfigureEnvelope(
        PropertyBuilder<byte[]> payload, PropertyBuilder<string> mediaType,
        PropertyBuilder<string> contractId, PropertyBuilder<string> contractType)
    {
        payload.IsRequired().HasMaxLength(TickerResultStorage.MaxPayloadBytes);
        mediaType.IsRequired().HasMaxLength(256);
        contractId.IsRequired(false).HasMaxLength(512);
        contractType.IsRequired(false).HasMaxLength(1024);
    }
}

public sealed class CronTickerOccurrenceResultConfigurations<TCronTicker>
    : IEntityTypeConfiguration<CronTickerOccurrenceResultEntity<TCronTicker>>
    where TCronTicker : CronTickerEntity, new()
{
    private readonly string _schema;
    public CronTickerOccurrenceResultConfigurations(string schema = Constants.DefaultSchema) => _schema = schema;

    public void Configure(EntityTypeBuilder<CronTickerOccurrenceResultEntity<TCronTicker>> builder)
    {
        builder.HasKey(x => x.TickerId);
        TimeTickerResultConfigurations<TimeTickerEntity>.ConfigureEnvelope(
            builder.Property(x => x.Payload), builder.Property(x => x.MediaType),
            builder.Property(x => x.ContractId), builder.Property(x => x.ContractType));
        builder.HasOne(x => x.Occurrence).WithOne()
            .HasForeignKey<CronTickerOccurrenceResultEntity<TCronTicker>>(x => x.TickerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("CronTickerOccurrenceResults", _schema);
    }
}