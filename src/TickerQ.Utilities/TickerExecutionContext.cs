using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities;

internal interface ITickerOptionsSeeding
{
    bool SeedDefinedCronTickers { get; }
    Func<IServiceProvider, System.Threading.Tasks.Task> TimeSeederAction { get; }
    Func<IServiceProvider, System.Threading.Tasks.Task> CronSeederAction { get; }
}

internal class TickerExecutionContext
{
   private long _nextOccurrenceTicks;
   internal Action<IServiceProvider> ExternalProviderApplicationAction { get; set; }
   internal Action<IApplicationBuilder> DashboardApplicationAction { get; set; }
   public Action<object, CoreNotifyActionType> NotifyCoreAction { get; set; }
   public string LastHostExceptionMessage { get; set; }
   internal ITickerOptionsSeeding OptionsSeeding { get; set; }
   
   internal volatile InternalFunctionContext[] Functions = [];
   
   public void SetNextPlannedOccurrence(DateTime? dt) =>
      Interlocked.Exchange(ref _nextOccurrenceTicks, dt?.Ticks ?? -1);
   
   public DateTime? GetNextPlannedOccurrence()
   {
      var t = Interlocked.Read(ref _nextOccurrenceTicks);
      return t < 0 ? null : new DateTime(t, DateTimeKind.Utc);
   }
   
   public void SetFunctions(ReadOnlySpan<InternalFunctionContext> functions)
   {
      var copy = new InternalFunctionContext[functions.Length];
      functions.CopyTo(copy.AsSpan());
      
      CacheFunctionReferences(copy.AsSpan());
      Functions = copy;
   }

   private static void CacheFunctionReferences(Span<InternalFunctionContext> functions)
   {
      for (var i = 0; i < functions.Length; i++)
      {
         ref var context = ref functions[i];
         if (TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var tickerItem))
         {
            context.CachedDelegate = tickerItem.Delegate;
            context.CachedPriority = tickerItem.Priority;
         }
      
         if (context.TimeTickerChildren is { Count: > 0 })
         {
            var childrenSpan = CollectionsMarshal.AsSpan(context.TimeTickerChildren);
            CacheFunctionReferences(childrenSpan);
         }
      }
   }
}