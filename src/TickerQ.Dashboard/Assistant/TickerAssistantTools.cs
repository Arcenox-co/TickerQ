using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Dashboard.Assistant;

/// <summary>
/// Read-only tools exposed to the chat model. Every tool is a thin wrapper
/// over the same <see cref="ITickerDashboardDataService{TTimeTicker,TCronTicker}"/>
/// the dashboard UI uses, so the model can only ever read what an authorized
/// dashboard user could read — and can never mutate anything. Results are
/// returned as compact text so the model spends its context on data, not JSON
/// punctuation.
/// </summary>
internal static class TickerAssistantTools
{
    public static IList<AITool> Build<TTimeTicker, TCronTicker>(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data,
        bool allowProposals,
        Func<AssistantProposal, Task> emitProposal,
        CancellationToken ct)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                async ([System.ComponentModel.Description("Max rows to return (1-50).")] int limit) =>
                {
                    var res = await data.GetExecutionsFlatAsync(
                        new ExecutionQueryFilter { Statuses = new[] { TickerStatus.Failed }, PageSize = Clamp(limit, 50), SortDescending = true }, ct);
                    return FormatExecutions(res.Items, res.TotalCount);
                },
                "get_recent_failures",
                "List the most recent FAILED executions (time tickers and cron occurrences) with their function name, exception message, retry counts and timestamps. Use this first when asked why things are failing."),

            AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("Filter by function name (contains match). Empty = any.")] string functionName,
                    [System.ComponentModel.Description("Filter by status: Idle, Queued, InProgress, Done, DueDone, Failed, Cancelled, Skipped. Empty = any.")] string status,
                    [System.ComponentModel.Description("Max rows to return (1-50).")] int limit) =>
                {
                    var filter = new ExecutionQueryFilter { PageSize = Clamp(limit, 50), SortDescending = true };
                    if (!string.IsNullOrWhiteSpace(functionName)) filter.FunctionName = functionName;
                    if (TryParseStatus(status, out var st)) filter.Statuses = new[] { st };
                    var res = await data.GetExecutionsFlatAsync(filter, ct);
                    return FormatExecutions(res.Items, res.TotalCount);
                },
                "query_executions",
                "Query executions by function name and/or status. Returns matching rows with exceptions and timing."),

            AIFunctionFactory.Create(
                async ([System.ComponentModel.Description("Max rows (1-50).")] int limit) =>
                {
                    var res = await data.GetCronTickersFlatAsync(
                        new CronTickerQueryFilter { PageSize = Clamp(limit, 50) }, ct);
                    var crons = res.Items.ToList();
                    if (crons.Count == 0) return "No cron tickers configured.";
                    var sb = new StringBuilder();
                    foreach (var c in crons)
                        sb.AppendLine($"- {c.FunctionName} | id={c.Id} | expr='{c.Expression}' | enabled={c.IsEnabled} | lastRun={c.LastRunStatus?.ToString() ?? "never"} at {Fmt(c.LastRunAt)} | occurrences={c.OccurrenceCount}");
                    return sb.ToString();
                },
                "list_cron_tickers",
                "List cron tickers with their expression, enabled state, last run status and occurrence count."),

            AIFunctionFactory.Create(
                async ([System.ComponentModel.Description("Cron ticker id (GUID).")] string cronTickerId) =>
                {
                    if (!Guid.TryParse(cronTickerId, out var id)) return "Invalid cron ticker id.";
                    var c = await data.GetCronTickerByIdAsync(id, ct);
                    if (c == null) return "No cron ticker with that id.";
                    var occ = await data.GetCronOccurrencesFlatAsync(
                        id, new CronOccurrenceQueryFilter { PageSize = 10, SortDescending = true }, ct);
                    var sb = new StringBuilder();
                    sb.AppendLine($"Cron '{c.FunctionName}' id={c.Id} expr='{c.Expression}' enabled={c.IsEnabled} retries={c.Retries} desc={c.Description ?? "-"}");
                    sb.AppendLine("Recent occurrences:");
                    foreach (var o in occ.Items.ToList())
                        sb.AppendLine($"  {o.Status} | scheduled {Fmt(o.ScheduledFor)} | executed {Fmt(o.ExecutedAt)} | retries {o.RetryCount} | {ErrorOf(o.ExceptionMessage, o.SkippedReason)}");
                    return sb.ToString();
                },
                "get_cron_ticker",
                "Get one cron ticker plus its 10 most recent occurrences (status, timing, errors). Use after list_cron_tickers to drill into a specific schedule."),

            AIFunctionFactory.Create(
                async () =>
                {
                    var nodes = await data.GetNodesAsync(ct);
                    if (nodes.Count == 0) return "No SDK nodes are currently connected (single-process scheduler, or nodes offline).";
                    var sb = new StringBuilder();
                    foreach (var n in nodes)
                        sb.AppendLine($"- {n.NodeName} | health={n.HealthStatus} | activeJobs={n.ActiveJobs} | functions={n.FunctionCount} | lastHeartbeat={Fmt(n.LastHeartbeat)}");
                    return sb.ToString();
                },
                "get_nodes",
                "List connected SDK nodes with health, sync state and last heartbeat. Useful when jobs are skipped because a node went offline."),

            AIFunctionFactory.Create(
                async () =>
                {
                    var host = await data.GetHostStatusAsync(ct);
                    var stats = await data.GetOverallStatusesAsync(ct);
                    var counts = string.Join(", ", stats.Select(s => $"{s.Status}={s.Count}"));
                    return $"Host running={host.IsRunning}, activeThreads={host.ActiveThreads}/{host.MaxConcurrency}. Status counts: {counts}";
                },
                "get_scheduler_status",
                "Get overall scheduler health: whether the host is running, active worker threads, and the count of executions in each status."),
        };

        // ── Proposal tools (never in read-only mode) ──
        // These do NOT write anything. They validate, then emit a confirmation
        // card to the user; the actual create/update happens only when the
        // user clicks confirm — through the dashboard's normal REST endpoints.
        if (allowProposals && emitProposal != null)
        {
            tools.Add(AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("Registered function name to run (must match exactly — use get_all tools to check).")] string functionName,
                    [System.ComponentModel.Description("When to run, ISO-8601 UTC (e.g. 2026-07-12T06:00:00Z). Must be in the future.")] string executionTimeUtc,
                    [System.ComponentModel.Description("Retry attempts on failure (0 for none).")] int retries,
                    [System.ComponentModel.Description("Optional short description. Empty allowed.")] string description,
                    [System.ComponentModel.Description("Optional JSON payload the job receives. Empty allowed.")] string requestJson) =>
                {
                    var invalid = await ValidateFunctionAsync(data, functionName, ct);
                    if (invalid != null) return invalid;

                    if (!DateTime.TryParse(executionTimeUtc, CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var executionTime))
                        return "Invalid executionTimeUtc — provide ISO-8601, e.g. 2026-07-12T06:00:00Z.";
                    if (executionTime < DateTime.UtcNow.AddMinutes(-1))
                        return $"executionTimeUtc is in the past (now is {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}Z). Pick a future time.";

                    await emitProposal(new AssistantProposal
                    {
                        Kind = "createTimeTicker",
                        Function = functionName,
                        ExecutionTime = executionTime.ToString("O"),
                        Retries = retries,
                        Description = description ?? "",
                        RequestJson = requestJson ?? "",
                    });
                    return ProposalShown;
                },
                "propose_time_ticker",
                "Draft a ONE-TIME job (time ticker) for the user to confirm. Nothing is created until the user clicks Confirm on the card."));

            tools.Add(AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("Registered function name to run (must match exactly).")] string functionName,
                    [System.ComponentModel.Description("Standard 5-field cron expression, e.g. '0 6 * * 1-5'.")] string expression,
                    [System.ComponentModel.Description("Retry attempts on failure (0 for none).")] int retries,
                    [System.ComponentModel.Description("Optional short description. Empty allowed.")] string description,
                    [System.ComponentModel.Description("Optional JSON payload the job receives. Empty allowed.")] string requestJson) =>
                {
                    var invalid = await ValidateFunctionAsync(data, functionName, ct);
                    if (invalid != null) return invalid;

                    var exprError = ValidateCronExpression(expression);
                    if (exprError != null) return exprError;

                    await emitProposal(new AssistantProposal
                    {
                        Kind = "createCronTicker",
                        Function = functionName,
                        Expression = expression.Trim(),
                        Retries = retries,
                        Description = description ?? "",
                        RequestJson = requestJson ?? "",
                        IsEnabled = true,
                    });
                    return ProposalShown;
                },
                "propose_cron_ticker",
                "Draft a RECURRING schedule (cron ticker) for the user to confirm. Nothing is created until the user clicks Confirm on the card."));

            tools.Add(AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("Id (GUID) of the cron ticker to modify — find it via list_cron_tickers first.")] string cronTickerId,
                    [System.ComponentModel.Description("New cron expression, or empty to keep the current one.")] string newExpression,
                    [System.ComponentModel.Description("\"true\", \"false\", or empty to keep the current enabled state.")] string enabled,
                    [System.ComponentModel.Description("New retry count, or -1 to keep the current one.")] int retries,
                    [System.ComponentModel.Description("New description, or empty to keep the current one.")] string description) =>
                {
                    if (!Guid.TryParse(cronTickerId, out var id)) return "Invalid cron ticker id.";
                    var cron = await data.GetCronTickerByIdAsync(id, ct);
                    if (cron == null) return "No cron ticker with that id — use list_cron_tickers to find the right one.";

                    if (!string.IsNullOrWhiteSpace(newExpression))
                    {
                        var exprError = ValidateCronExpression(newExpression);
                        if (exprError != null) return exprError;
                    }

                    bool? isEnabled = enabled?.Trim().ToLowerInvariant() switch
                    {
                        "true" => true,
                        "false" => false,
                        _ => null,
                    };

                    // The proposal carries the COMPLETE desired state so the
                    // confirm click sends one deterministic update.
                    await emitProposal(new AssistantProposal
                    {
                        Kind = "updateCronTicker",
                        TargetId = cron.Id,
                        Function = cron.FunctionName,
                        Expression = string.IsNullOrWhiteSpace(newExpression) ? cron.Expression : newExpression.Trim(),
                        CurrentExpression = cron.Expression,
                        Retries = retries >= 0 ? retries : cron.Retries,
                        Description = string.IsNullOrWhiteSpace(description) ? (cron.Description ?? "") : description,
                        IsEnabled = isEnabled ?? cron.IsEnabled,
                    });
                    return ProposalShown;
                },
                "propose_cron_update",
                "Draft a change to an EXISTING cron ticker (expression, enabled state, retries, description) for the user to confirm. Always tell the user which cron will be modified."));

            tools.Add(AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("When the ROOT step runs, ISO-8601 UTC (e.g. 2026-07-12T06:00:00Z). Must be in the future.")] string executionTimeUtc,
                    [System.ComponentModel.Description(
                        "The chain as a JSON object (single root step). Each step: " +
                        "{\"function\":\"Name\",\"retries\":0,\"description\":\"\",\"requestJson\":\"\"," +
                        "\"runCondition\":\"OnSuccess\",\"children\":[…]}. " +
                        "runCondition is REQUIRED on children (OnSuccess | OnFailure | OnCancelled | " +
                        "OnFailureOrCancelled | OnAnyCompletedStatus | InProgress) and must be omitted/null on the root. " +
                        "Max depth 10, max 5 children per step.")] string chainJson) =>
                {
                    if (!DateTime.TryParse(executionTimeUtc, CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var executionTime))
                        return "Invalid executionTimeUtc — provide ISO-8601, e.g. 2026-07-12T06:00:00Z.";
                    if (executionTime < DateTime.UtcNow.AddMinutes(-1))
                        return $"executionTimeUtc is in the past (now is {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}Z). Pick a future time.";

                    AssistantChainNode root;
                    try
                    {
                        root = JsonSerializer.Deserialize(chainJson, AssistantJsonContext.Default.AssistantChainNode);
                    }
                    catch (JsonException ex)
                    {
                        return $"chainJson is not valid JSON: {ex.Message}";
                    }
                    if (root == null) return "chainJson must be a single root step object.";

                    var functions = await data.GetAllFunctionsAsync(ct);
                    var registered = functions.Select(f => f.FunctionName).ToHashSet(StringComparer.Ordinal);
                    var error = ValidateChainNode(root, registered, isRoot: true, depth: 1);
                    if (error != null) return error;

                    await emitProposal(new AssistantProposal
                    {
                        Kind = "createChain",
                        Function = root.Function,
                        ExecutionTime = executionTime.ToString("O"),
                        ChainJson = JsonSerializer.Serialize(root, AssistantJsonContext.Default.AssistantChainNode),
                    });
                    return ProposalShown;
                },
                "propose_chain",
                "Draft a CHAIN of time tickers (a workflow: steps that run after their parent completes, per runCondition) for the user to confirm. Nothing is created until the user clicks Confirm on the card."));
        }

        return tools;
    }

    private static readonly HashSet<string> ValidRunConditions = new(StringComparer.Ordinal)
    {
        "OnSuccess", "OnFailure", "OnCancelled", "OnFailureOrCancelled", "OnAnyCompletedStatus", "InProgress",
    };

    private static string ValidateChainNode(AssistantChainNode node, HashSet<string> registered, bool isRoot, int depth)
    {
        if (depth > 10) return "Chain too deep — max depth is 10.";
        if (string.IsNullOrWhiteSpace(node.Function)) return "Every chain step needs a function.";
        if (!registered.Contains(node.Function))
            return $"Unknown function '{node.Function}'. Registered functions: {string.Join(", ", registered)}";

        if (isRoot)
        {
            if (!string.IsNullOrEmpty(node.RunCondition))
                return "The root step must not have a runCondition — it starts the chain.";
        }
        else if (string.IsNullOrEmpty(node.RunCondition) || !ValidRunConditions.Contains(node.RunCondition))
        {
            return $"Step '{node.Function}' needs a valid runCondition: {string.Join(" | ", ValidRunConditions)}";
        }

        if (node.Children is { Count: > 5 }) return $"Step '{node.Function}' has too many children — max 5.";
        foreach (var child in node.Children ?? Enumerable.Empty<AssistantChainNode>().ToList())
        {
            var error = ValidateChainNode(child, registered, isRoot: false, depth + 1);
            if (error != null) return error;
        }
        return null;
    }

    private const string ProposalShown =
        "Proposal card shown to the user. It has NOT been applied — the user must click Confirm. " +
        "Tell the user to review the card; do not claim the ticker was created or updated.";

    private static async Task<string> ValidateFunctionAsync<TTimeTicker, TCronTicker>(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data, string functionName, CancellationToken ct)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (string.IsNullOrWhiteSpace(functionName)) return "functionName is required.";
        var functions = await data.GetAllFunctionsAsync(ct);
        if (functions.Any(f => string.Equals(f.FunctionName, functionName, StringComparison.Ordinal)))
            return null;
        var available = string.Join(", ", functions.Select(f => f.FunctionName));
        return $"Unknown function '{functionName}'. Registered functions: {available}";
    }

    private static string ValidateCronExpression(string expression)
    {
        // TickerQ accepts standard 5-field cron and 6-field (leading seconds).
        var parts = (expression ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is 5 or 6
            ? null
            : "Invalid cron expression — expected 5 fields (minute hour day-of-month month day-of-week, e.g. '0 6 * * 1-5') or 6 with leading seconds.";
    }

    private static int Clamp(int v, int max) => v < 1 ? 10 : (v > max ? max : v);

    private static bool TryParseStatus(string? s, out TickerStatus status)
        => Enum.TryParse(s, ignoreCase: true, out status) && !string.IsNullOrWhiteSpace(s);

    private static string Fmt(DateTime? t)
        => t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z" : "-";

    private static string ErrorOf(string? exception, string? skipped)
        => !string.IsNullOrEmpty(exception) ? $"error: {Trim(exception)}"
         : !string.IsNullOrEmpty(skipped) ? $"skipped: {Trim(skipped)}"
         : "no error";

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;

    private static string FormatExecutions(IEnumerable<ExecutionFlatDto> source, int total)
    {
        var items = source.ToList();
        if (items.Count == 0) return "No matching executions.";
        var sb = new StringBuilder();
        sb.AppendLine($"{items.Count} of {total} rows (most recent first). Timestamps are UTC.");
        foreach (var e in items)
        {
            sb.AppendLine(
                $"- {e.FunctionName} [{e.Type}] id={e.Id} | {e.Status} | scheduled {Fmt(e.ScheduledFor)} | executed {Fmt(e.ExecutedAt)} " +
                $"| retries {e.RetryCount}/{e.Retries} | node={e.LockHolder ?? "-"} | {ErrorOf(e.ExceptionMessage, e.SkippedReason)}");
        }
        return sb.ToString();
    }
}
