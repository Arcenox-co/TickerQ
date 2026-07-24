using System;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.MongoDB.DependencyInjection;

namespace TickerQ.MongoDB
{
    public class TickerQMongoOptionBuilder<TTimeTicker, TCronTicker>
        where TTimeTicker : global::TickerQ.Utilities.Entities.TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : global::TickerQ.Utilities.Entities.CronTickerEntity, new()
    {
        internal Action<IServiceCollection> ConfigureServices { get; set; }
        internal string CollectionPrefix { get; set; } = "ticker_";

        /// <summary>
        /// Configure TickerQ to own its own <see cref="MongoDB.Driver.IMongoClient"/> built from
        /// <paramref name="connectionString"/>. The client is registered as a singleton.
        /// </summary>
        public TickerQMongoOptionBuilder<TTimeTicker, TCronTicker> UseTickerQMongoClient(string connectionString, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException("databaseName is required", nameof(databaseName));

            ServiceBuilder.UseOwnedClient<TTimeTicker, TCronTicker>(this, connectionString, databaseName);
            return this;
        }

        /// <summary>
        /// Reuse an <see cref="MongoDB.Driver.IMongoClient"/> already registered in DI.
        /// </summary>
        public TickerQMongoOptionBuilder<TTimeTicker, TCronTicker> UseExistingMongoClient(string databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException("databaseName is required", nameof(databaseName));

            ServiceBuilder.UseExistingClient<TTimeTicker, TCronTicker>(this, databaseName);
            return this;
        }

        /// <summary>
        /// Override the prefix prepended to collection names. Defaults to <c>"ticker_"</c>,
        /// producing <c>ticker_TimeTickers</c>, <c>ticker_CronTickers</c>, <c>ticker_CronTickerOccurrences</c>.
        /// </summary>
        public TickerQMongoOptionBuilder<TTimeTicker, TCronTicker> SetCollectionPrefix(string prefix)
        {
            CollectionPrefix = prefix ?? string.Empty;
            return this;
        }
    }
}
