using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

/// <summary>
/// The promise state table. <c>StateJson</c> holds up to 200 findings and 50 activity entries, so it has no length
/// limit; on Oracle <see cref="TickerProviderModelConventions"/> makes it an NCLOB, since an unbounded string would be
/// NVARCHAR2(2000) there.
/// </summary>
public sealed class TickerQPromiseConfigurations(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<TickerQPromiseEntity>
{
    public void Configure(EntityTypeBuilder<TickerQPromiseEntity> builder)
    {
        builder.HasKey(x => new { x.ApplicationNamespaceKey, x.Id });
        builder.Property(x => x.ApplicationNamespaceKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StateJson).IsRequired();
        builder.ToTable("TickerQPromises", schema);
    }
}
