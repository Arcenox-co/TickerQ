using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities;

internal interface ITickerOptionsSeeding
{
    bool SeedDefinedCronTickers { get; }
    bool RegisterBackgroundServices => true;
    long ReconciliationEpoch => 0;
    string ApplicationNamespace => null;
    IReadOnlyDictionary<string, string> LegacyDefinedCronOwnership => null;
    bool AdoptAllLegacyDefinedCronTickers => false;
    LegacyRuntimePartitionAdoption LegacyRuntimePartitionAdoption => null;
    Func<IServiceProvider, CancellationToken, System.Threading.Tasks.Task> TimeSeederAction { get; }
    Func<IServiceProvider, CancellationToken, System.Threading.Tasks.Task> CronSeederAction { get; }
}

internal class TickerExecutionContext
{
   private long _nextOccurrenceTicks;
   internal Action<IServiceProvider> ExternalProviderApplicationAction { get; set; }
   internal Action<object> DashboardApplicationAction { get; set; }
   internal string LicenseCertificatePath { get; set; }
   public Action<object, CoreNotifyActionType> NotifyCoreAction { get; set; }
   public string LastHostExceptionMessage { get; set; }
   internal ITickerOptionsSeeding OptionsSeeding { get; set; }
   internal TickerQRuntimePartition RuntimePartition { get; set; } = TickerQRuntimePartition.LegacyGlobal;
   
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

   internal static void CacheFunctionReferences(Span<InternalFunctionContext> functions)
   {
      var pending = new Stack<InternalFunctionContext>();
      for (var i = 0; i < functions.Length; i++)
         pending.Push(functions[i]);

      while (pending.TryPop(out var context))
      {
         if (TickerChainInfrastructure.IsParallelGroup(context.FunctionName))
         {
            context.CachedDelegate = TickerChainInfrastructure.ParallelGroupDelegate;
            context.CachedPriority = TickerTaskPriority.Normal;
            context.CachedMaxConcurrency = 0;
         }
         else if (TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var tickerItem))
         {
            context.CachedDelegate = tickerItem.Delegate;
            context.CachedPriority = tickerItem.Priority;
            context.CachedMaxConcurrency = tickerItem.MaxConcurrency;
         }

         if (context.TimeTickerChildren is { Count: > 0 })
         {
            foreach (var child in context.TimeTickerChildren) pending.Push(child);
         }
      }
   }
}
