using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TickerQ.Functions;
using TickerQ.Functions.Delegates;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities;

namespace TickerQ
{
    internal class TickerCollection : ITickerCollection
    {
        internal IReadOnlyDictionary<string, (TickerFunctionDelegate Delegate, TickerTaskPriority Priority)> TickerFunctionsDelegate { get; private set; }
        internal IReadOnlyDictionary<string, string> MemoryCronExpressions { get; private set; }

        public TickerCollection(TickerOptionsBuilder tickerOptionsBuilder, IConfiguration configuration)
        {
            var (tickerFunctionsDelegate, memoryCronExpressions) = FunctionFactory.CollectFunctions(configuration, tickerOptionsBuilder.Assemblies);

            TickerFunctionsDelegate = new ReadOnlyDictionary<string, (TickerFunctionDelegate Delegate, TickerTaskPriority TaskPriority)>(tickerFunctionsDelegate);

            if (!tickerOptionsBuilder.UseEfCore)
                MemoryCronExpressions = new ReadOnlyDictionary<string, string>(memoryCronExpressions);
        }

        public bool ExistFunction(string function)
        {
            if (string.IsNullOrWhiteSpace(function))
                return false;

            return TickerFunctionsDelegate.ContainsKey(function);
        }

        public IReadOnlyCollection<(string Function, string CronExpression)> GetMemoryTickers()
        {
            if (TickerFunctionsDelegate == null)
                return new (string, string)[0];

            var functions = MemoryCronExpressions.Select(x => (x.Key, x.Value)).ToList();

            return new ReadOnlyCollection<(string, string)>(functions);
        }

        public IReadOnlyCollection<(string Function, TickerTaskPriority TaskPriority)> GetTickerFunctions()
        {
            var functions = TickerFunctionsDelegate.Select(x => (x.Key, x.Value.Priority)).ToList();

            return new ReadOnlyCollection<(string, TickerTaskPriority)>(functions);
        }
    }
}
