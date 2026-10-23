using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

public sealed class SchedulePolicyConfigurations(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<SchedulePolicyEntity>
{
    public void Configure(EntityTypeBuilder<SchedulePolicyEntity> builder)
    {
        builder.HasKey(x => new { x.ApplicationNamespaceKey, x.Id });
        builder.Property(x => x.ApplicationNamespaceKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.DefinitionJson).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.NameKey })
            .IsUnique().HasDatabaseName("UX_SchedulePolicies_Name");
        builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.UpdatedAtUtc })
            .HasDatabaseName("IX_SchedulePolicies_Updated");
        builder.ToTable("SchedulePolicies", schema);
    }
}
