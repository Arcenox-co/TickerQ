using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.DependencyInjection
{
    public static class TickerQEndpointExtensions
    {
        #region MapTicker (no group)

        /// <summary>
        /// Registers a ticker function via ITickerFunction (no request payload).
        /// Function name defaults to typeof(T).Name.
        /// </summary>
        public static TickerFunctionBuilder<TFunction> MapTicker<TFunction>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TFunction : class, ITickerFunction
        {
            return RegisterTickerFunction<TFunction>(services, null, null, lifetime,
                (ct, sp, ctx) =>
                {
                    var instance = sp.GetRequiredService<TFunction>();
                    return instance.ExecuteAsync(ctx, ct);
                });
        }

        /// <summary>
        /// Registers a ticker function via ITickerFunction&lt;TRequest&gt; (with typed request).
        /// </summary>
        public static TickerFunctionBuilder<TFunction> MapTicker<TFunction, TRequest>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TFunction : class, ITickerFunction<TRequest>
        {
            return RegisterTickerFunctionWithRequest<TFunction, TRequest>(services, null, null, lifetime);
        }

        #endregion

        #region MapTickerGroup

        /// <summary>
        /// Creates a ticker function group with a shared prefix. Functions registered in the group
        /// are named "GroupName.ClassName".
        /// </summary>
        public static TickerFunctionGroup MapTickerGroup(this IServiceCollection services, string groupName)
        {
            return new TickerFunctionGroup(services, groupName);
        }

        /// <summary>
        /// Creates a ticker function group with a callback for inline registration.
        /// </summary>
        public static IServiceCollection MapTickerGroup(this IServiceCollection services, string groupName, Action<TickerFunctionGroup> configure)
        {
            var group = new TickerFunctionGroup(services, groupName);
            configure(group);
            return services;
        }

        #endregion

        #region MapTimeTicker (lambda-based)

        /// <summary>
        /// Registers a lambda-based ticker function (no request payload).
        /// </summary>
        public static TickerFunctionBuilder<object> MapTimeTicker(
            this IServiceCollection services,
            string functionName,
            Func<TickerFunctionContext, CancellationToken, Task> handler)
        {
            TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [functionName] = (string.Empty, TickerTaskPriority.Normal, new TickerFunctionDelegate((ct, sp, ctx) => handler(ctx, ct)), 0)
            });

            return new TickerFunctionBuilder<object>(functionName);
        }

        /// <summary>
        /// Registers a lambda-based ticker function with typed request.
        /// </summary>
        public static TickerFunctionBuilder<object> MapTimeTicker<TRequest>(
            this IServiceCollection services,
            string functionName,
            Func<TickerFunctionContext<TRequest>, CancellationToken, Task> handler)
        {
            TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [functionName] = (string.Empty, TickerTaskPriority.Normal, new TickerFunctionDelegate(async (ct, sp, ctx) =>
                {
                    var genericContext = await TickerRequestProvider.ToGenericContextAsync<TRequest>(ctx, ct);
                    await handler(genericContext, ct);
                }), 0)
            });

            TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
            {
                [functionName] = (typeof(TRequest).FullName, typeof(TRequest))
            });

            return new TickerFunctionBuilder<object>(functionName);
        }

        #endregion

        #region Internal registration helpers

        internal static TickerFunctionBuilder<TFunction> RegisterTickerFunction<TFunction>(
            IServiceCollection services,
            string groupName,
            string nameOverride,
            ServiceLifetime lifetime,
            Func<CancellationToken, IServiceProvider, TickerFunctionContext, Task> handler)
            where TFunction : class
        {
            var name = BuildFunctionName<TFunction>(groupName, nameOverride);

            services.TryAdd(new ServiceDescriptor(typeof(TFunction), typeof(TFunction), lifetime));

            TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [name] = (string.Empty, TickerTaskPriority.Normal, new TickerFunctionDelegate(handler), 0)
            });

            TickerFunctionProvider.RegisterTypeMapping(typeof(TFunction), name);

            return new TickerFunctionBuilder<TFunction>(name);
        }

        internal static TickerFunctionBuilder<TFunction> RegisterTickerFunctionWithRequest<TFunction, TRequest>(
            IServiceCollection services,
            string groupName,
            string nameOverride,
            ServiceLifetime lifetime)
            where TFunction : class, ITickerFunction<TRequest>
        {
            var name = BuildFunctionName<TFunction>(groupName, nameOverride);

            services.TryAdd(new ServiceDescriptor(typeof(TFunction), typeof(TFunction), lifetime));

            TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [name] = (string.Empty, TickerTaskPriority.Normal, new TickerFunctionDelegate(async (ct, sp, ctx) =>
                {
                    var instance = sp.GetRequiredService<TFunction>();
                    var genericContext = await TickerRequestProvider.ToGenericContextAsync<TRequest>(ctx, ct);
                    await instance.ExecuteAsync(genericContext, ct);
                }), 0)
            });

            TickerFunctionProvider.RegisterRequestType(new Dictionary<string, (string, Type)>
            {
                [name] = (typeof(TRequest).FullName, typeof(TRequest))
            });

            TickerFunctionProvider.RegisterTypeMapping(typeof(TFunction), name);

            return new TickerFunctionBuilder<TFunction>(name);
        }

        private static string BuildFunctionName<T>(string groupName, string nameOverride)
        {
            var typeName = nameOverride ?? typeof(T).Name;
            return string.IsNullOrEmpty(groupName) ? typeName : $"{groupName}.{typeName}";
        }

        #endregion
    }

    /// <summary>
    /// A group of ticker functions with a shared name prefix and default configuration.
    /// </summary>
    public sealed class TickerFunctionGroup
    {
        private readonly IServiceCollection _services;
        private readonly string _groupName;
        private TickerTaskPriority _defaultPriority = TickerTaskPriority.Normal;
        private int _defaultMaxConcurrency;
        private ServiceLifetime _defaultLifetime = ServiceLifetime.Scoped;

        internal TickerFunctionGroup(IServiceCollection services, string groupName)
        {
            _services = services;
            _groupName = groupName;
        }

        /// <summary>
        /// Sets the default priority for all functions in this group.
        /// </summary>
        public TickerFunctionGroup WithPriority(TickerTaskPriority priority)
        {
            _defaultPriority = priority;
            return this;
        }

        /// <summary>
        /// Sets the default max concurrency for all functions in this group.
        /// </summary>
        public TickerFunctionGroup WithMaxConcurrency(int maxConcurrency)
        {
            _defaultMaxConcurrency = maxConcurrency;
            return this;
        }

        /// <summary>
        /// Sets the default DI lifetime for all functions in this group.
        /// </summary>
        public TickerFunctionGroup WithLifetime(ServiceLifetime lifetime)
        {
            _defaultLifetime = lifetime;
            return this;
        }

        /// <summary>
        /// Registers a ticker function (no request) in this group.
        /// Name: "GroupName.ClassName" or "GroupName.CustomName" if overridden.
        /// </summary>
        public TickerFunctionBuilder<TFunction> MapTicker<TFunction>(string nameOverride = null)
            where TFunction : class, ITickerFunction
        {
            var builder = TickerQEndpointExtensions.RegisterTickerFunction<TFunction>(
                _services, _groupName, nameOverride, _defaultLifetime,
                (ct, sp, ctx) =>
                {
                    var instance = sp.GetRequiredService<TFunction>();
                    return instance.ExecuteAsync(ctx, ct);
                });

            if (_defaultMaxConcurrency > 0)
                builder.WithMaxConcurrency(_defaultMaxConcurrency);
            if (_defaultPriority != TickerTaskPriority.Normal)
                builder.WithPriority(_defaultPriority);

            return builder;
        }

        /// <summary>
        /// Registers a ticker function with typed request in this group.
        /// </summary>
        public TickerFunctionBuilder<TFunction> MapTicker<TFunction, TRequest>(string nameOverride = null)
            where TFunction : class, ITickerFunction<TRequest>
        {
            var builder = TickerQEndpointExtensions.RegisterTickerFunctionWithRequest<TFunction, TRequest>(
                _services, _groupName, nameOverride, _defaultLifetime);

            if (_defaultMaxConcurrency > 0)
                builder.WithMaxConcurrency(_defaultMaxConcurrency);
            if (_defaultPriority != TickerTaskPriority.Normal)
                builder.WithPriority(_defaultPriority);

            return builder;
        }
    }
}
