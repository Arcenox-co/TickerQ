using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Assistant;

internal static partial class AssistantInvestigationFactory
{
    internal const string LegacyNamespace = "Legacy global";

    internal static string ResolveNamespace(SchedulerOptionsBuilder? scheduler)
        => string.IsNullOrWhiteSpace(scheduler?.ApplicationNamespace)
            ? LegacyNamespace
            : scheduler.ApplicationNamespace.Trim();

    internal static bool TryReadExecutionId(string? prompt, out Guid executionId)
    {
        executionId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        var match = InvestigationPrompt().Match(prompt.Trim());
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out executionId) && executionId != Guid.Empty;
    }

    internal static async Task<AssistantInvestigationRecord?> CreateAsync<TTimeTicker, TCronTicker>(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data,
        Guid executionId,
        string applicationNamespace,
        string trigger,
        string? userId,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var time = await data.GetTimeTickerByIdAsync(executionId, cancellationToken);
        if (time != null)
            return Create(applicationNamespace, trigger, userId, time.Id, "TimeTicker",
                time.ParentId ?? (time.ChildCount > 0 ? time.Id : null),
                time.FunctionName, time.Status.ToString(), time.ExceptionMessage ?? time.SkippedReason,
                time.ExecutedAt ?? time.ScheduledFor ?? time.CreatedAt);

        var occurrence = await data.GetCronOccurrenceByIdAsync(executionId, cancellationToken);
        if (occurrence == null) return null;
        return Create(applicationNamespace, trigger, userId, occurrence.Id, "CronOccurrence",
            occurrence.ChainRootId, occurrence.FunctionName, occurrence.Status.ToString(),
            occurrence.ExceptionMessage ?? occurrence.SkippedReason,
            occurrence.ExecutedAt ?? occurrence.ScheduledFor);
    }

    private static AssistantInvestigationRecord Create(string applicationNamespace, string trigger,
        string? userId, Guid executionId, string executionType, Guid? chainRootId,
        string? functionName, string executionStatus, string? rawError, DateTime occurredAt)
    {
        var evidence = ParseEvidence(rawError);
        var classification = Classify(executionStatus, evidence.Type, evidence.Message, evidence.SourceFile);
        var safeSourcePath = SafeSourcePath(evidence.SourceFile);
        // Source-backed failures are grouped by the application frame and exception type. Dynamic
        // values in the message (order IDs, timestamps, etc.) must not purchase a fresh diagnosis
        // for every recurring occurrence. Without a source frame, retain the message so unrelated
        // provider/scheduler errors are not merged.
        var fingerprintInput = evidence.SourceLine.HasValue && !string.IsNullOrWhiteSpace(safeSourcePath)
            ? string.Join("\n", functionName, executionStatus, evidence.Type,
                safeSourcePath, evidence.SourceLine.Value.ToString())
            : string.Join("\n", functionName, executionStatus, evidence.Type, evidence.Message);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)));
        return new AssistantInvestigationRecord
        {
            ApplicationNamespace = applicationNamespace,
            ExecutionId = executionId,
            ExecutionType = executionType,
            ChainRootId = chainRootId,
            FunctionName = functionName ?? "Unknown function",
            ExecutionStatus = executionStatus,
            Classification = classification,
            Trigger = trigger,
            State = AssistantInvestigationValues.Processing,
            ReviewState = AssistantInvestigationValues.New,
            EvidenceFingerprint = fingerprint,
            SourcePath = safeSourcePath,
            SourceLine = evidence.SourceLine,
            StartedByUserId = userId ?? "",
            // Persistence providers can materialize UTC database columns as Kind=Unspecified.
            // Their clock value is still UTC; converting it as local time would apply the host
            // offset a second time (for example, 13:00 UTC becomes 11:00 UTC on a UTC+02 host).
            OccurredAt = occurredAt.Kind == DateTimeKind.Local
                ? occurredAt.ToUniversalTime()
                : DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc),
        };
    }

    private static Evidence ParseEvidence(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Evidence(null, null, null, null);
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new Evidence(null, raw, null, null);
            var root = document.RootElement;
            return new Evidence(
                ReadString(root, "Type", "type", "ExceptionType", "exceptionType"),
                ReadString(root, "Message", "message") ?? raw,
                ReadString(root, "SourceFile", "sourceFile"),
                ReadPositiveInt(root, "SourceLine", "sourceLine"));
        }
        catch (JsonException)
        {
            return new Evidence(null, raw, null, null);
        }
    }

    private static string Classify(string status, string? type, string? message, string? sourceFile)
    {
        type ??= "";
        message ??= "";
        if (status == "Cancelled" || type.EndsWith("OperationCanceledException", StringComparison.Ordinal) ||
            type.EndsWith("TaskCanceledException", StringComparison.Ordinal)) return "Cancellation";
        if (type.Contains("TimeoutException", StringComparison.Ordinal) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        if (!string.IsNullOrWhiteSpace(sourceFile) || !string.IsNullOrWhiteSpace(type)) return "Application error";
        if (status == "Skipped") return "Scheduler decision";
        return "Unclassified";
    }

    private static string? SafeSourcePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index >= 0 ? normalized[(index + 1)..] : normalized;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static int? ReadPositiveInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number > 0)
                return number;
        return null;
    }

    [GeneratedRegex("^Investigate\\s+execution\\s+(?<id>[0-9a-fA-F-]{36})(?:\\.|\\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex InvestigationPrompt();

    private sealed record Evidence(string? Type, string? Message, string? SourceFile, int? SourceLine);
}
