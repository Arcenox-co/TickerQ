using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace TickerQ.Docs.Demo;

/// <summary>
/// Deterministic local assistant used only by the documentation screenshot fixture. It never makes
/// network calls. For schedule policy prompts it calls the dashboard's real tools so the screenshots
/// show genuine tool output and proposal cards. Product integrations should provide their preferred
/// IChatClient implementation.
/// </summary>
internal sealed class DocsDemoChatClient : IChatClient
{
    private const string ListPoliciesCall = "docs-list-policies";
    private const string ProposePolicyCall = "docs-propose-policy";

    private static readonly ChatClientMetadata Metadata =
        new("docs-demo", new Uri("http://localhost"), "docs-demo-local");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var toolResult = history.LastOrDefault()?.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        if (toolResult != null)
            return Reply(toolResult.CallId == ListPoliciesCall
                ? SummarizePolicies(toolResult.Result?.ToString() ?? string.Empty)
                : "I drafted the **Weekend deliveries** policy. Review the card: it allows Saturday and Sunday from 09:00 to 17:00 in Europe/Tirane, skips December 25 every year, and is blocked whenever the maintenance window allows. Nothing is saved until you confirm it.");

        var prompt = history.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;
        var tools = options?.Tools?.OfType<AIFunction>().Select(tool => tool.Name).ToHashSet() ?? [];

        if (prompt.Contains("polic", StringComparison.OrdinalIgnoreCase) &&
            (prompt.Contains("create", StringComparison.OrdinalIgnoreCase) || prompt.Contains("draft", StringComparison.OrdinalIgnoreCase)) &&
            tools.Contains("propose_schedule_policy"))
        {
            return Call(ProposePolicyCall, "propose_schedule_policy", new Dictionary<string, object?>
            {
                ["name"] = "Weekend deliveries",
                ["description"] = "Courier pickups on weekends, outside maintenance",
                ["timeZoneId"] = "Europe/Tirane",
                ["allowedDays"] = "Weekend",
                ["dailyWindows"] = "09:00-17:00",
                ["excludedDates"] = "",
                ["excludedEveryYear"] = "12-25",
                ["requiredPolicies"] = "",
                ["blockedWhenPoliciesAllow"] = "Maintenance window",
            });
        }

        if (prompt.Contains("polic", StringComparison.OrdinalIgnoreCase) && tools.Contains("list_schedule_policies"))
            return Call(ListPoliciesCall, "list_schedule_policies", new Dictionary<string, object?>());

        // Run investigations start with "Investigate execution ..."; chat questions mention failures.
        return Reply(prompt.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                     prompt.StartsWith("Investigate execution", StringComparison.OrdinalIgnoreCase)
            ? """
              ## Confirmed evidence

              `DocsDemo.Payment.Charge` failed inside the application handler after its retry was exhausted. The scheduler acquired and invoked the job normally; the captured source frame points to the intentional demo exception.

              ## Safest next action

              Review the payment handler and its input, then retry from the failed chain step after correcting the application condition.
              """
            : """
              I can inspect registered functions, scheduler health, retained execution logs, chains, cron occurrences, schedule policies, cluster nodes, and saved investigations. Operational changes are returned as reviewable proposals and are never applied without confirmation.
              """);
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
        => serviceType == typeof(ChatClientMetadata) ? Metadata
            : serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static Task<ChatResponse> Reply(string text)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

    private static Task<ChatResponse> Call(string callId, string name, IDictionary<string, object?> arguments)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, name, arguments)])));

    // Turns list_schedule_policies output into a short markdown answer.
    private static string SummarizePolicies(string toolOutput)
    {
        var lines = toolOutput.Split('\n', StringSplitOptions.TrimEntries);
        var answer = new List<string> { "These schedule policies are defined on this host:", "" };
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("- ", StringComparison.Ordinal)) continue;
            var header = lines[i][2..].Split(" | ");
            var rules = i + 1 < lines.Length ? lines[i + 1].Replace("days=", "").Replace(" | hours=", ", ") : "";
            var usage = lines.Skip(i + 1).TakeWhile(line => !line.StartsWith("- ", StringComparison.Ordinal))
                .FirstOrDefault(line => line.StartsWith("usedByCron=", StringComparison.Ordinal))?["usedByCron=".Length..] ?? "none";
            var zone = header.FirstOrDefault(part => part.StartsWith("zone=", StringComparison.Ordinal))?["zone=".Length..];
            answer.Add($"- **{header[0]}** ({zone}): {rules}. Used by: {usage}.");
        }
        answer.Add("");
        answer.Add("Ask me to preview a cron expression against one of them, or to draft a new policy.");
        return string.Join('\n', answer);
    }
}
