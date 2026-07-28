using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Base;

public class TickerFunctionContext<TRequest> : TickerFunctionContext
{
    public TickerFunctionContext(TickerFunctionContext tickerFunctionContext, TRequest request)
    {
        Request = request;
        Id = tickerFunctionContext.Id;
        ParentId = tickerFunctionContext.ParentId;
        Type = tickerFunctionContext.Type;
        RetryCount = tickerFunctionContext.RetryCount;
        IsDue = tickerFunctionContext.IsDue;
        ScheduledFor = tickerFunctionContext.ScheduledFor;
        RequestCancelOperationAction = tickerFunctionContext.RequestCancelOperationAction;
        CronOccurrenceOperations = tickerFunctionContext.CronOccurrenceOperations;
        FunctionName = tickerFunctionContext.FunctionName;
        // Result state is shared by reference with the originating base context so a
        // SetResult call on this typed wrapper reaches the single sink the runtime reads,
        // and the direct parent's committed result stays readable through this wrapper.
        ResultSink = tickerFunctionContext.ResultSink;
        ParentResultEnvelope = tickerFunctionContext.ParentResultEnvelope;
    }

    public readonly TRequest Request;
}

public class TickerFunctionContext
{
    internal AsyncServiceScope ServiceScope { get; set; }
    internal Action RequestCancelOperationAction { get; set; }
    public Guid Id { get; internal set; }
    /// <summary>
    /// The parent ticker identifier.
    /// For cron ticker occurrences, this is the owning CronTicker's Id.
    /// For chained time tickers, this is the parent TimeTicker's Id.
    /// Null when the ticker has no parent.
    /// </summary>
    public Guid? ParentId { get; internal set; }
    public TickerType Type { get; internal set; }
    public int RetryCount { get; internal set; }
    /// <summary>Max retry attempts configured on the ticker. Internal — not part of the user-facing API.</summary>
    internal int Retries { get; set; }
    /// <summary>Per-retry wait (seconds). Reused as fallback if shorter than <see cref="Retries"/>. Internal.</summary>
    internal int[] RetryIntervals { get; set; }
    public bool IsDue { get; internal set; }
    /// <summary>
    /// The time this ticker was scheduled to run (UTC).
    /// For time tickers, this is the ExecutionTime; for cron, the occurrence ExecutionTime.
    /// </summary>
    public DateTime ScheduledFor { get; internal set; }
    public string FunctionName { get; internal set; }
    public CronOccurrenceOperations CronOccurrenceOperations { get; internal set; }

    /// <summary>
    /// Shared sink the runtime reads after a successful terminal execution to publish this
    /// ticker's optional result. Created by the runtime before the function body is invoked
    /// and shared by reference with the typed <see cref="TickerFunctionContext{TRequest}"/>
    /// wrapper. Absent (never set) is deliberately distinguishable from a published JSON null.
    /// </summary>
    internal TickerResultSink ResultSink { get; set; }

    /// <summary>
    /// The direct parent's committed result, injected by the runtime before this function
    /// runs. Null for roots (no parent) and for parents that published no result. A deep
    /// descendant sees only its immediate parent's result, never an ancestor's.
    /// </summary>
    internal TickerResultEnvelope ParentResultEnvelope { get; set; }

    /// <summary>Whether this ticker's direct parent published a committed result this run can read.</summary>
    public bool HasParentResult => ParentResultEnvelope != null;

    public void RequestCancellation()
        => RequestCancelOperationAction();
    internal void SetServiceScope(AsyncServiceScope serviceScope)
        => ServiceScope = serviceScope;

    /// <summary>
    /// Publishes <paramref name="value"/> as this ticker's result using a source-generated
    /// <see cref="JsonTypeInfo{T}"/> — the AOT/trim-safe path. The value is serialized
    /// immediately into an immutable <see cref="TickerResultEnvelope"/>; the runtime persists
    /// it only if this execution reaches a successful terminal state (never on a failed
    /// attempt, exception, retry, cancellation, skip, or stale completion). Calling it more
    /// than once keeps the last value.
    /// </summary>
    public void SetResult<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        if (typeInfo is null)
            throw new ArgumentNullException(nameof(typeInfo));

        var payload = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        var envelope = new TickerResultEnvelope(
            payload,
            TickerResultEnvelope.CurrentVersion,
            "application/json",
            contractType: typeInfo.Type?.FullName);

        (ResultSink ??= new TickerResultSink()).Set(envelope);
    }

    /// <summary>
    /// Reflection-based convenience overload of <see cref="SetResult{T}(T, JsonTypeInfo{T})"/>.
    /// This path is NOT Native-AOT/trim-safe; prefer the <see cref="JsonTypeInfo{T}"/> overload
    /// in trimmed or AOT-published apps.
    /// </summary>
    [RequiresUnreferencedCode("Result serialization uses reflection metadata. Use the JsonTypeInfo overload for trimming/AOT.")]
    [RequiresDynamicCode("Result serialization may require runtime JSON metadata. Use the JsonTypeInfo overload for Native AOT.")]
    public void SetResult<T>(T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, TickerHelper.RequestJsonSerializerOptions);
        var envelope = new TickerResultEnvelope(
            payload,
            TickerResultEnvelope.CurrentVersion,
            "application/json",
            contractType: typeof(T).FullName);

        (ResultSink ??= new TickerResultSink()).Set(envelope);
    }

    /// <summary>
    /// Reads and deserializes the direct parent's committed result using a source-generated
    /// <see cref="JsonTypeInfo{T}"/>. Fails closed on an unknown envelope version. Check
    /// <see cref="HasParentResult"/> first — this throws when there is no parent result
    /// (a root, or a parent that published none). A parent's explicit JSON null is a present
    /// result and returns the default value, distinct from the no-parent-result case.
    /// </summary>
    public T GetParentResult<T>(JsonTypeInfo<T> typeInfo)
    {
        if (typeInfo is null)
            throw new ArgumentNullException(nameof(typeInfo));

        var envelope = RequireParentResult();
        return JsonSerializer.Deserialize(envelope.Payload.Span, typeInfo);
    }

    /// <summary>
    /// Reflection-based convenience overload of <see cref="GetParentResult{T}(JsonTypeInfo{T})"/>.
    /// NOT Native-AOT/trim-safe; prefer the <see cref="JsonTypeInfo{T}"/> overload for trimming/AOT.
    /// </summary>
    [RequiresUnreferencedCode("Result deserialization uses reflection metadata. Use the JsonTypeInfo overload for trimming/AOT.")]
    [RequiresDynamicCode("Result deserialization may require runtime JSON metadata. Use the JsonTypeInfo overload for Native AOT.")]
    public T GetParentResult<T>()
    {
        var envelope = RequireParentResult();
        return JsonSerializer.Deserialize<T>(envelope.Payload.Span, TickerHelper.RequestJsonSerializerOptions);
    }

    private TickerResultEnvelope RequireParentResult()
    {
        if (ParentResultEnvelope is null)
            throw new InvalidOperationException(
                "No parent result is available. Check HasParentResult first; a root ticker and a " +
                "parent that published no result have none.");

        // Never trust a forward-versioned envelope: fail closed rather than misread it.
        ParentResultEnvelope.EnsureSupportedVersion();
        return ParentResultEnvelope;
    }
}

public class CronOccurrenceOperations
{
    internal Action SkipIfAlreadyRunningAction { get; set; }
    public void SkipIfAlreadyRunning()
        => SkipIfAlreadyRunningAction();
}
