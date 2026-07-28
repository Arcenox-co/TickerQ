using System.Reflection;
using Google.Protobuf;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class RemoteExecutionResultMappingTests
{
    [Fact]
    public void CreateExecuteFunction_DispatchesCommittedDirectParentEnvelope()
    {
        var context = NewContext();
        SetInternal(context, "ParentResultEnvelope", Envelope("null"));

        var execute = RemoteExecutionDelegateFactory.CreateExecuteFunction(context, [1, 2, 3]);

        Assert.NotNull(execute.ParentResult);
        Assert.Equal("null", execute.ParentResult.Payload.ToStringUtf8());
        Assert.Equal(1, execute.ParentResult.EnvelopeVersion);
        Assert.Equal("application/json", execute.ParentResult.MediaType);
        Assert.Equal("contract", execute.ParentResult.ContractId);
        Assert.Equal("Example.Result", execute.ParentResult.ContractType);
    }

    [Fact]
    public void CreateExecuteFunction_DispatchesDirectParentId()
    {
        var context = NewContext();
        var parentId = Guid.NewGuid();
        SetInternal(context, "ParentId", parentId);

        var execute = RemoteExecutionDelegateFactory.CreateExecuteFunction(context, []);

        Assert.True(execute.HasParentId);
        Assert.Equal(parentId.ToString(), execute.ParentId);
    }

    [Fact]
    public void CreateExecuteFunction_LeavesParentAbsentWhenNoCommittedResult()
    {
        var execute = RemoteExecutionDelegateFactory.CreateExecuteFunction(NewContext(), []);

        Assert.Null(execute.ParentResult);
    }

    [Fact]
    public void StageSuccessfulResult_PutsWorkerEnvelopeIntoCoreResultSink()
    {
        var context = NewContext();
        var result = new ExecutionResult
        {
            Success = true,
            Result = ProtoEnvelope("null")
        };

        WorkerResultEnvelopeMapper.StageSuccessfulResult(result, context);

        var sink = GetInternal(context, "ResultSink");
        Assert.NotNull(sink);
        Assert.True((bool)GetProperty(sink, "HasResult")!);
        var envelope = (TickerResultEnvelope)GetProperty(sink, "Envelope")!;
        Assert.Equal("null", System.Text.Encoding.UTF8.GetString(envelope.Payload.Span));
        Assert.Equal("contract", envelope.ContractId);
    }

    [Fact]
    public void StageSuccessfulResult_DoesNotStageFailureOrAbsentResult()
    {
        var failed = NewContext();
        WorkerResultEnvelopeMapper.StageSuccessfulResult(
            new ExecutionResult { Success = false, Result = ProtoEnvelope("null") }, failed);
        Assert.Null(GetInternal(failed, "ResultSink"));

        var absent = NewContext();
        WorkerResultEnvelopeMapper.StageSuccessfulResult(
            new ExecutionResult { Success = true }, absent);
        Assert.Null(GetInternal(absent, "ResultSink"));
    }

    [Fact]
    public void ContradictoryCancelledSuccess_IsCancellation_AndNeverStagesResult()
    {
        var context = NewContext();
        var result = new ExecutionResult
        {
            Success = true,
            Cancelled = true,
            Result = ProtoEnvelope("null")
        };

        Assert.Throws<TaskCanceledException>(() =>
            WorkerResultEnvelopeMapper.ValidateAndStageResult(result, context));
        Assert.Null(GetInternal(context, "ResultSink"));
    }

    [Fact]
    public void ContradictorySuccessfulResultWithError_FailsClosed_AndNeverStagesResult()
    {
        var context = NewContext();
        var result = new ExecutionResult
        {
            Success = true,
            Error = "contradictory worker state",
            Result = ProtoEnvelope("null")
        };

        Assert.Throws<InvalidOperationException>(() =>
            WorkerResultEnvelopeMapper.ValidateAndStageResult(result, context));
        Assert.Null(GetInternal(context, "ResultSink"));
    }

    [Fact]
    public void FailedResultWithEnvelope_FailsClosed_AndNeverStagesResult()
    {
        var context = NewContext();
        var result = new ExecutionResult
        {
            Success = false,
            Result = ProtoEnvelope("null")
        };

        Assert.Throws<InvalidOperationException>(() =>
            WorkerResultEnvelopeMapper.ValidateAndStageResult(result, context));
        Assert.Null(GetInternal(context, "ResultSink"));
    }

    [Theory]
    [InlineData(2, "application/json")]
    [InlineData(1, "text/plain")]
    [InlineData(0, "application/json")]
    public void StageSuccessfulResult_FailsClosed_OnUnsupportedMetadata(int version, string mediaType)
    {
        var proto = ProtoEnvelope("{}");
        proto.EnvelopeVersion = version;
        proto.MediaType = mediaType;

        Assert.ThrowsAny<Exception>(() => WorkerResultEnvelopeMapper.StageSuccessfulResult(
            new ExecutionResult { Success = true, Result = proto }, NewContext()));
    }

    [Fact]
    public void MappingRejectsPayloadOverOneMiBSymmetrically()
    {
        var tooLarge = new byte[(1024 * 1024) + 1];
        var context = NewContext();
        SetInternal(context, "ParentResultEnvelope",
            new TickerResultEnvelope(tooLarge, 1, "application/json"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteExecutionDelegateFactory.CreateExecuteFunction(context, []));

        var proto = ProtoEnvelope("{}");
        proto.Payload = ByteString.CopyFrom(tooLarge);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerResultEnvelopeMapper.StageSuccessfulResult(
                new ExecutionResult { Success = true, Result = proto }, NewContext()));
    }

    [Fact]
    public void MappingAcceptsPayloadAtExactlyOneMiB()
    {
        var payload = new byte[1024 * 1024];
        var context = NewContext();
        SetInternal(context, "ParentResultEnvelope",
            new TickerResultEnvelope(payload, 1, "application/json"));
        Assert.Equal(payload.Length,
            RemoteExecutionDelegateFactory.CreateExecuteFunction(context, []).ParentResult.Payload.Length);

        var proto = ProtoEnvelope("{}");
        proto.Payload = ByteString.CopyFrom(payload);
        WorkerResultEnvelopeMapper.StageSuccessfulResult(
            new ExecutionResult { Success = true, Result = proto }, context);
    }

    [Theory]
    [InlineData("ContractId")]
    [InlineData("ContractType")]
    public void MappingRejectsBlankOptionalMetadata(string field)
    {
        var proto = ProtoEnvelope("{}");
        typeof(ResultEnvelope).GetProperty(field)!.SetValue(proto, "   ");

        Assert.Throws<ArgumentException>(() => WorkerResultEnvelopeMapper.StageSuccessfulResult(
            new ExecutionResult { Success = true, Result = proto }, NewContext()));
    }

    private static TickerFunctionContext NewContext()
    {
        var context = new TickerFunctionContext();
        SetInternal(context, "Id", Guid.NewGuid());
        SetInternal(context, "FunctionName", "Remote@node");
        SetInternal(context, "Type", TickerType.TimeTicker);
        SetInternal(context, "ScheduledFor", DateTime.UtcNow);
        return context;
    }

    private static TickerResultEnvelope Envelope(string json)
        => new(System.Text.Encoding.UTF8.GetBytes(json), 1, "application/json", "contract", "Example.Result");

    private static ResultEnvelope ProtoEnvelope(string json) => new()
    {
        Payload = ByteString.CopyFromUtf8(json),
        EnvelopeVersion = 1,
        MediaType = "application/json",
        ContractId = "contract",
        ContractType = "Example.Result"
    };

    private static object? GetInternal(object target, string property)
        => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static object? GetProperty(object target, string property)
        => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(target);

    private static void SetInternal(object target, string property, object? value)
        => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
