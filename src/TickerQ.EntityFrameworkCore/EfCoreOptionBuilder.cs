using System;
using System.Linq;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore
{
    public class EfCoreOptionBuilder
    {
        internal Type TimeTickerType { get; set; }
        internal Type CronTickerType { get; set; }
        internal Type TimeTickerEntityType { get; set; }
        internal Type CronTickerEntityType { get; set; }
        internal bool UsesModelCustomizer { get; private set; }
        internal bool CancelMissedTickersOnReset { get; private set; }
        internal bool IgnoreSeedMemoryCronTickersInternal { get; private set; }
        internal Type TimeTickerMapperType { get; private set; }
        internal Type CronTickerMapperType { get; private set; }
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

        public void UseTimeTickerMapper<TTimeTickerMapper>()
            where TTimeTickerMapper: ITimeTickerMapper
        {
            TimeTickerMapperType = typeof(TTimeTickerMapper);
            ValidateMapperTypes(TimeTickerMapperType, typeof(ITimeTickerMapper<,>), TimeTickerType, TimeTickerEntityType);
        }

        public void UseCronTickerMapper<TCronTickerMapper>()
            where TCronTickerMapper: ICronTickerMapper
        {
            CronTickerMapperType = typeof(TCronTickerMapper);
            ValidateMapperTypes(CronTickerMapperType, typeof(ICronTickerMapper<,>), CronTickerType, CronTickerEntityType);
        }

        private static void ValidateMapperTypes(Type mapperType, Type genericTypeDefinition, params Type[] expectedGenericTypes)
        {
            var interfaceType = mapperType.GetInterfaces()
                .FirstOrDefault(o => o.IsGenericType && o.GetGenericTypeDefinition() == genericTypeDefinition)
                ?? throw new InvalidOperationException($"The mapper type {mapperType} must implement the correct interface: " + genericTypeDefinition);

            var genericArguments = interfaceType.GetGenericArguments();
            for (var i = 0; i < expectedGenericTypes.Length; i++)
            {
                if (expectedGenericTypes[i] != genericArguments[i])
                {
                    throw new InvalidOperationException(
                        $"The mapper type {mapperType} has incorrect generic argument type at position {i}. " +
                        $"Expected: {expectedGenericTypes[i]}, Actual: {genericArguments[i]}");
                }
            }
        }
    }
}
