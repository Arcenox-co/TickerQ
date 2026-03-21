namespace TickerQ.SDK;

public class TickerSdkOptions
{
    /// <summary>
    /// The API URL for job operations. Initially points to Hub, updated to Scheduler URL after sync.
    /// </summary>
    internal Uri? ApiUri { get; set; }

    /// <summary>
    /// The Hub URL. Fixed to the TickerQ Hub service and cannot be changed.
    /// </summary>
    internal Uri HubUri { get; } = new Uri(TickerQSdkConstants.HubBaseUrl);

    internal string? WebhookSignature { get; set; }
    internal Uri? CallbackUri { get; private set; }
    internal string? ApiKey { get; private set; }
    internal string? ApiSecret { get; private set; }
    internal string? NodeName { get; private set; }

    public TickerSdkOptions SetApiKey(string apiKey)
    {
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        return this;
    }

    public TickerSdkOptions SetApiSecret(string apiSecret)
    {
        ApiSecret = apiSecret ?? throw new ArgumentNullException(nameof(apiSecret));
        return this;
    }

    public TickerSdkOptions SetCallbackUri(Uri callbackUri)
    {
        CallbackUri = callbackUri ?? throw new ArgumentNullException(nameof(callbackUri));
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
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("ApiKey is required. Call SetApiKey() to configure.");

        if (string.IsNullOrWhiteSpace(ApiSecret))
            errors.Add("ApiSecret is required. Call SetApiSecret() to configure.");

        if (CallbackUri == null)
            errors.Add("CallbackUri is required. Call SetCallbackUri() to configure.");

        if (string.IsNullOrWhiteSpace(NodeName))
            errors.Add("NodeName is required. Call SetNodeName() to configure.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"TickerQ SDK configuration is invalid:\n- {string.Join("\n- ", errors)}");
    }
}
