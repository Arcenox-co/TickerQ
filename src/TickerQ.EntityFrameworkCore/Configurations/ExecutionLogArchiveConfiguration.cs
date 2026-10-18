using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Configurations;

/// <summary>Apply explicitly when using a manually configured application DbContext.</summary>
public sealed class ExecutionLogArchiveConfiguration(string schema = "ticker") : IEntityTypeConfiguration<ExecutionLogArchive>
{
    public void Configure(EntityTypeBuilder<ExecutionLogArchive> builder)
    {
        builder.ToTable("ExecutionLogs", schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.PartitionKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.HasIndex(x => new { x.PartitionKey, x.ExpiresAtUtcTicks });
        builder.HasIndex(x => new { x.PartitionKey, x.UpdatedAtUtcTicks, x.Id });
    }
}
