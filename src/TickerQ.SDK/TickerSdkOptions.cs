namespace TickerQ.SDK;

public class TickerSdkOptions
{
    internal Uri? ApiUri { get; set; }
    internal Uri? CallbackUri { get; private set; }
    internal string? ApiKey { get; private set; }
    internal string? ApiSecret { get; private set; }

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
}
