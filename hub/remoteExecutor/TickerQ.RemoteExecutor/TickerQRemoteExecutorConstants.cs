namespace TickerQ.RemoteExecutor;

/// <summary>
/// Fixed endpoint constants for the production TickerQ Hub. Not user-configurable —
/// the SDK + Scheduler always dial the same Hub. Per-environment scoping is handled
/// by the API token (<c>tq_sched_*</c> / <c>tq_sdk_*</c>), not by URL.
/// </summary>
public static class TickerQRemoteExecutorConstants
{
    /// <summary>
    /// REST base URL of the Hub (used by the SDK for the boot-time function-sync POST).
    /// </summary>
    public const string HubBaseUrl = "https://hub.tickerq.net/";

    /// <summary>
    /// gRPC base URL of the Hub (used by the Scheduler tunnel + SDK control stream).
    /// Lives on a separate subdomain from the REST endpoint because the load-balancer
    /// backend behind it is HTTP/2-only — REST and gRPC can't share the same hostname.
    /// </summary>
    public const string HubGrpcBaseUrl = "https://grpc.hub.tickerq.net/";

    /// <summary>
    /// Hostname used for request routing (matches <see cref="HubBaseUrl"/>).
    /// </summary>
    public const string HubHostname = "hub.tickerq.net";
}
