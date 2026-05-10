namespace TickerQ.RemoteExecutor;

public class TickerQRemoteExecutionOptions
{
    /// <summary>
    /// Single bearer-style token issued by the Hub (e.g. <c>tq_sched_AbCdEf...</c>).
    /// Carries the full credential — Hub looks the token up by SHA-256 hash. Per-env
    /// scoping is by token; the gRPC URL is fixed.
    /// </summary>
    internal string? ApiKey { get; set; }

    /// <summary>
    /// gRPC endpoint URL for the Hub. Locked to the production gRPC subdomain — not
    /// user-overridable.
    /// </summary>
    internal string HubGrpcEndpointUrl => TickerQRemoteExecutorConstants.HubGrpcBaseUrl;

    internal string? WebHookSignature { get; set; }

    /// <summary>
    /// Display name for this scheduler instance, shown in the Hub dashboard. Defaults to
    /// <see cref="Environment.MachineName"/>; override when running multiple instances per host.
    /// </summary>
    internal string NodeName { get; private set; } = Environment.MachineName;

    /// <summary>
    /// Loopback URL of the node's own gRPC server. The tunnel client replays inbound calls
    /// <summary>
    /// Public URL of this scheduler/application. Sent to the Hub on first tunnel
    /// register and stored as the env's ApplicationUrl so SDKs can discover where
    /// to open their worker streams without anyone setting it in the Hub UI.
    /// </summary>
    internal string? ApplicationUrl { get; private set; }

    /// <summary>
    /// Sets the single bearer-style API token (e.g. <c>tq_sched_AbCdEf...</c>) issued by
    /// the Hub. The token alone identifies which environment this scheduler belongs to.
    /// </summary>
    public void SetApiKey(string apiKey)
    {
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    }

    /// <summary>
    /// Sets a display name for this node in the Hub dashboard.
    /// </summary>
    public void SetNodeName(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.", nameof(nodeName));
        NodeName = nodeName.Trim();
    }

    /// <summary>
    /// Declares this scheduler's public URL. Sent to the Hub on tunnel register so the
    /// Hub auto-populates the env's ApplicationUrl — eliminating the manual UI step.
    /// SDKs read this URL from the Hub to open their worker streams.
    /// </summary>
    public void SetApplicationUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Application URL is required.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Application URL must be absolute.", nameof(url));
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Application URL must use http or https.", nameof(url));
        ApplicationUrl = url.TrimEnd('/');
    }

    /// <summary>
    /// Validates that all required configuration options are set.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when required options are missing.</exception>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                "TickerQ RemoteExecutor configuration is invalid: ApiKey is required. " +
                "Call SetApiKey() with the Hub-issued token (e.g. tq_sched_...).");
    }
}
