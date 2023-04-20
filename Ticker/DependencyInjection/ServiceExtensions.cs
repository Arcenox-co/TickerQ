using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities;

namespace TickerQ.DependencyInjection
{
    public static class ServiceExtensions
    {
        /// <summary>
        /// Adds Ticker to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="optionsBuilder"></param>
        /// <returns></returns>
        public static TickerOptionsBuilder AddTicker(this IServiceCollection services, Action<TickerOptionsBuilder> optionsBuilder = null)
        {
            var optionInstance = new TickerOptionsBuilder();

            if (optionsBuilder != default)
                optionsBuilder?.Invoke(optionInstance);

            if (optionInstance.Assemblies == default || optionInstance.Assemblies.Length == 0)
                optionInstance.SetAssemblies(Assembly.GetCallingAssembly());

            if (optionInstance.EfCoreConfigAction != default)
                optionInstance.SetUseEfCore(services);

            if(optionInstance.TickerHandlerService != default)
                services.AddScoped(typeof(ITickerExceptionHandler), optionInstance.TickerHandlerService);

            if(optionInstance.TimeZoneInfo != default)
                services.AddSingleton<IClock, SystemClock>(x => new SystemClock(optionInstance.TimeZoneInfo));
            else
                services.AddSingleton<IClock, SystemClock>();

            services.AddSingleton<TickerOptionsBuilder>(_ => optionInstance)
                    .AddSingleton<ITickerHost, TickerHost>()
                    .AddSingleton<TickerCollection>()
                    .AddSingleton<ITickerCollection, TickerCollection>();

            return optionInstance;
        }

        /// <summary>
        /// Use Ticker in the application.
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseTicker(this IApplicationBuilder app)
        {
            var tickerHost = app.ApplicationServices.GetRequiredService<ITickerHost>();

            tickerHost.Run();

            return app;
        }
    }
}
