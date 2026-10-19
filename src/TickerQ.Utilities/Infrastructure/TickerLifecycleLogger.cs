using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

internal enum TickerLifecycleEvent : byte { None, Created, Queued, Started, RetryScheduled, Completed, ChainLaunched }

/// <summary>Best-effort lifecycle diagnostics, sharing the application's bounded execution-log tail.</summary>
internal sealed class TickerLifecycleLogger(ITickerExecutionLogStore store, ExecutionLogOptions options, ITickerClock clock)
{
    internal bool Enabled => options.Enabled && options.IncludeLifecycleEvents;

    public void Created(Guid id, string function, TickerType type, DateTime createdAt, string partition)
        => Write(new InternalFunctionContext { TickerId = id, FunctionName = function, Type = type,
            RuntimePartitionKey = partition }, TickerLifecycleEvent.Created, TickerStatus.Idle,
            type == TickerType.CronTickerOccurrence ? "Cron occurrence created." : "Job created.", createdAt);

    public void CreatedTree<T>(TimeTickerEntity<T> root, string partition) where T : TimeTickerEntity<T>
    {
        if (!Enabled || root == null) return;
        var pending = new Stack<TimeTickerEntity<T>>();
        var visited = new HashSet<Guid>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node.Id)) continue;
            Created(node.Id, node.Function, TickerType.TimeTicker, node.CreatedAt, partition);
            if (node.Children != null) foreach (var child in node.Children) pending.Push(child);
        }
    }

    public void Queued(InternalFunctionContext context)
        => Write(context, TickerLifecycleEvent.Queued, TickerStatus.Queued, "Queued for execution.");

    public void Started(InternalFunctionContext context, int attempt)
        => Write(context, TickerLifecycleEvent.Started, TickerStatus.InProgress, $"Running — attempt {attempt + 1}.", attempt: attempt);

    public void RetryScheduled(InternalFunctionContext context, int attempt, int delaySeconds)
        => Write(context, TickerLifecycleEvent.RetryScheduled, TickerStatus.InProgress,
            $"Retry {attempt}/{context.Retries} scheduled in {delaySeconds}s." + Reason(context.ExceptionDetails), attempt: attempt);

    public void Committed(InternalFunctionContext context)
    {
        if (!context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
            context.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped)) return;
        var outcome = context.Status switch
        {
            TickerStatus.Done => "Completed",
            TickerStatus.DueDone => "Completed (overdue recovery)",
            _ => context.Status.ToString()
        };
        var elapsedMs = Math.Max(0, context.ElapsedTime);
        var duration = elapsedMs < 1 ? "<1ms" : elapsedMs < 1000
            ? elapsedMs.ToString(CultureInfo.InvariantCulture) + "ms"
            : (elapsedMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture) + "s";
        Write(context, TickerLifecycleEvent.Completed, context.Status,
            $"{outcome} — {duration}; {context.RetryCount} retries." +
            (context.Status is TickerStatus.Done or TickerStatus.DueDone ? "" : Reason(context.ExceptionDetails)));
    }

    public void ChainLaunched(InternalFunctionContext context)
        => Write(context, TickerLifecycleEvent.ChainLaunched, context.Status,
            context.Status == TickerStatus.Skipped ? "Recurring chain skipped." + Reason(context.ExceptionDetails)
                : "Recurring chain launched. Follow the child jobs for their outcomes.");

    private static string Reason(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("Message", out var message) &&
                message.ValueKind == JsonValueKind.String) raw = message.GetString();
        }
        catch (JsonException) { }
        return " " + raw;
    }

    private void Write(InternalFunctionContext context, TickerLifecycleEvent kind, TickerStatus status,
        string message, DateTime? timestamp = null, int? attempt = null)
    {
        try
        {
            var level = status == TickerStatus.Failed ? LogLevel.Error :
                status is TickerStatus.Cancelled or TickerStatus.Skipped || kind == TickerLifecycleEvent.RetryScheduled
                    ? LogLevel.Warning : LogLevel.Information;
            if (!Enabled || level < options.MinimumLevel) return;
            // A bounded tail deduplicates retries of the same notification. A new lease or
            // attempt remains distinct, including cron triggers sharing an ID with a chain root.
            var identity = $"{context.RuntimePartitionKey}|{context.Type}|{context.TickerId}|{kind}|{status}|" +
                (kind == TickerLifecycleEvent.Created ? "" : $"{context.AcquisitionToken}|{context.ChainGeneration}|{attempt ?? context.RetryCount}");
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            store.Add(new TickerExecutionLogLine
            {
                Id = new Guid(hash.AsSpan(0, 16)), TickerId = context.TickerId,
                UnixMs = new DateTimeOffset(DateTime.SpecifyKind(timestamp ?? clock.UtcNow, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                Level = (int)level, Category = "TickerQ.Scheduler", FunctionName = context.FunctionName ?? "",
                Message = options.Redact == null ? message : options.Redact(message) ?? "",
                LifecycleEvent = kind, Status = status, Attempt = kind == TickerLifecycleEvent.Created ? 0 : (attempt ?? context.RetryCount) + 1
            });
        }
        catch { /* Diagnostic capture must never change an execution outcome. */ }
    }
}
