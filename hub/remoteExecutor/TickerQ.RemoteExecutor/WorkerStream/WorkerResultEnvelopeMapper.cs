using Google.Protobuf;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;

namespace TickerQ.RemoteExecutor.WorkerStream;

/// <summary>Fail-closed conversion between worker protobuf envelopes and Core result envelopes.</summary>
internal static class WorkerResultEnvelopeMapper
{
    internal const int MaxPayloadBytes = 1024 * 1024;
    private const string JsonMediaType = "application/json";

    internal static void SetParentResult(ExecuteFunction request, TickerResultEnvelope? envelope)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (envelope is not null)
            request.ParentResult = ToProto(envelope);
    }

    internal static void StageSuccessfulResult(ExecutionResult result, TickerFunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        if (result.Cancelled || !result.Success || result.Result is null)
            return;

        var envelope = FromProto(result.Result);
        (context.ResultSink ??= new TickerResultSink()).Set(envelope);
    }

    internal static void ValidateAndStageResult(ExecutionResult result, TickerFunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);

        // Cancellation dominates every other terminal flag. This deliberately rejects a
        // contradictory success+cancelled response as cancellation and never trusts its result.
        if (result.Cancelled)
            throw new TaskCanceledException(
                string.IsNullOrEmpty(result.Error) ? "Cancelled by dashboard" : result.Error);
        if (!result.Success)
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.Error) ? "Worker reported execution failure" : result.Error);
        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException(
                "Worker reported contradictory success and error terminal state.");

        StageSuccessfulResult(result, context);
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
