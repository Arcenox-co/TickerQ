namespace TickerQ.RemoteExecutor;

public class TickerQRemoteExecutionOptions
{
    internal string? ApiKey { get; set; }
    internal string? ApiSecret { get; set; }

    /// <summary>
    /// The Hub endpoint URL. Fixed to the TickerQ Hub service and cannot be changed.
    /// </summary>
    internal string HubEndpointUrl { get; } = TickerQRemoteExecutorConstants.HubBaseUrl;

    internal string? WebHookSignature { get; set; }

    public void SetApiKey(string apiKey)
    {
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    }

    public void SetApiSecret(string apiSecret)
    {
        ApiSecret = apiSecret ?? throw new ArgumentNullException(nameof(apiSecret));
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

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"TickerQ RemoteExecutor configuration is invalid:\n- {string.Join("\n- ", errors)}");
    }
}