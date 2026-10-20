using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

public sealed class TickerQNodeConfigurations(string schema = Constants.DefaultSchema) : IEntityTypeConfiguration<TickerQNodeEntity>
{
    public void Configure(EntityTypeBuilder<TickerQNodeEntity> builder)
    {
        builder.ToTable("TickerQNodes", schema);
        builder.HasKey(x => new { x.ApplicationNamespaceKey, x.NodeKey });
        builder.Property(x => x.ApplicationNamespaceKey).HasMaxLength(80);
        builder.Property(x => x.NodeKey).HasMaxLength(64);
        builder.Property(x => x.ExecutionOwnerId).HasMaxLength(512).IsRequired();
        builder.Property(x => x.NodeIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DashboardUrl).HasMaxLength(2048).IsRequired(false);
        builder.Property(x => x.Version).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.LastSeenAtUtc });
    }
}

public sealed class TickerQControlCommandConfigurations(string schema = Constants.DefaultSchema) : IEntityTypeConfiguration<TickerQControlCommandEntity>
{
    public void Configure(EntityTypeBuilder<TickerQControlCommandEntity> builder)
    {
        builder.ToTable("TickerQControlCommands", schema);
        builder.HasKey(x => new { x.ApplicationNamespaceKey, x.Id });
        builder.Property(x => x.ApplicationNamespaceKey).HasMaxLength(80);
        builder.Property(x => x.TargetNodeKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetExecutionOwnerId).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(512).IsRequired(false);
        builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.TargetNodeKey, x.Status, x.RequestedAtUtc });
        builder.HasIndex(x => new { x.ApplicationNamespaceKey, x.ExpiresAtUtc });
    }
}
