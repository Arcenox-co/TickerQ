using Microsoft.Extensions.Logging;

namespace TickerQ.SDK;

/// <summary>
/// Controls per-execution ILogger capture: every log call made inside a
/// [TickerFunction] body is forwarded to the dashboard so customers can watch
/// their own logs in real time. Capture happens on top of the host's existing
/// log providers — console / AppInsights / etc. continue to receive the same
/// log calls untouched.
/// </summary>
public sealed class TickerSdkLogCaptureOptions
{
    /// <summary>Forward ILogger output from inside a ticker execution to the dashboard. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum log level to forward. Default Information.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Information;
}

public class TickerSdkOptions
{
    /// <summary>
    /// Worker-stream target URL — set by the boot-time function-sync to the Scheduler's
    /// ApplicationUrl returned by the Hub. Null until SyncAsync completes.
    /// </summary>
    internal Uri? ApiUri { get; set; }

    /// <summary>
    /// gRPC base URL of the Hub. Used for both the boot-time SyncNodesFunctions RPC
    /// and the persistent SDK control stream. Locked to the production endpoint —
    /// not user-overridable.
    /// </summary>
    internal Uri HubControlUri => new Uri(TickerQSdkConstants.HubGrpcBaseUrl);

    internal string? WebhookSignature { get; set; }
    /// <summary>
    /// Single bearer-style token issued by the Hub (e.g. <c>tq_sdk_AbCdEf...</c>).
    /// Sent as <c>x-api-key</c> metadata on every gRPC call. Hub looks the token up
    /// by SHA-256 hash; the SDK never sees a paired secret.
    /// </summary>
    internal string? ApiKey { get; private set; }
    /// <summary>
    /// Display name for this SDK node, shown in the Hub dashboard. Defaults to
    /// <see cref="Environment.MachineName"/>; override via <see cref="SetNodeName"/>
    /// when running multiple SDK instances per host so they're distinguishable.
    /// </summary>
    internal string NodeName { get; private set; } = Environment.MachineName;
    /// <summary>
    /// SDK runtime identifier sent to the Hub during function-sync; drives the
    /// dashboard's per-node logo. Hardcoded — this is the .NET SDK.
    /// </summary>
    internal const string SdkType = "dotnet";

    /// <summary>
    /// Per-execution log capture settings. Mutable so customers can flip behavior
    /// (e.g. disable forwarding, raise minimum level) before <c>AddTickerQSdk</c>
    /// returns. Default: enabled at Information.
    /// </summary>
    public TickerSdkLogCaptureOptions LogCapture { get; } = new();

    /// <summary>
    /// Sets the single bearer-style API token (e.g. <c>tq_sdk_AbCdEf...</c>) issued by
    /// the Hub. The token alone identifies which environment this SDK belongs to.
    /// </summary>
    public TickerSdkOptions SetApiKey(string apiKey)
    {
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        return this;
    }

    public TickerSdkOptions SetNodeName(string nodeName)
    {
        NodeName = string.IsNullOrWhiteSpace(nodeName) ? throw new ArgumentNullException(nameof(nodeName)) : nodeName;
        return this;
    }

    /// <summary>
    /// Validates that all required configuration options are set.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when required options are missing.</exception>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                "TickerQ SDK configuration is invalid: ApiKey is required. " +
                "Call SetApiKey() with the Hub-issued token (e.g. tq_sdk_...).");
    }
}
