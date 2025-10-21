using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerConfigurations : CronTickerConfigurations<CronTickerEntity>
    {
    }

    public class CronTickerConfigurations<TCronTicker> : IEntityTypeConfiguration<CronTickerEntity>
        where TCronTicker : CronTickerEntity
    {
        private readonly string _schema;
        private readonly string _tableName;

        public CronTickerConfigurations(string schema = Constants.DefaultSchema, string tableName = "CronTickers")
        {
            _schema = schema;
            _tableName = tableName;
        }

        public virtual void Configure(EntityTypeBuilder<CronTickerEntity> builder)
        {
            builder.HasKey("Id");

            builder.HasIndex("Expression")
                .HasName($"IX_{_tableName}_Expression");

            builder.ToTable(_tableName, _schema);
        }
    }
}
