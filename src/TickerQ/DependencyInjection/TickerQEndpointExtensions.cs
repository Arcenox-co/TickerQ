using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.DependencyInjection
{
    public static class TickerQEndpointExtensions
    {
        /// <summary>
        /// Registers a ticker function implemented via ITickerFunction or ITickerFunction&lt;TRequest&gt;.
        /// The type name becomes the function name. Configure with .WithCron(), .WithMaxConcurrency(), .WithPriority().
        /// The source generator handles DI and delegate registration at compile time.
        /// </summary>
        public static TickerFunctionBuilder<TFunction> MapTicker<TFunction>(this IHost host)
            where TFunction : class, ITickerFunctionBase
        {
            return new TickerFunctionBuilder<TFunction>(typeof(TFunction).Name);
        }

        /// <summary>
        /// Registers a lambda-based ticker function.
        /// </summary>
        public static TickerFunctionBuilder<object> MapTimeTicker(
            this IHost host,
            string functionName,
            Func<TickerFunctionContext, CancellationToken, Task> handler)
        {
            var dict = new System.Collections.Generic.Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [functionName] = (string.Empty, TickerTaskPriority.Normal, new TickerFunctionDelegate((ct, sp, ctx) => handler(ctx, ct)), 0)
            };
            TickerFunctionProvider.RegisterFunctions(dict);

            return new TickerFunctionBuilder<object>(functionName);
        }

        /// <summary>
        /// Registers a lambda-based ticker function with typed request.
        /// </summary>
        public static TickerFunctionBuilder<object> MapTimeTicker<TRequest>(
            this IHost host,
            string functionName,
            Func<TickerFunctionContext<TRequest>, CancellationToken, Task> handler)
        {
            var dict = new System.Collections.Generic.Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [functionName] = (string.Empty, TickerTaskPriority.Normal, new TickerFunctionDelegate(async (ct, sp, ctx) =>
                {
                    var request = await TickerRequestProvider.GetRequestAsync<TRequest>(ctx, ct);
                    var genericContext = new TickerFunctionContext<TRequest>(ctx, request);
                    await handler(genericContext, ct);
                }), 0)
            };
            TickerFunctionProvider.RegisterFunctions(dict);

            var requestTypes = new System.Collections.Generic.Dictionary<string, (string, Type)>
            {
                [functionName] = (typeof(TRequest).FullName, typeof(TRequest))
            };
            TickerFunctionProvider.RegisterRequestType(requestTypes);

            return new TickerFunctionBuilder<object>(functionName);
        }
    }
}
