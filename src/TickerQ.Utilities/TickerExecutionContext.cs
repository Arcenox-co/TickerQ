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
   public InternalFunctionContext[] Functions { get; private set; } = [];
   
   public void SetNextPlannedOccurrence(DateTime? occurrence)
      => NextPlannedOccurrence = occurrence;
   
   public void SetFunctions(InternalFunctionContext[] functions)
      => Functions = functions;
}