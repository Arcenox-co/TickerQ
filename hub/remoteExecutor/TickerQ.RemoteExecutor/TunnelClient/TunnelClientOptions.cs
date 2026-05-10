namespace TickerQ.RemoteExecutor.TunnelClient;

/// <summary>
/// Configuration for the reverse tunnel client. When set, the scheduler opens a
/// persistent outbound gRPC stream to the Hub; the Hub dispatches dashboard calls
/// back through the stream, so the scheduler doesn't need a public URL.
/// </summary>
public sealed class TunnelClientOptions
{
    /// <summary>Hub tunnel endpoint, e.g. <c>https://hub.tickerq.net</c>.</summary>
    public string HubUrl { get; set; } = string.Empty;

    /// <summary>
    /// Single bearer-style token issued by the Hub (e.g. <c>tq_sched_AbCdEf...</c>).
    /// Carries the full credential — Hub looks the token up by SHA-256 hash. Replaces
    /// the previous (ApiKey, ApiSecret) pair.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Display name for this scheduler (shown in Hub dashboard).</summary>
    public string NodeName { get; set; } = System.Environment.MachineName;

    /// <summary>Whether to trust self-signed certs on the local gRPC loopback (dev).</summary>
    public bool TrustLocalDevCert { get; set; } = true;

    /// <summary>
    /// Public URL of this scheduler/application, reported on tunnel register.
    /// Hub stores it as the env's ApplicationUrl so SDKs can discover the scheduler.
    /// </summary>
    public string? ApplicationUrl { get; set; }
}
