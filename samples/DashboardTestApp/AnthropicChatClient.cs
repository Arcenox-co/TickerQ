using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace DashboardTestApp;

/// <summary>
/// <see cref="IChatClient"/> adapter over the official Anthropic C# SDK, so
/// the dashboard's provider-neutral assistant can talk to Claude. Supports
/// the tool loop: Claude's tool_use blocks map to FunctionCallContent (the
/// dashboard's FunctionInvokingChatClient executes them) and tool results map
/// back to tool_result blocks. Assistant turns keep their raw Anthropic
/// content in RawRepresentation so replays go back to the API intact.
/// </summary>
internal sealed class AnthropicChatClient : IChatClient
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicChatClient(string apiKey, string model = "claude-opus-4-8")
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        _model = model;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var (system, anthropicMessages) = BuildMessages(messages);
        var tools = BuildTools(options);

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = options?.MaxOutputTokens ?? 1500,
            Messages = anthropicMessages,
            System = system is null ? null : (MessageCreateParamsSystem)system,
            Tools = tools,
        };

        var response = await _client.Messages.Create(parameters, cancellationToken);

        var contents = new List<AIContent>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text))
            {
                contents.Add(new TextContent(text.Text));
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                var args = toolUse.Input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                contents.Add(new FunctionCallContent(toolUse.ID, toolUse.Name, args));
            }
        }

        var message = new ChatMessage(ChatRole.Assistant, contents)
        {
            // Preserve the original blocks for faithful replay on the next round.
            RawRepresentation = response.Content,
        };
        return new ChatResponse(message);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The dashboard endpoint streams SSE per tool round; buffering one
        // Anthropic round here keeps the adapter small. max_tokens is modest
        // (1500), so a non-streamed round stays well under HTTP timeouts.
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }

    private static (string? System, List<MessageParam> Messages) BuildMessages(IEnumerable<ChatMessage> messages)
    {
        string? system = null;
        var result = new List<MessageParam>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                system = system is null ? message.Text : $"{system}\n\n{message.Text}";
                continue;
            }

            if (message.Role == ChatRole.Assistant)
            {
                // Replay the original Anthropic blocks when we have them (keeps
                // tool_use ids intact for the follow-up tool_result matching).
                if (message.RawRepresentation is IReadOnlyList<ContentBlock> raw)
                {
                    var blocks = new List<ContentBlockParam>();
                    foreach (var b in raw)
                    {
                        if (b.TryPickText(out var t))
                            blocks.Add(new TextBlockParam { Text = t.Text });
                        else if (b.TryPickToolUse(out var tu))
                            blocks.Add(new ToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });
                    }
                    if (blocks.Count > 0)
                        result.Add(new MessageParam { Role = Role.Assistant, Content = blocks });
                }
                else if (!string.IsNullOrEmpty(message.Text))
                {
                    result.Add(new MessageParam { Role = Role.Assistant, Content = message.Text });
                }
                continue;
            }

            // User / tool role: tool results become tool_result blocks.
            var functionResults = message.Contents.OfType<FunctionResultContent>().ToList();
            if (functionResults.Count > 0)
            {
                var blocks = new List<ContentBlockParam>();
                foreach (var fr in functionResults)
                {
                    blocks.Add(new ToolResultBlockParam(fr.CallId)
                    {
                        Content = fr.Result?.ToString() ?? "",
                    });
                }
                result.Add(new MessageParam { Role = Role.User, Content = blocks });
            }
            else if (!string.IsNullOrEmpty(message.Text))
            {
                result.Add(new MessageParam { Role = Role.User, Content = message.Text });
            }
        }

        return (system, result);
    }

    private static List<ToolUnion>? BuildTools(ChatOptions? options)
    {
        var functions = options?.Tools?.OfType<AIFunction>().ToList();
        if (functions is not { Count: > 0 }) return null;

        var tools = new List<ToolUnion>();
        foreach (var fn in functions)
        {
            // AIFunction.JsonSchema is the full JSON Schema object
            // ({type, properties, required, …}) — hand it to the SDK raw.
            var rawSchema = fn.JsonSchema.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());

            tools.Add(new Tool
            {
                Name = fn.Name,
                Description = fn.Description,
                InputSchema = new InputSchema(rawSchema),
            });
        }
        return tools;
    }

    public object? GetService(System.Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
