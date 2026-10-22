using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace TickerQ.Docs.Demo;

/// <summary>
/// Deterministic local assistant used only by the documentation screenshot fixture.
/// Product integrations should provide their preferred IChatClient implementation.
/// </summary>
internal sealed class DocsDemoChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;
        var response = prompt.Contains("failure", StringComparison.OrdinalIgnoreCase)
            ? """
              ## Confirmed evidence

              `DocsDemo.Payment.Charge` failed inside the application handler after its retry was exhausted. The scheduler acquired and invoked the job normally; the captured source frame points to the intentional demo exception.

              ## Safest next action

              Review the payment handler and its input, then retry from the failed chain step after correcting the application condition.
              """
            : """
              I can inspect registered functions, scheduler health, retained execution logs, chains, cron occurrences, cluster nodes, and saved investigations. Operational changes are returned as reviewable proposals and are never applied without confirmation.
              """;

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
