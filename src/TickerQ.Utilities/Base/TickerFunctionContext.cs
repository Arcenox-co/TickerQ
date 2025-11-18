using System;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Base;

public class TickerFunctionContext<TRequest> : TickerFunctionContext
{
    public TickerFunctionContext(TickerFunctionContext tickerFunctionContext, TRequest request) 
    {
        Request = request;
        Id = tickerFunctionContext.Id;
        Type = tickerFunctionContext.Type;
        RetryCount = tickerFunctionContext.RetryCount;
        IsDue = tickerFunctionContext.IsDue;
        RequestCancelOperationAction = tickerFunctionContext.RequestCancelOperationAction;
        CronOccurrenceOperations = tickerFunctionContext.CronOccurrenceOperations;
        FunctionName = tickerFunctionContext.FunctionName;
    }

    public readonly TRequest Request;
}

public class TickerFunctionContext
{
    internal AsyncServiceScope ServiceScope { get; set; }
    internal Action RequestCancelOperationAction { get; set; }
    public Guid Id { get; internal set; }
    public TickerType Type { get; internal set; }
    public int RetryCount { get; internal set; }
    public bool IsDue { get; internal set; }
    public string FunctionName { get; internal set; }
    public CronOccurrenceOperations CronOccurrenceOperations { get; internal set; }
    public void RequestCancellation() 
        => RequestCancelOperationAction();
    internal void SetServiceScope(AsyncServiceScope serviceScope)
        => ServiceScope = serviceScope;
}

public class CronOccurrenceOperations
{
    internal Action SkipIfAlreadyRunningAction { get; set; }
    public void SkipIfAlreadyRunning()
        => SkipIfAlreadyRunningAction();
}