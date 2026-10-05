using System;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Instrumentation;

namespace TickerQ.Instrumentation.OpenTelemetry
{
    public static class ServiceExtensions
    {
        /// <summary>Adds the TickerQ activity source to an OpenTelemetry tracer provider.</summary>
        public static TracerProviderBuilder AddTickerQInstrumentation(
            this TracerProviderBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.AddSource(OpenTelemetryInstrumentation.ActivitySourceName);
        }

        /// <summary>
        /// Adds OpenTelemetry instrumentation with activity tracing for TickerQ jobs.
        /// Also includes standard logging through ILogger.
        /// </summary>
        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOpenTelemetryInstrumentation<TTimeTicker, TCronTicker>(
            this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            tickerConfiguration.ExternalProviderConfigServiceAction += services =>
            {
                // Replace any existing instrumentation with OpenTelemetry version
                services.AddSingleton<ITickerQInstrumentation, OpenTelemetryInstrumentation>();
            };

            return tickerConfiguration;
        }
    }
}
