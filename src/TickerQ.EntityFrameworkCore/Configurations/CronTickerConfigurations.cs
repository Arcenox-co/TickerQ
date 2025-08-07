using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerConfigurations : IEntityTypeConfiguration<CronTickerEntity>
    {
        private readonly string _schema;

        public CronTickerConfigurations(string schema = Constants.DefaultSchema)
        {
            _schema = schema;
        }

        public void Configure(EntityTypeBuilder<CronTickerEntity> builder)
        {
            builder.HasKey("Id");

            builder.HasIndex("Expression")
                .HasName("IX_CronTickers_Expression");

            builder.ToTable("CronTickers", _schema);
        }
    }
}