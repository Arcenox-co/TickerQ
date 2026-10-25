using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// The stored state of one time ticker or cron occurrence: its status, failure or skip reason, and
    /// the result it published with <c>SetResult</c>. Returned by <c>GetOutcomeAsync</c> and
    /// <c>WaitForCompletionAsync</c> on the ticker managers.
    /// </summary>
    public sealed class TickerOutcome
    {
        private readonly TickerResultEnvelope _result;

        internal TickerOutcome(Guid id, TickerStatus status, DateTime? executedAt, string exceptionMessage,
            string skippedReason, TickerResultEnvelope result)
        {
            Id = id;
            Status = status;
            ExecutedAt = executedAt;
            ExceptionMessage = exceptionMessage;
            SkippedReason = skippedReason;
            _result = result;
        }

        /// <summary>The time ticker or cron occurrence id.</summary>
        public Guid Id { get; }

        public TickerStatus Status { get; }

        /// <summary>Whether the run has finished: Done, DueDone, Failed, Cancelled, or Skipped. Retries happen before this.</summary>
        public bool IsCompleted => Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
            or TickerStatus.Cancelled or TickerStatus.Skipped;

        /// <summary>Whether the run finished successfully (Done or DueDone).</summary>
        public bool IsSucceeded => Status is TickerStatus.Done or TickerStatus.DueDone;

        public DateTime? ExecutedAt { get; }

        /// <summary>The stored exception details when the run failed.</summary>
        public string ExceptionMessage { get; }

        public string SkippedReason { get; }

        /// <summary>Whether the successful run published a result. A published JSON null counts as a result.</summary>
        public bool HasResult => _result != null;

        /// <summary>
        /// Deserializes the published result with source-generated serializer metadata, the Native AOT-safe
        /// path. Check <see cref="HasResult"/> first; this throws when the run published no result.
        /// </summary>
        public T GetResult<T>(JsonTypeInfo<T> typeInfo)
        {
            if (typeInfo is null)
                throw new ArgumentNullException(nameof(typeInfo));
            return JsonSerializer.Deserialize(RequireResult().Payload.Span, typeInfo);
        }

        /// <summary>
        /// Reflection-based overload of <see cref="GetResult{T}(JsonTypeInfo{T})"/>. Not Native AOT or
        /// trim-safe; prefer the <see cref="JsonTypeInfo{T}"/> overload there.
        /// </summary>
        [RequiresUnreferencedCode("Result deserialization uses reflection metadata. Use the JsonTypeInfo overload for trimming/AOT.")]
        [RequiresDynamicCode("Result deserialization may require runtime JSON metadata. Use the JsonTypeInfo overload for Native AOT.")]
        public T GetResult<T>()
            => JsonSerializer.Deserialize<T>(RequireResult().Payload.Span, TickerHelper.RequestJsonSerializerOptions);

        private TickerResultEnvelope RequireResult()
        {
            if (_result is null)
                throw new InvalidOperationException(
                    "No result is available. Check HasResult first; a run that has not succeeded, or that called " +
                    "no SetResult, has none.");
            // Never trust a forward-versioned envelope: fail closed rather than misread it.
            _result.EnsureSupportedVersion();
            return _result;
        }
    }
}
