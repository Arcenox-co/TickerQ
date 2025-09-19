using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities;

internal class TickerExecutionContext
{
   internal Func<IServiceProvider, Task> ExternalProviderApplicationAction { get; set; }
   internal Action<IApplicationBuilder> DashboardApplicationAction { get; set; }
   public Action<object, CoreNotifyActionType> NotifyCoreAction { get; set; }
   public int MaxConcurrency { get; set; }
   public string InstanceIdentifier { get; set; }
   public static int ActiveThreads;
   public string LastHostExceptionMessage { get; set; }
   public TimeSpan TimeOutChecker { get; set; } = TimeSpan.FromMinutes(1);
   public DateTime? NextPlannedOccurrence { get; private set; }
   
   internal volatile InternalFunctionContext[] Functions = [];
   
   public void SetNextPlannedOccurrence(DateTime? occurrence)
      => NextPlannedOccurrence = occurrence;
   
   public void SetFunctions(ReadOnlySpan<InternalFunctionContext> functions)
   {
      var copy = new InternalFunctionContext[functions.Length];
      functions.CopyTo(copy.AsSpan());
      
      // Cache function delegates for performance optimization
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
      }
   }
}