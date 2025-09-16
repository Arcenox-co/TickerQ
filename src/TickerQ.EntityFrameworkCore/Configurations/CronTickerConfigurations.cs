using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerConfigurations<TCronTicker> : IEntityTypeConfiguration<TCronTicker>
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly string _schema;

        public CronTickerConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<TCronTicker> builder)
        {
            builder.HasKey("Id");
                
            builder.Property(e => e.Id)
                .ValueGeneratedNever();

            builder.HasIndex("Expression")
                .HasDatabaseName("IX_CronTickers_Expression");
            
            builder.HasIndex("Function","Expression", "Request")
                .HasDatabaseName("IX_Function_Expression_Request")
                .IsUnique();

            builder.ToTable("CronTickers", _schema);
        }
    }
}