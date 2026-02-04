namespace TickerQ.RemoteExecutor;

/// <summary>
/// Constants used by the TickerQ RemoteExecutor.
/// </summary>
public static class TickerQRemoteExecutorConstants
{
    /// <summary>
    /// The base URL of the TickerQ Hub service.
    /// This is a fixed endpoint and cannot be configured by users.
    /// </summary>
    public const string HubBaseUrl = "https://hub.tickerq.net/";

    /// <summary>
    /// The Hub hostname used for request routing.
    /// </summary>
    public const string HubHostname = "hub.tickerq.net";
}
