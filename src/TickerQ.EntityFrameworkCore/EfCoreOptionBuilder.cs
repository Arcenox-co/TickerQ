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
        internal bool SeedDefinedCronTickers { get; private set; } = true;
        internal Func<ITimeTickerManager<TTimeTicker>, Task> TimeSeeder { get; private set; }
        internal Func<ICronTickerManager<TCronTicker>, Task> CronSeeder  { get; private set; }
        internal Action<IServiceCollection> ConfigureServices { get; set; }
        internal int PoolSize { get; set; } = 1024;
        internal string Schema { get; set; } = "ticker";
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> IgnoreSeedDefinedCronTickers()
        {
            SeedDefinedCronTickers = false;
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(Func<ITimeTickerManager<TTimeTicker>, Task> timeTickerAsync, Func<ICronTickerManager<TCronTicker>, Task> cronTickerAsync)
        {
            TimeSeeder = async t => await timeTickerAsync(t);
            CronSeeder = async c => await cronTickerAsync(c);
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseApplicationDbContext<TDbContext>(ConfigurationType configurationType) where TDbContext : DbContext
        {
            ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeTicker, TCronTicker>(this, configurationType);
            return this;
        }
        
        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseTickerQDbContext<TDbContext>(Action<DbContextOptionsBuilder> optionsAction, string schema = null) where TDbContext : TickerQDbContext<TTimeTicker, TCronTicker>
        {
            if(string.IsNullOrEmpty(schema))
                schema = Schema;
            
            ServiceBuilder.UseTickerQDbContext<TDbContext, TTimeTicker, TCronTicker>(this, optionsAction);
            return this;
        }

        public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetDbContextPoolSize(int poolSize)
        {
            PoolSize = poolSize;
            return this;
        }
    }
}