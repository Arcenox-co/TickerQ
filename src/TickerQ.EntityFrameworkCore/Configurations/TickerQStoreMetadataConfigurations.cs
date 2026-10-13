using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

public sealed class TickerQStoreMetadataConfigurations : IEntityTypeConfiguration<TickerQStoreMetadata>
{
    private readonly string _schema;
    public TickerQStoreMetadataConfigurations(string schema = Constants.DefaultSchema) => _schema = schema;

    public void Configure(EntityTypeBuilder<TickerQStoreMetadata> builder)
    {
        builder.HasKey(x => new { x.ApplicationNamespaceKey, x.Id });
        builder.Property(x => x.ApplicationNamespaceKey).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.LastMigrationId).HasMaxLength(200).IsRequired(false);
        builder.Property(x => x.ActivationCheckpoint).HasMaxLength(200).IsRequired(false);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.ToTable("TickerQStoreMetadata", _schema);
    }
}
