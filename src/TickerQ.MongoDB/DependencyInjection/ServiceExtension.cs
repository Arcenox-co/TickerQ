using System;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.MongoDB.DependencyInjection
{
    public static class ServiceExtension
    {
        /// <summary>
        /// Registers the MongoDB persistence provider for TickerQ. Call this from
        /// inside <c>AddTickerQ(options =&gt; ...)</c>.
        /// </summary>
        public static TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> AddOperationalStore(
            this TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> tickerConfiguration,
            Action<TickerQMongoOptionBuilder<TimeTickerEntity, CronTickerEntity>> mongoConfiguration = null)
            => AddOperationalStore<TimeTickerEntity, CronTickerEntity>(tickerConfiguration, mongoConfiguration);

        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOperationalStore<TTimeTicker, TCronTicker>(
            this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
            Action<TickerQMongoOptionBuilder<TTimeTicker, TCronTicker>> mongoConfiguration = null)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            var optionBuilder = new TickerQMongoOptionBuilder<TTimeTicker, TCronTicker>();
            mongoConfiguration?.Invoke(optionBuilder);

            if (optionBuilder.ConfigureServices is null)
                throw new InvalidOperationException(
                    "TickerQ.MongoDB: you must call UseTickerQMongoClient(...) or UseExistingMongoClient(...) on the option builder.");

            tickerConfiguration.ExternalProviderConfigServiceAction += optionBuilder.ConfigureServices;
            return tickerConfiguration;
        }
    }
}
