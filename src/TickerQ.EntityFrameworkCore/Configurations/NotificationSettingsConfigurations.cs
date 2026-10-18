using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

public sealed class NotificationSettingsConfigurations(string schema = Constants.DefaultSchema) : IEntityTypeConfiguration<NotificationSettingsEntity>
{
    public void Configure(EntityTypeBuilder<NotificationSettingsEntity> builder)
    {
        builder.ToTable("NotificationSettings", schema);
        builder.HasKey(x => x.ApplicationNamespaceKey);
        builder.Property(x => x.ApplicationNamespaceKey).HasMaxLength(80);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.ProtectedConfiguration).IsRequired();
    }
}
