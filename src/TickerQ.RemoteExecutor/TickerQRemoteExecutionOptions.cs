namespace TickerQ.RemoteExecutor;

public class TickerQRemoteExecutionOptions
{
    internal string ApiKey { get; set; }
    internal string ApiSecret { get; set; }
    internal string? FunctionsEndpointUrl { get; set; }
    
    public void SetApiKey(string apiKey)
    {
        ApiKey = apiKey;
    }
    
    public void SetApiSecret(string apiSecret)
    {
        ApiSecret = apiSecret;
    }
    
    public void SetFunctionsEndpointUrl(string url)
    {
        FunctionsEndpointUrl = url;
    }
}