using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Assistant;
using TickerQ.Dashboard.DependencyInjection;

namespace TickerQ.Tests;

public sealed class AssistantConnectionInfoTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1", "localhost:11434", "local")]
    [InlineData("http://127.0.0.1:1234/v1", "127.0.0.1:1234", "local")]
    [InlineData("http://[::1]:8080/", "[::1]:8080", "local")]
    [InlineData("http://ollama.localhost/", "ollama.localhost", "local")]
    [InlineData("https://api.openai.com/v1?api-key=secret", "api.openai.com", "remote")]
    [InlineData("https://my-resource.openai.azure.com/openai/deployments/x", "my-resource.openai.azure.com", "remote")]
    [InlineData("http://10.0.0.5:11434/", "10.0.0.5:11434", "remote")]
    public void Endpoint_is_reduced_to_host_and_port_and_classified(string uri, string endpoint, string location)
    {
        var info = AssistantConnectionInfo.Describe(new MetadataClient(new ChatClientMetadata("openai", new Uri(uri), "gpt-4o-mini")), "");

        Assert.Equal(endpoint, info.Endpoint);
        Assert.Equal(location, info.Location);
        Assert.Equal("openai", info.Provider);
        Assert.DoesNotContain("secret", info.Endpoint);
        Assert.DoesNotContain("/", info.Endpoint);
    }

    [Fact]
    public void Configured_model_name_wins_over_the_provider_default()
    {
        var client = new MetadataClient(new ChatClientMetadata("ollama", new Uri("http://localhost:11434"), "llama3.1"));

        Assert.Equal("llama3.1", AssistantConnectionInfo.Describe(client, "").Model);
        Assert.Equal("Ops model", AssistantConnectionInfo.Describe(client, "Ops model").Model);
    }

    [Fact]
    public void Client_without_metadata_is_unknown()
    {
        var info = AssistantConnectionInfo.Describe(new MetadataClient(null), "custom");

        Assert.Equal(AssistantConnectionInfo.Unknown, info.Location);
        Assert.Equal("", info.Provider);
        Assert.Equal("", info.Endpoint);
        Assert.Equal("custom", info.Model);
    }

    [Fact]
    public void Client_that_throws_while_describing_itself_is_unknown()
    {
        var info = AssistantConnectionInfo.Describe(new MetadataClient(null, throws: true), "");

        Assert.Equal(AssistantConnectionInfo.Unknown, info.Location);
    }

    [Fact]
    public void Runtime_config_reports_a_local_model_without_exposing_the_endpoint_path()
    {
        var script = ConfigScript(_ => new MetadataClient(
            new ChatClientMetadata("ollama", new Uri("http://localhost:11434/v1?token=secret"), "llama3.1")));

        Assert.Contains("\"provider\":\"ollama\"", script);
        Assert.Contains("\"endpoint\":\"localhost:11434\"", script);
        Assert.Contains("\"location\":\"local\"", script);
        Assert.Contains("\"model\":\"llama3.1\"", script);
        Assert.Contains("\"connectionFailed\":false", script);
        Assert.DoesNotContain("secret", script);
    }

    [Fact]
    public void Runtime_config_reports_a_chat_client_that_cannot_be_created()
    {
        var script = ConfigScript(_ => throw new InvalidOperationException("OPENAI_API_KEY is not set"));

        Assert.Contains("\"enabled\":true", script);
        Assert.Contains("\"connectionFailed\":true", script);
        Assert.DoesNotContain("OPENAI_API_KEY", script);
    }

    private static string ConfigScript(Func<IServiceProvider, IChatClient> factory)
    {
        var options = new DashboardOptionsBuilder();
        options.AddAssistant(assistant => assistant.UseChatClient(factory));
        var services = new ServiceCollection();
        services.AddSingleton(sp => new TickerAssistantService(options.Assistant!, sp));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return ServiceCollectionExtensions.GenerateConfigJs(context, "/tickerq/dashboard", options);
    }

    private sealed class MetadataClient(ChatClientMetadata? metadata, bool throws = false) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (throws) throw new InvalidOperationException("metadata unavailable");
            return serviceType == typeof(ChatClientMetadata) ? metadata : null;
        }

        public void Dispose() { }
    }
}
