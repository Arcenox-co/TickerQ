namespace TickerQ.RemoteExecutor;

/// <summary>
/// Constants used by the TickerQ RemoteExecutor.
/// </summary>
internal static class TickerQRemoteExecutorConstants
{
    /// <summary>
    /// The base URL of the TickerQ Hub service.
    /// This is a fixed endpoint and cannot be configured by users.
    /// </summary>
    internal const string HubBaseUrl = "https://grpc.hub.tickerq.net/";
}
