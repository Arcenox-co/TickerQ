using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TickerQ.EntityFrameworkCore.Configurations.Base
{
    internal class TickerModelCostumizer : RelationalModelCustomizer
    {
        public TickerModelCostumizer(ModelCustomizerDependencies dependencies)
           : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            modelBuilder.ApplyConfiguration(new CronTickerConfiguration());
            modelBuilder.ApplyConfiguration(new TimeTickerConfiguration());
            modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfiguration());

            base.Customize(modelBuilder, context);
        }
    }
}
