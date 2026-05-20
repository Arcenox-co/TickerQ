using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.EntityFrameworkCore
{
    public class TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        internal Action<IServiceCollection> ConfigureServices { get; set; }
        internal int PoolSize { get; set; } = 1024;
        internal string Schema { get; set; } = "ticker";

        // Periodic-ticker opt-in. Set via EnablePeriodic<T>(); consumed by ServiceBuilder
        // to register the EF-backed periodic persistence provider and customizer variant.
        internal bool PeriodicEnabled { get; private set; }
        internal Type PeriodicTickerType { get; private set; }

        /// <summary>
        /// Enables EF Core persistence for <see cref="PeriodicTickerEntity"/> using the default entity type.
        /// Must be paired with <c>tickerOptions.EnablePeriodic()</c> on the core
        /// <c>TickerOptionsBuilder</c>; this call wires the EF-backed provider for it.
        /// </summary>
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> EnablePeriodic()
            => EnablePeriodic<PeriodicTickerEntity>();

        /// <summary>
        /// Enables EF Core persistence for a custom <see cref="PeriodicTickerEntity"/> subclass.
        /// </summary>
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> EnablePeriodic<TPeriodicTicker>()
            where TPeriodicTicker : PeriodicTickerEntity, new()
        {
            PeriodicEnabled = true;
            PeriodicTickerType = typeof(TPeriodicTicker);
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseApplicationDbContext<TDbContext>(ConfigurationType configurationType) where TDbContext : DbContext
        {
            ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeTicker, TCronTicker>(this, configurationType);
            return this;
        }
        
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseTickerQDbContext<TDbContext>(Action<DbContextOptionsBuilder> optionsAction, string schema = null) where TDbContext : TickerQDbContext<TTimeTicker, TCronTicker>
        {
            Schema = schema ?? Schema;
            
            ServiceBuilder.UseTickerQDbContext<TDbContext, TTimeTicker, TCronTicker>(this, optionsAction);
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetDbContextPoolSize(int poolSize)
        {
            PoolSize = poolSize;
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetSchema(string schema)
        {
            Schema = schema;
            return this;
        }
    }
}
