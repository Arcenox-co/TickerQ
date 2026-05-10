namespace TickerQ.SDK;

/// <summary>
/// Fixed endpoint constants for the production TickerQ Hub. Not user-configurable —
/// the SDK always dials the same Hub. Per-environment scoping is handled by the API
/// token (<c>tq_sdk_*</c>), not by URL.
/// </summary>
public static class TickerQSdkConstants
{
    /// <summary>
    /// gRPC base URL of the Hub. Used by both the boot-time SyncNodesFunctions RPC
    /// and the persistent SDK control stream. SDK is gRPC-only; there is no REST
    /// path to the Hub anymore.
    /// </summary>
    public const string HubGrpcBaseUrl = "https://grpc.hub.tickerq.net/";

    /// <summary>
    /// SDK version reported in the worker-stream Hello handshake.
    /// </summary>
    public const string SdkVersion = "1.0.0";
}
