using System;
using Microsoft.Extensions.AI;

namespace TickerQ.Dashboard.Assistant;

/// <summary>
/// Singleton that owns the operator-configured <see cref="IChatClient"/>
/// wrapped in a <see cref="FunctionInvokingChatClient"/> so tool calls run
/// automatically. Registered only when <c>AddAssistant(...)</c> configured a
/// chat client; its presence in DI is what flips the feature on.
/// </summary>
internal sealed class TickerAssistantService : IDisposable
{
    private readonly IChatClient _client;

    public AssistantOptionsBuilder Options { get; }

    public TickerAssistantService(AssistantOptionsBuilder options, IServiceProvider sp)
    {
        Options = options;
        var inner = options.ChatClientFactory!(sp);
        _client = new ChatClientBuilder(inner)
            .UseFunctionInvocation(configure: invoking =>
                invoking.MaximumIterationsPerRequest = options.MaxToolIterations)
            .Build();
    }

    /// <summary>The tool-invoking client used by the chat endpoint.</summary>
    public IChatClient Client => _client;

    public void Dispose() => _client.Dispose();
}
