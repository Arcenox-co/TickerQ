using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Utilities
{
    public delegate Task TickerFunctionDelegate(CancellationToken cancellationToken, IServiceProvider serviceProvider, TickerFunctionContext context);

    /// <summary>
    /// Provider for managing ticker functions and their request types using FrozenDictionary.
    /// Uses a callback-based approach to collect all registrations and create a single optimized FrozenDictionary.
    /// </summary>
    public static class TickerFunctionProvider
    {
        // Callback actions to collect registrations
        private static Action<Dictionary<string, (string, Type)>> _requestTypeRegistrations;
        private static Action<Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate)>> _functionRegistrations;
        
        // Final frozen dictionaries
        public static FrozenDictionary<string, (string, Type)> TickerFunctionRequestTypes;
        public static FrozenDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate)> TickerFunctions;

        /// <summary>
        /// Registers ticker functions during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="functions">The functions to register. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
        public static void RegisterFunctions(IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions)
        {
            if (functions == null)
                throw new ArgumentNullException(nameof(functions));
            
            if (functions.Count == 0)
                return;

            _functionRegistrations += dict =>
            {
                foreach (var (key, value) in functions)
                {
                    dict.TryAdd(key, value); // Preserves existing entries
                }
            };
        }

        /// <summary>
        /// Registers ticker functions with capacity hint during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="functions">The functions to register. Cannot be null.</param>
        /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
        /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
        public static void RegisterFunctions(IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions, int _)
        {
            // For callback approach, capacity is calculated automatically in Build()
            RegisterFunctions(functions);
        }

        /// <summary>
        /// Registers request types during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="requestTypes">The request types to register. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
        public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes)
        {
            if (requestTypes == null)
                throw new ArgumentNullException(nameof(requestTypes));
            
            if (requestTypes.Count == 0)
                return;

            _requestTypeRegistrations += dict =>
            {
                foreach (var (key, value) in requestTypes)
                {
                    dict.TryAdd(key, value); // Preserves existing entries
                }
            };
        }

        /// <summary>
        /// Registers request types with capacity hint during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="requestTypes">The request types to register. Cannot be null.</param>
        /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
        /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
        public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes, int _)
        {
            // For callback approach, capacity is calculated automatically in Build()
            RegisterRequestType(requestTypes);
        }

        /// <summary>
        /// Updates cron expressions for registered functions by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="configuration">IConfiguration to update based on path</param>
        /// <exception cref="ArgumentNullException">Thrown when cronUpdates parameter is null.</exception>
        internal static void UpdateCronExpressionsFromIConfiguration(IConfiguration configuration)
        {
            _functionRegistrations += dict =>
            {
                foreach (var (key, value) in dict)
                {
                    if (value.cronExpression.StartsWith('%'))
                    {
                        var configKey = value.cronExpression.Trim('%');
                        var mappedCronExpression = configuration[configKey];
            
                        if (!string.IsNullOrEmpty(mappedCronExpression))
                        {
                            dict[key] = (mappedCronExpression, value.Priority, value.Delegate);
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Builds the final FrozenDictionaries by executing all callbacks with optimal capacity.
        /// Uses a single-pass approach: directly creates optimally-sized dictionaries and populates them.
        /// This method should be called once after all registration is complete.
        /// After calling this method, no more registrations should be made.
        /// </summary>
        public static void Build()
        {
            // Build functions dictionary
            if (_functionRegistrations != null)
            {
                // Single pass: execute callbacks directly on final dictionary
                var functionsDict = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate)>();
                _functionRegistrations(functionsDict);
                TickerFunctions = functionsDict.ToFrozenDictionary();
                _functionRegistrations = null; // Release callback chain
            }
            else
            {
                TickerFunctions = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate)>()
                    .ToFrozenDictionary();
            }

            // Build request types dictionary  
            if (_requestTypeRegistrations != null)
            {
                // Single pass: execute callbacks directly on final dictionary
                var requestTypesDict = new Dictionary<string, (string, Type)>();
                _requestTypeRegistrations(requestTypesDict);
                TickerFunctionRequestTypes = requestTypesDict.ToFrozenDictionary();
                _requestTypeRegistrations = null; // Release callback chain
            }
            else
            {
                TickerFunctionRequestTypes = new Dictionary<string, (string, Type)>()
                    .ToFrozenDictionary();
            }
        }
    }

    public static class TickerRequestProvider
    {
        public static async Task<T> GetRequestAsync<T>(TickerFunctionContext context, CancellationToken cancellationToken)
        {
            try
            {
                var internalTickerManager = context.ServiceScope.ServiceProvider.GetService<IInternalTickerManager>();
                return await internalTickerManager.GetRequestAsync<T>(context.Id, context.Type, cancellationToken);
            }
            catch (Exception e)
            {
                var logger = context.ServiceScope.ServiceProvider.GetService<ITickerQInstrumentation>();
                
                logger.LogRequestDeserializationFailure(typeof(T).FullName, context.FunctionName, context.Id, context.Type, e);
            }
            
            return default;
        }
    }
}