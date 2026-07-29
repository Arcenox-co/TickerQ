using Google.Protobuf;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;

namespace TickerQ.SDK.WorkerStream;

/// <summary>Fail-closed conversion between worker protobuf envelopes and Core result envelopes.</summary>
internal static class WorkerResultEnvelopeMapper
{
    internal const int MaxPayloadBytes = 1024 * 1024;
    private const string JsonMediaType = "application/json";

    internal static void ApplyParentResult(ExecuteFunction request, InternalFunctionContext function)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(function);

        Guid? parentId = null;
        if (request.HasParentId)
        {
            if (string.IsNullOrWhiteSpace(request.ParentId) || !Guid.TryParse(request.ParentId, out var parsed))
                throw new ArgumentException("ExecuteFunction parent_id must be a non-empty GUID when present.",
                    nameof(request));
            parentId = parsed;
        }

        // Validate every supplied value before mutating the context. A malformed transport
        // request therefore fails before user code can observe a partially populated context.
        var parentResult = request.ParentResult is null
            ? null
            : FromProto(request.ParentResult);
        function.ParentId = parentId;
        function.ParentResultEnvelope = parentResult;
    }

    internal static ExecutionResult CreateExecutionResult(
        string requestId,
        TickerWorkerExecutionResult outcome,
        bool cancelled)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        // Stream cancellation is authoritative even if the function raced to a successful return.
        var success = !cancelled && outcome.Success;
        var result = new ExecutionResult
        {
            RequestId = requestId ?? string.Empty,
            Success = success,
            Cancelled = cancelled || outcome.Cancelled,
            Error = success ? string.Empty : outcome.Error ?? (cancelled || outcome.Cancelled
                ? "Cancelled by dashboard"
                : "Function execution failed")
        };

        if (success && outcome.ResultEnvelope is not null)
            result.Result = ToProto(outcome.ResultEnvelope);

        return result;
    }

    internal static TickerResultEnvelope FromProto(ResultEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(envelope.EnvelopeVersion, envelope.MediaType, envelope.Payload.Length);
        return new TickerResultEnvelope(
            envelope.Payload.ToByteArray(),
            envelope.EnvelopeVersion,
            envelope.MediaType,
            Optional(envelope.ContractId),
            Optional(envelope.ContractType));
    }

    internal static ResultEnvelope ToProto(TickerResultEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(envelope.Version, envelope.MediaType, envelope.PayloadLength);
        return new ResultEnvelope
        {
            Payload = ByteString.CopyFrom(envelope.Payload.Span),
            EnvelopeVersion = envelope.Version,
            MediaType = envelope.MediaType,
            ContractId = envelope.ContractId ?? string.Empty,
            ContractType = envelope.ContractType ?? string.Empty
        };
    }

    private static void Validate(int version, string mediaType, int payloadLength)
    {
        if (version != TickerResultEnvelope.CurrentVersion)
            throw new NotSupportedException($"Unsupported ticker result envelope version {version}.");
        if (!string.Equals(mediaType, JsonMediaType, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unsupported ticker result media type '{mediaType}'.");
        if (payloadLength > MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payloadLength), payloadLength,
                $"Ticker result payload exceeds the {MaxPayloadBytes}-byte transport limit.");
    }

    private static string? Optional(string value)
        => string.IsNullOrEmpty(value) ? null : value;
}
