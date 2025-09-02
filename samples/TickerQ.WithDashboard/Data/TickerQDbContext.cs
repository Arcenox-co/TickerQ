using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Configurations;

namespace TickerQ.WithDashboard.Data
{
    public class TickerQDbContext : DbContext
    {
        public TickerQDbContext(DbContextOptions<TickerQDbContext> options) : base(options)
        {
        }

        // TickerQ entities
        public DbSet<TimeTickerEntity> TimeTickers { get; set; }
        public DbSet<CronTickerEntity> CronTickers { get; set; }
        public DbSet<CronTickerOccurrenceEntity<CronTickerEntity>> CronTickerOccurrences { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Apply TickerQ entity configurations explicitly (needed for migrations)
            // Default schema is "ticker"
            builder.ApplyConfiguration(new TimeTickerConfigurations());
            builder.ApplyConfiguration(new CronTickerConfigurations());
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations());
        }
    }
}
