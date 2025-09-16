using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.EntityFrameworkCore
{
    public class EfCoreOptionBuilder<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity, new()
        where TCronTicker : CronTickerEntity, new()
    {
        internal bool SeedDefinedCronTickers { get; private set; } = true;
        internal Func<ITimeTickerManager<TTimeTicker>, Task> TimeSeeder { get; private set; }
        internal Func<ICronTickerManager<TCronTicker>, Task> CronSeeder  { get; private set; }
        internal Action<IServiceCollection> ConfigureServices { get; set; }
        internal int PoolSize { get; set; } = 32;
        public EfCoreOptionBuilder<TTimeTicker, TCronTicker> IgnoreSeedDefinedCronTickers()
        {
            SeedDefinedCronTickers = false;
            return this;
        }

        public EfCoreOptionBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(Func<ITimeTickerManager<TTimeTicker>, Task> timeTickerAsync, Func<ICronTickerManager<TCronTicker>, Task> cronTickerAsync)
        {
            TimeSeeder = async t => await timeTickerAsync(t);
            CronSeeder = async c => await cronTickerAsync(c);
            return this;
        }

        public EfCoreOptionBuilder<TTimeTicker, TCronTicker> UseDbContext<TDbContext>(ConfigurationType configurationType) where TDbContext : DbContext
        {
            CustomizerServiceDescriptor.UseDbContext<TDbContext, TTimeTicker, TCronTicker>(this, configurationType);
            return this;
        }

        public EfCoreOptionBuilder<TTimeTicker, TCronTicker> SetDbContextPoolSize(int poolSize)
        {
            PoolSize = poolSize;
            return this;
        }
    }
}