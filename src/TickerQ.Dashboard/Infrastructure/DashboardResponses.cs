using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Dashboard.Infrastructure;

internal class AuthInfoResponse
{
    public string Mode { get; set; }
    public bool Enabled { get; set; }
    public int SessionTimeout { get; set; }
}

internal class AuthValidateResponse
{
    public bool Authenticated { get; set; }
    public string Username { get; set; }
    public string Message { get; set; }
}

internal class DashboardOptionsResponse
{
    public int MaxConcurrency { get; set; }
    public TimeSpan IdleWorkerTimeOut { get; set; }
    public string CurrentMachine { get; set; }
    public string LastHostExceptionMessage { get; set; }
    public string SchedulerTimeZone { get; set; }
}

internal class ActionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
}

internal class ActionResponseWithId
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public Guid? TickerId { get; set; }
}

internal class TickerRequestResponse
{
    public string Result { get; set; }
    public int MatchType { get; set; }
}

internal class TickerFunctionResponse
{
    public string FunctionName { get; set; }
    public string FunctionRequestNamespace { get; set; }
    public string FunctionRequestType { get; set; }
    public int Priority { get; set; }
}

internal class NextTickerResponse
{
    public DateTime? NextOccurrence { get; set; }
}

internal class HostStatusResponse
{
    public bool IsRunning { get; set; }
}

internal class TupleResponse<T1, T2>
{
    public T1 Item1 { get; set; }
    public T2 Item2 { get; set; }
}

internal class FrontendConfigResponse
{
    public string BasePath { get; set; }
    public string BackendDomain { get; set; }
    public AuthInfoResponse Auth { get; set; }
}
