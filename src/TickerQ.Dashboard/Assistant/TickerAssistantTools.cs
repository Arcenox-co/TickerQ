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
internal static partial class TickerAssistantTools
{
    public static IList<AITool> Build<TTimeTicker, TCronTicker>(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data,
        bool allowProposals,
        Func<AssistantProposal, Task>? emitProposal,
        CancellationToken ct,
        Func<Guid, CancellationToken, Task<TickerQ.Utilities.Infrastructure.ExecutionLogReadResult>>? readLogs = null,
        TickerQ.Utilities.Integrations.ITickerIntegrationAdminService? integrations = null,
        string schedulerTimeZone = "UTC",
        TickerQ.Utilities.Infrastructure.TickerQRuntimeDiagnostics? diagnostics = null,
        bool allowIntegrationProposals = true,
        RunInvestigatorOptionsBuilder? runInvestigator = null,
        IExecutionSourceContextProvider? sourceContextProvider = null)
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
                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        if (!TryParseStatus(status, out var st)) return "Invalid execution status.";
                        filter.Statuses = new[] { st };
                    }
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
                        sb.AppendLine($"  id={o.Id} chainRoot={o.ChainRootId} | {o.Status} | scheduled {Fmt(o.ScheduledFor)} | executed {Fmt(o.ExecutedAt)} | retries {o.RetryCount} | {ErrorOf(o.ExceptionMessage, o.SkippedReason)}");
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
                    return $"Scheduler time zone={schedulerTimeZone}. Current UTC={DateTime.UtcNow:O}. Host running={host.IsRunning}, " +
                        $"runningAttempts={host.ActiveExecutions}/{host.MaxConcurrency}, queuedAttempts={host.QueuedExecutions}, " +
                        $"functionWaits={host.WaitingForFunction}, retryDelays={host.RetryingExecutions}, activeWorkflows={host.ActiveWorkflows}. Status counts: {counts}";
                },
                "get_scheduler_status",
                "Get overall scheduler health: host state, running and queued attempts, function-capacity waits, retry delays, active workflows, and execution status counts. Workflow and attempt counts overlap."),
        };

        // ── Proposal tools (never in read-only mode) ──
        // These do NOT write anything. They validate, then emit a confirmation
        // card to the user; the actual save/create/update happens only when the
        // user applies the card through the dashboard's normal REST endpoints.
        if (allowProposals && emitProposal != null)
        {
            tools.Add(AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("Registered function name to run (must match exactly — use list_functions to check).")] string functionName,
                    [System.ComponentModel.Description("Short name for the saved draft (1-200 characters), e.g. 'Send weekly invoice'.")] string draftName,
                    [System.ComponentModel.Description("When to run, ISO-8601 UTC (e.g. 2026-07-12T06:00:00Z). Must be in the future.")] string executionTimeUtc,
                    [System.ComponentModel.Description("Retry attempts on failure (0 for none).")] int retries,
                    [System.ComponentModel.Description("Optional short description. Empty allowed.")] string description,
                    [System.ComponentModel.Description("JSON payload the job receives. Use an empty string when list_functions reports requestType=none; inspect typed functions with get_function_contract.")] string requestJson) =>
                {
                    var draftNameError = ValidateDraftName(draftName);
                    if (draftNameError != null) return draftNameError;
                    var invalid = await ValidateFunctionPayloadAsync(data, functionName, retries, requestJson, ct);
                    if (invalid != null) return invalid;

                    if (!DateTime.TryParse(executionTimeUtc, CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var executionTime))
                        return "Invalid executionTimeUtc — provide ISO-8601, e.g. 2026-07-12T06:00:00Z.";
                    if (executionTime < DateTime.UtcNow.AddMinutes(-1))
                        return $"executionTimeUtc is in the past (now is {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}Z). Pick a future time.";

                    await emitProposal(new AssistantProposal
                    {
                        Kind = "createTimeTickerDraft",
                        DraftName = draftName.Trim(),
                        Function = functionName,
                        ExecutionTime = executionTime.ToString("O"),
                        Retries = retries,
                        Description = description ?? "",
                        RequestJson = requestJson ?? "",
                    });
                    return DraftProposalShown;
                },
                "propose_time_ticker",
                "Prepare a ONE-TIME job as a saved, non-executable draft. Nothing is saved until the user clicks Save draft, and it cannot run until the user reviews and activates it."));

            tools.Add(AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("Registered function name to run (must match exactly).")] string functionName,
                    [System.ComponentModel.Description("Standard 5-field cron expression, e.g. '0 6 * * 1-5'.")] string expression,
                    [System.ComponentModel.Description("Retry attempts on failure (0 for none).")] int retries,
                    [System.ComponentModel.Description("Optional short description. Empty allowed.")] string description,
                    [System.ComponentModel.Description("JSON payload the job receives. Use an empty string when list_functions reports requestType=none; inspect typed functions with get_function_contract.")] string requestJson) =>
                {
                    var invalid = await ValidateFunctionPayloadAsync(data, functionName, retries, requestJson, ct);
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

                    if (retries < -1) return "Retries must be -1 (keep current) or non-negative.";
                    if (!string.IsNullOrWhiteSpace(enabled) && !bool.TryParse(enabled, out _)) return "enabled must be true, false or empty.";
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

                    // Preserve unspecified fields; confirmation applies a narrow PATCH.
                    await emitProposal(new AssistantProposal
                    {
                        Kind = "updateCronTicker",
                        TargetId = cron.Id,
                        Function = cron.FunctionName,
                        Expression = string.IsNullOrWhiteSpace(newExpression) ? cron.Expression : newExpression.Trim(),
                        CurrentExpression = cron.Expression,
                        Retries = retries >= 0 ? retries : null,
                        Description = string.IsNullOrWhiteSpace(description) ? null : description,
                        IsEnabled = isEnabled,
                    });
                    return ProposalShown;
                },
                "propose_cron_update",
                "Draft a change to an EXISTING cron ticker (expression, enabled state, retries, description) for the user to confirm. Always tell the user which cron will be modified."));

            tools.Add(AIFunctionFactory.Create(
                async (
                    [System.ComponentModel.Description("Short name for the saved chain draft (1-200 characters), e.g. 'Invoice fulfilment'.")] string draftName,
                    [System.ComponentModel.Description("When the ROOT step runs, ISO-8601 UTC (e.g. 2026-07-12T06:00:00Z). Must be in the future.")] string executionTimeUtc,
                    [System.ComponentModel.Description(
                        "The chain as a JSON object (single root step). Each step: " +
                        "{\"function\":\"Name\",\"retries\":0,\"description\":\"\",\"requestJson\":\"\"," +
                        "\"runCondition\":\"OnSuccess\",\"children\":[…]}. " +
                        "requestJson MUST be empty for functions with requestType=none; use get_function_contract before filling typed payloads. " +
                        "runCondition is REQUIRED on children (OnSuccess | OnFailure | OnCancelled | " +
                        "OnFailureOrCancelled | OnAnyCompletedStatus | InProgress) and must be omitted/null on the root. " +
                        "Max depth 10, max 5 children per step.")] string chainJson) =>
                {
                    var draftNameError = ValidateDraftName(draftName);
                    if (draftNameError != null) return draftNameError;
                    if (!DateTime.TryParse(executionTimeUtc, CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var executionTime))
                        return "Invalid executionTimeUtc — provide ISO-8601, e.g. 2026-07-12T06:00:00Z.";
                    if (executionTime < DateTime.UtcNow.AddMinutes(-1))
                        return $"executionTimeUtc is in the past (now is {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}Z). Pick a future time.";

                    if (chainJson == null || chainJson.Length > 32768) return "Chain JSON must be at most 32 KB.";
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
                    var registered = functions
                        .GroupBy(f => f.FunctionName, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                    var nodeCount = 0;
                    var error = ValidateChainNode(root, registered, isRoot: true, depth: 1, ref nodeCount);
                    if (error != null) return error;

                    await emitProposal(new AssistantProposal
                    {
                        Kind = "createChainDraft",
                        DraftName = draftName.Trim(),
                        Function = root.Function,
                        ExecutionTime = executionTime.ToString("O"),
                        ChainJson = JsonSerializer.Serialize(root, AssistantJsonContext.Default.AssistantChainNode),
                    });
                    return DraftProposalShown;
                },
                "propose_chain",
                "Prepare a CHAIN of time tickers as a saved, non-executable draft. Nothing is saved until the user clicks Save draft, and no step can run until the user reviews and activates it."));
        }

        AddOperationalTools(tools, data, allowProposals, emitProposal, ct, readLogs);
        AddRunInvestigatorTool(
            tools,
            data,
            ct,
            readLogs,
            runInvestigator ?? new RunInvestigatorOptionsBuilder(),
            sourceContextProvider);
        if (diagnostics != null)
            tools.Add(AIFunctionFactory.Create(() =>
                $"Node-local diagnostics. Fallback last success={diagnostics.LastSuccessfulFallbackScanUtc:O}; last failure={diagnostics.LastFallbackFailureUtc:O}; failures={diagnostics.FallbackScanFailures}. " +
                $"Log writes available={diagnostics.LogWritesAvailable?.ToString() ?? "unknown"}; last flush={diagnostics.LastSuccessfulLogFlushUtc:O}; write failures={diagnostics.LogWriteFailures}. Missing timestamps mean no observation, not proof of failure.",
                "get_runtime_diagnostics", "Read this node's overdue recovery and execution-log persistence health. This is not a cluster-wide health check."));
        if (integrations != null) AddIntegrationTools(tools, data, integrations, allowProposals && allowIntegrationProposals, emitProposal, ct);
        return tools;
    }

    private static readonly HashSet<string> ValidRunConditions = new(StringComparer.Ordinal)
    {
        "OnSuccess", "OnFailure", "OnCancelled", "OnFailureOrCancelled", "OnAnyCompletedStatus", "InProgress",
    };

    private static string ValidateChainNode(AssistantChainNode node,
        IReadOnlyDictionary<string, FunctionInfoDto> registered, bool isRoot, int depth, ref int nodeCount)
    {
        if (node == null) return "Chain steps cannot be null.";
        if (++nodeCount > 100) return "Chain has too many steps — max 100.";
        if (depth > 10) return "Chain too deep — max depth is 10.";
        if (string.IsNullOrWhiteSpace(node.Function)) return "Every chain step needs a function.";
        if (!registered.TryGetValue(node.Function, out var function))
            return $"Unknown function '{node.Function}'. Use list_functions to find registered names.";
        var payloadError = ValidatePayload(node.Retries ?? 0, node.RequestJson);
        if (payloadError != null) return $"Step '{node.Function}': {payloadError}";
        payloadError = ValidatePayloadForFunction(function, node.RequestJson);
        if (payloadError != null) return payloadError;

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
            var error = ValidateChainNode(child, registered, isRoot: false, depth + 1, ref nodeCount);
            if (error != null) return error;
        }
        return null;
    }

    private const string ProposalShown =
        "Proposal card shown to the user. It has NOT been applied — the user must click Confirm. " +
        "Tell the user to review the card; do not claim the ticker was created or updated.";

    private const string DraftProposalShown =
        "Draft proposal card shown to the user. It has NOT been saved or scheduled — the user must click Save draft. " +
        "After saving, the card provides a link to review, edit, and activate the draft. Do not claim the job was created or scheduled.";

    private static string? ValidateDraftName(string? draftName)
        => string.IsNullOrWhiteSpace(draftName) || draftName.Trim().Length > 200
            ? "Draft name must contain between 1 and 200 characters."
            : null;

    private static async Task<string> ValidateFunctionAsync<TTimeTicker, TCronTicker>(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data, string functionName, CancellationToken ct)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (string.IsNullOrWhiteSpace(functionName)) return "functionName is required.";
        var functions = await data.GetAllFunctionsAsync(ct);
        if (functions.Any(f => string.Equals(f.FunctionName, functionName, StringComparison.Ordinal)))
            return null;
        var available = string.Join(", ", functions.Take(50).Select(f => f.FunctionName));
        return $"Unknown function '{functionName}'. Registered functions: {available}";
    }

    private static async Task<string> ValidateFunctionPayloadAsync<TTimeTicker, TCronTicker>(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data, string functionName,
        int retries, string requestJson, CancellationToken ct)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var payloadError = ValidatePayload(retries, requestJson);
        if (payloadError != null) return payloadError;
        if (string.IsNullOrWhiteSpace(functionName)) return "functionName is required.";

        var functions = await data.GetAllFunctionsAsync(ct);
        var function = functions.FirstOrDefault(candidate =>
            string.Equals(candidate.FunctionName, functionName, StringComparison.Ordinal));
        if (function == null)
        {
            var available = string.Join(", ", functions.Take(50).Select(candidate => candidate.FunctionName));
            return $"Unknown function '{functionName}'. Registered functions: {available}";
        }

        return ValidatePayloadForFunction(function, requestJson);
    }

    private static string ValidatePayloadForFunction(FunctionInfoDto function, string requestJson)
    {
        var hasPayload = !string.IsNullOrWhiteSpace(requestJson);
        var contract = function.RequestContract;
        if (contract == null)
            return hasPayload
                ? $"Function '{function.FunctionName}' does not accept a request payload. Use an empty requestJson."
                : null;

        if (!hasPayload)
            return contract.Required
                ? $"Function '{function.FunctionName}' requires a request payload. Use get_function_contract before proposing it."
                : null;

        if (string.IsNullOrWhiteSpace(contract.SchemaJson)) return null;
        try
        {
            using var payload = JsonDocument.Parse(requestJson);
            using var schema = JsonDocument.Parse(contract.SchemaJson);
            if (!schema.RootElement.TryGetProperty("required", out var required) ||
                required.ValueKind != JsonValueKind.Array)
                return null;
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
                return $"Payload for function '{function.FunctionName}' must be a JSON object. Use get_function_contract for its schema.";

            var missing = required.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(name => !string.IsNullOrEmpty(name) && !payload.RootElement.TryGetProperty(name!, out _))
                .ToArray();
            return missing.Length == 0
                ? null
                : $"Payload for function '{function.FunctionName}' is missing required field(s): {string.Join(", ", missing)}. " +
                  "Use get_function_contract before proposing it.";
        }
        catch (JsonException)
        {
            // ValidatePayload already reports malformed request JSON. A malformed registered
            // schema is runtime metadata and should not make the assistant invent a payload.
            return null;
        }
    }

    private static string ValidateCronExpression(string expression)
    {
        if (expression == null || expression.Length > 256 || expression.Contains('%') ||
            !TickerQ.Utilities.CronExpression.TryParse(expression, out _))
            return "Invalid cron expression. Use a valid 5-field or 6-field expression, without configuration placeholders.";
        return null;
    }

    private static string? ValidatePayload(int retries, string? requestJson)
    {
        if (retries < 0) return "Retries cannot be negative.";
        if (string.IsNullOrWhiteSpace(requestJson)) return null;
        if (requestJson.Length > 16384) return "Job payload must be at most 16 KB.";
        try { using var document = JsonDocument.Parse(requestJson); }
        catch (JsonException) { return "Job payload must be valid JSON."; }
        return null;
    }

    private static int Clamp(int v, int max) => v < 1 ? 10 : (v > max ? max : v);

    private static bool TryParseStatus(string? s, out TickerStatus status)
        => Enum.TryParse(s, ignoreCase: true, out status) && Enum.IsDefined(status) && !string.IsNullOrWhiteSpace(s);

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
                $"| elapsed={e.ElapsedTime}ms | retries {e.RetryCount}/{e.Retries} | node={e.LockHolder ?? "-"} | {ErrorOf(e.ExceptionMessage, e.SkippedReason)}");
        }
        return sb.ToString();
    }
}
