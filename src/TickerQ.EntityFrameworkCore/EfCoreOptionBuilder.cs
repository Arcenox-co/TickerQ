using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore
{
    public class EfCoreOptionBuilder
    {
        internal bool UsesModelCustomizer { get; private set; }
        internal bool CancelMissedTickersOnReset { get; private set; }
        internal bool IgnoreSeedMemoryCronTickersInternal { get; private set; }
        public Func<object, Task> TimeSeeder { get; private set; }
        public Func<object, Task> CronSeeder  { get; private set; }
        public void UseModelCustomizerForMigrations()
            => UsesModelCustomizer = true;
        
        /// <summary>
        /// Will cancel missed tickers that are tied to this node on application start.
        /// </summary>
        public void CancelMissedTickersOnAppStart()
            => CancelMissedTickersOnReset = true;

        public void IgnoreSeedMemoryCronTickers()
            =>  IgnoreSeedMemoryCronTickersInternal = true;
        
        public void UseTickerSeeder<TTimeTicker, TCronTicker>(
            Func<ITimeTickerManager<TTimeTicker>, Task> timeTickerAsync,
            Func<ICronTickerManager<TCronTicker>, Task> cronTickerAsync)
            where TCronTicker : CronTicker, new()
            where TTimeTicker : TimeTicker, new()
        {
            TimeSeeder = async t => await timeTickerAsync((ITimeTickerManager<TTimeTicker>)t);
            CronSeeder = async c => await cronTickerAsync((ICronTickerManager<TCronTicker>)c);
        }

        public void UseTickerSeeder(
            Func<ITimeTickerManager<TimeTicker>, Task> timeTickerAsync,
            Func<ICronTickerManager<CronTicker>, Task> cronTickerAsync)
        {
            TimeSeeder = async t => await timeTickerAsync((ITimeTickerManager<TimeTicker>)t);
            CronSeeder = async c => await cronTickerAsync((ICronTickerManager<CronTicker>)c);
        }
    }
}