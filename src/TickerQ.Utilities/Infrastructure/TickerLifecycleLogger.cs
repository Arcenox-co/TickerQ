using System;
using System.Buffers;
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

internal enum TickerLifecycleEvent : byte { None, Created, Queued, Started, RetryScheduled, Completed, ChainLaunched, Progress }

/// <summary>Best-effort lifecycle diagnostics, sharing the application's bounded execution-log tail.</summary>
internal sealed class TickerLifecycleLogger(ITickerExecutionLogStore store, ExecutionLogOptions options, ITickerClock clock)
{
    internal bool Enabled => options.Enabled && options.IncludeLifecycleEvents;

    public void Created(Guid id, string function, TickerType type, DateTime createdAt, string partition,
        bool isEphemeral = false)
    {
        if (!IsEnabled(LogLevel.Information)) return;
        Write(new InternalFunctionContext { TickerId = id, FunctionName = function, Type = type,
            RuntimePartitionKey = partition, IsTestRun = isEphemeral },
            TickerLifecycleEvent.Created, TickerStatus.Idle,
            type == TickerType.CronTickerOccurrence ? "Cron occurrence created." : "Job created.", createdAt);
    }

    public void CreatedTree<T>(TimeTickerEntity<T> root, string partition) where T : TimeTickerEntity<T>
    {
        if (!IsEnabled(LogLevel.Information) || root == null) return;
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
    {
        if (!IsEnabled(LogLevel.Information)) return;
        Write(context, TickerLifecycleEvent.Started, TickerStatus.InProgress, $"Running — attempt {attempt + 1}.", attempt: attempt);
    }

    public void RetryScheduled(InternalFunctionContext context, int attempt, int delaySeconds)
    {
        if (!IsEnabled(LogLevel.Warning)) return;
        Write(context, TickerLifecycleEvent.RetryScheduled, TickerStatus.InProgress,
            $"Retry {attempt}/{context.Retries} scheduled in {delaySeconds}s." + Reason(context.ExceptionDetails), attempt: attempt);
    }

    public void Progress(InternalFunctionContext context, TickerProgressUpdate update)
    {
        if (!IsEnabled(LogLevel.Information)) return;
        var percent = update.Percent.ToString("0.##", CultureInfo.InvariantCulture);
        var message = update.Message?.Trim();
        var duplicateSuffix = $"{percent}%";
        if (message?.EndsWith(duplicateSuffix, StringComparison.Ordinal) == true)
            message = message[..^duplicateSuffix.Length].TrimEnd();
        var detail = string.IsNullOrWhiteSpace(message) ? "" : $" — {message}";
        Write(context, TickerLifecycleEvent.Progress, TickerStatus.InProgress,
            $"Progress {percent}%{detail}", update.UpdatedAtUtc, eventId: Guid.NewGuid());
    }

    public void Committed(InternalFunctionContext context)
    {
        if (!IsEnabled(LevelFor(TickerLifecycleEvent.Completed, context.Status))) return;
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
    {
        if (!IsEnabled(LevelFor(TickerLifecycleEvent.ChainLaunched, context.Status))) return;
        Write(context, TickerLifecycleEvent.ChainLaunched, context.Status,
            context.Status == TickerStatus.Skipped ? "Recurring chain skipped." + Reason(context.ExceptionDetails)
                : "Recurring chain launched. Follow the child jobs for their outcomes.");
    }

    private bool IsEnabled(LogLevel level) => Enabled && level >= options.MinimumLevel;

    private static LogLevel LevelFor(TickerLifecycleEvent kind, TickerStatus status)
        => status == TickerStatus.Failed ? LogLevel.Error :
            status is TickerStatus.Cancelled or TickerStatus.Skipped || kind == TickerLifecycleEvent.RetryScheduled
                ? LogLevel.Warning : LogLevel.Information;

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
        string message, DateTime? timestamp = null, int? attempt = null, Guid? eventId = null)
    {
        try
        {
            var level = LevelFor(kind, status);
            if (!IsEnabled(level)) return;
            // A bounded tail deduplicates retries of the same notification. A new lease or
            // attempt remains distinct, including cron triggers sharing an ID with a chain root.
            store.Add(new TickerExecutionLogLine
            {
                Id = eventId ?? CreateEventId(context, kind, status, attempt), TickerId = context.TickerId,
                UnixMs = new DateTimeOffset(DateTime.SpecifyKind(timestamp ?? clock.UtcNow, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                Level = (int)level, Category = "TickerQ.Scheduler", FunctionName = context.FunctionName ?? "",
                Message = options.Redact == null ? message : options.Redact(message) ?? "",
                IsEphemeral = context.IsTestRun,
                LifecycleEvent = kind, Status = status, Attempt = kind == TickerLifecycleEvent.Created ? 0 : (attempt ?? context.RetryCount) + 1
            });
        }
        catch { /* Diagnostic capture must never change an execution outcome. */ }
    }

    private static Guid CreateEventId(InternalFunctionContext context, TickerLifecycleEvent kind,
        TickerStatus status, int? attempt)
    {
        char[] rentedChars = null;
        byte[] rentedBytes = null;
        Span<char> identity = stackalloc char[512];
        try
        {
            // Preserve the existing textual identity, including current-culture integer formatting,
            // unknown enum values, and empty nullable GUID fields. Do not materialize that text.
            int length;
            while (!TryFormatIdentity(identity, context, kind, status, attempt, out length))
            {
                var replacement = ArrayPool<char>.Shared.Rent(checked(identity.Length * 2));
                if (rentedChars != null) ArrayPool<char>.Shared.Return(rentedChars, clearArray: true);
                rentedChars = replacement;
                identity = rentedChars;
            }

            var byteCount = Encoding.UTF8.GetByteCount(identity[..length]);
            Span<byte> encoded = byteCount <= 1024
                ? stackalloc byte[byteCount]
                : (rentedBytes = ArrayPool<byte>.Shared.Rent(byteCount));
            Encoding.UTF8.GetBytes(identity[..length], encoded);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(encoded[..byteCount], hash);
            return new Guid(hash[..16]);
        }
        finally
        {
            if (rentedChars != null) ArrayPool<char>.Shared.Return(rentedChars, clearArray: true);
            if (rentedBytes != null) ArrayPool<byte>.Shared.Return(rentedBytes, clearArray: true);
        }
    }

    private static bool TryFormatIdentity(Span<char> destination, InternalFunctionContext context,
        TickerLifecycleEvent kind, TickerStatus status, int? attempt, out int written)
    {
        // Keep the provider implicit, as in the original interpolation: values use current culture,
        // but a CultureInfo subclass's ICustomFormatter must not become part of the identity.
        if (kind == TickerLifecycleEvent.Created)
            return destination.TryWrite(
                $"{context.RuntimePartitionKey}|{context.Type}|{context.TickerId}|{kind}|{status}|", out written);

        Span<char> acquisition = stackalloc char[36];
        Span<char> generation = stackalloc char[36];
        var acquisitionLength = 0;
        var generationLength = 0;
        if (context.AcquisitionToken is { } token) token.TryFormat(acquisition, out acquisitionLength);
        if (context.ChainGeneration is { } chain) chain.TryFormat(generation, out generationLength);
        return destination.TryWrite(
            $"{context.RuntimePartitionKey}|{context.Type}|{context.TickerId}|{kind}|{status}|{acquisition[..acquisitionLength]}|{generation[..generationLength]}|{attempt ?? context.RetryCount}",
            out written);
    }
}
