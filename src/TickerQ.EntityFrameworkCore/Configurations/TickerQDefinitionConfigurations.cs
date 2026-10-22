using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

public sealed class TickerQDefinitionConfigurations(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<TickerQDefinitionEntity>
{
    public void Configure(EntityTypeBuilder<TickerQDefinitionEntity> builder)
    {
        builder.HasKey(x => new { x.ApplicationNamespaceKey, x.Id });
        builder.Property(x => x.ApplicationNamespaceKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameKey).HasMaxLength(96).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.DefinitionJson).IsRequired();
        builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.Kind, x.Target, x.CreatedAtUtc })
            .HasDatabaseName("IX_TickerQDefinitions_List");
        builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.NameKey })
            .IsUnique()
            .HasDatabaseName("UX_TickerQDefinitions_Name");
        builder.ToTable("TickerQDefinitions", schema);
    }
}
