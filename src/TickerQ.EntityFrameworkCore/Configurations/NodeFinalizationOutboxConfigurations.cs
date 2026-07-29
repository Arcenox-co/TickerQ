using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Configurations;

public sealed class NodeFinalizationOutboxConfigurations : IEntityTypeConfiguration<NodeFinalizationOutboxEntity>
{
    private readonly string _schema;
    public NodeFinalizationOutboxConfigurations(string schema = Constants.DefaultSchema) => _schema = schema;

    public void Configure(EntityTypeBuilder<NodeFinalizationOutboxEntity> builder)
    {
        builder.HasKey(x => x.OutboxId);
        builder.HasIndex(x => new { x.TickerType, x.TickerId, x.AcquisitionToken, x.DispatchId, x.NodeEpoch })
            .IsUnique();
        builder.HasIndex(x => new { x.AvailableAtUtc, x.OutboxId });

        builder.Property(x => x.FinalizeUri).IsRequired().HasMaxLength(NodeFinalizationIntent.MaxUriLength);
        builder.Property(x => x.FinalizePathAndQuery).IsRequired().HasMaxLength(NodeFinalizationIntent.MaxPathAndQueryLength);
        builder.Property(x => x.ExactBody).IsRequired().HasMaxLength(NodeFinalizationIntent.MaxExactBodyBytes);
        builder.Property(x => x.TerminalMutationDigest).IsRequired().HasMaxLength(32);
        builder.Property(x => x.ClaimedBy).IsRequired(false).HasMaxLength(NodeFinalizationClaim.MaxClaimedByLength);
        builder.Property(x => x.LastErrorCode).IsRequired(false).HasMaxLength(NodeFinalizationOperationalState.MaxErrorCodeLength);
        builder.ToTable("NodeFinalizationOutbox", _schema);
    }
}
