using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Utilities
{
    public delegate Task TickerFunctionDelegate(CancellationToken cancellationToken, IServiceProvider serviceProvider, TickerFunctionContext context);

    public static class TickerFunctionProvider
    {
        private static MergeReadOnlyDictionary<string, (string, Type)> _tickerFunctionRequestTypes;
        internal static IReadOnlyDictionary<string, (string, Type)> TickerFunctionRequestTypes => _tickerFunctionRequestTypes;

        private static MergeReadOnlyDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate)> _tickerFunctions;
        public static IReadOnlyDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate)> TickerFunctions => _tickerFunctions;

        public static void RegisterFunctions(IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions)
        {
            _tickerFunctions = MergeDictionaries(_tickerFunctions, functions, preserveExisting: true);
        }

        public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes)
        {
            _tickerFunctionRequestTypes = MergeDictionaries(_tickerFunctionRequestTypes, requestTypes, preserveExisting: true);
        }

        internal static void MapCronExpressionsFromIConfigurations(IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions)
        {
            _tickerFunctions = MergeDictionaries(_tickerFunctions, functions, preserveExisting: false);
        }

        private static MergeReadOnlyDictionary<TKey, TValue> MergeDictionaries<TKey, TValue>(
            MergeReadOnlyDictionary<TKey, TValue> existing,
            IDictionary<TKey, TValue> incoming,
            bool preserveExisting = true)
        {
            if (existing == null)
            {
                existing = new MergeReadOnlyDictionary<TKey, TValue>(incoming);
            }
            else if (preserveExisting)
            {
                foreach (var kvp in incoming)
                {
                    existing.Dictionary.TryAdd(kvp.Key, kvp.Value);
                }
            }
            else
            {
                foreach (var kvp in incoming)
                {
                    existing.Dictionary[kvp.Key] = kvp.Value;
                }
            }

            return existing;
        }

        /// <summary>
        /// A ReadOnlyDictionary that exposes its internal dictionary for merging purposes.
        /// Used so that we don't have to create a new dictionary every time we want to add new items.
        /// </summary>
        private class MergeReadOnlyDictionary<TKey, TValue> : ReadOnlyDictionary<TKey, TValue>
        {
            internal MergeReadOnlyDictionary(IDictionary<TKey, TValue> dictionary) : base(dictionary)
            { }

            internal new IDictionary<TKey, TValue> Dictionary => base.Dictionary;
        }
    }

    public static class TickerRequestProvider
    {
        public static async Task<T> GetRequestAsync<T>(TickerFunctionContext context, CancellationToken cancellationToken)
        {
            var internalTickerManager = context.ServiceScope.ServiceProvider.GetService<IInternalTickerManager>();
            return await internalTickerManager.GetRequestAsync<T>(context.Id, context.Type, cancellationToken);
        }
    }
}