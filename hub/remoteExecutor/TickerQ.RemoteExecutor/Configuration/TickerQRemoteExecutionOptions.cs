namespace TickerQ.RemoteExecutor;

public class TickerQRemoteExecutionOptions
{
    internal string? ApiKey { get; set; }
    internal string? ApiSecret { get; set; }
    internal string? WebHookSignature { get; set; }

    /// <summary>
    /// Max items per SDK node's bounded write channel. Default: 100.
    /// </summary>
    public int NodeChannelCapacity { get; set; } = 100;

    /// <summary>
    /// Consecutive task failures before a node's circuit breaker opens. Default: 5.
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// How long a node's circuit stays open before allowing a probe request. Default: 30s.
    /// </summary>
    public TimeSpan CircuitBreakerCooldown { get; set; } = TimeSpan.FromSeconds(30);

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