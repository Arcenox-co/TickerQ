using System;
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
        CancelOperationAction = tickerFunctionContext.CancelOperationAction;
        CronOccurrenceOperations = tickerFunctionContext.CronOccurrenceOperations;
    }

    public readonly TRequest Request;
}

public class TickerFunctionContext
{
    internal Action CancelOperationAction { get; set; }
    public Guid Id { get; internal set; }
    public TickerType Type { get; internal set; }
    public int RetryCount { get; internal set; }
    public bool IsDue { get; internal set; }
    public CronOccurrenceOperations CronOccurrenceOperations { get; internal set; }
    public void CancelOperation() 
        => CancelOperationAction();
}

public class CronOccurrenceOperations
{
    internal Action SkipIfAlreadyRunningAction { get; set; }
    public void SkipIfAlreadyRunning()
        => SkipIfAlreadyRunningAction();
}