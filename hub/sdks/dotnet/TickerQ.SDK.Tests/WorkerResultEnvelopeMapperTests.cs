using Google.Protobuf;
using TickerQ.SDK.WorkerStream;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;
using Xunit;

namespace TickerQ.SDK.Tests;

public sealed class WorkerResultEnvelopeMapperTests
{
    [Fact]
    public void ApplyParentResult_Stages_AllMetadata_OnExecutionContext()
    {
        var function = new InternalFunctionContext();
        var request = new ExecuteFunction
        {
            ParentResult = new ResultEnvelope
            {
                Payload = ByteString.CopyFromUtf8("null"),
                EnvelopeVersion = 1,
                MediaType = "application/json",
                ContractId = "contract",
                ContractType = "Example.Result"
            }
        };

        WorkerResultEnvelopeMapper.ApplyParentResult(request, function);

        Assert.NotNull(function.ParentResultEnvelope);
        Assert.Equal("null", System.Text.Encoding.UTF8.GetString(function.ParentResultEnvelope.Payload.Span));
        Assert.Equal(1, function.ParentResultEnvelope.Version);
        Assert.Equal("application/json", function.ParentResultEnvelope.MediaType);
        Assert.Equal("contract", function.ParentResultEnvelope.ContractId);
        Assert.Equal("Example.Result", function.ParentResultEnvelope.ContractType);
    }

    [Fact]
    public void ApplyParentResult_ParsesDirectParentId()
    {
        var parentId = Guid.NewGuid();
        var function = new InternalFunctionContext();

        WorkerResultEnvelopeMapper.ApplyParentResult(
            new ExecuteFunction { ParentId = parentId.ToString() }, function);

        Assert.Equal(parentId, function.ParentId);
    }

    [Fact]
    public void ApplyParentResult_LeavesParentIdNullForLegacyMessage()
    {
        var function = new InternalFunctionContext();

        WorkerResultEnvelopeMapper.ApplyParentResult(new ExecuteFunction(), function);

        Assert.Null(function.ParentId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public void ApplyParentResult_FailsClosedBeforeMutatingContext_OnMalformedParentId(string parentId)
    {
        var function = new InternalFunctionContext();
        var request = new ExecuteFunction
        {
            ParentId = parentId,
            ParentResult = new ResultEnvelope
            {
                Payload = ByteString.CopyFromUtf8("null"),
                EnvelopeVersion = 1,
                MediaType = "application/json"
            }
        };

        Assert.Throws<ArgumentException>(() =>
            WorkerResultEnvelopeMapper.ApplyParentResult(request, function));
        Assert.Null(function.ParentId);
        Assert.Null(function.ParentResultEnvelope);
    }

    [Fact]
    public void ApplyParentResult_LeavesAbsentParentAbsent()
    {
        var function = new InternalFunctionContext();

        WorkerResultEnvelopeMapper.ApplyParentResult(new ExecuteFunction(), function);

        Assert.Null(function.ParentResultEnvelope);
    }

    [Theory]
    [InlineData(2, "application/json")]
    [InlineData(1, "text/plain")]
    [InlineData(0, "application/json")]
    public void ApplyParentResult_FailsClosed_OnUnsupportedMetadata(int version, string mediaType)
    {
        var request = new ExecuteFunction
        {
            ParentResult = new ResultEnvelope
            {
                Payload = ByteString.CopyFromUtf8("{}"),
                EnvelopeVersion = version,
                MediaType = mediaType
            }
        };

        Assert.ThrowsAny<Exception>(() =>
            WorkerResultEnvelopeMapper.ApplyParentResult(request, new InternalFunctionContext()));
    }

    [Fact]
    public void ApplyParentResult_RejectsPayloadOverOneMiB()
    {
        var request = new ExecuteFunction
        {
            ParentResult = new ResultEnvelope
            {
                Payload = ByteString.CopyFrom(new byte[(1024 * 1024) + 1]),
                EnvelopeVersion = 1,
                MediaType = "application/json"
            }
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerResultEnvelopeMapper.ApplyParentResult(request, new InternalFunctionContext()));
    }

    [Fact]
    public void ApplyParentResult_AcceptsPayloadAtExactlyOneMiB()
    {
        var function = new InternalFunctionContext();
        var request = new ExecuteFunction
        {
            ParentResult = new ResultEnvelope
            {
                Payload = ByteString.CopyFrom(new byte[1024 * 1024]),
                EnvelopeVersion = 1,
                MediaType = "application/json"
            }
        };

        WorkerResultEnvelopeMapper.ApplyParentResult(request, function);

        Assert.NotNull(function.ParentResultEnvelope);
        Assert.Equal(1024 * 1024, function.ParentResultEnvelope.PayloadLength);
    }

    [Theory]
    [InlineData("ContractId")]
    [InlineData("ContractType")]
    public void ApplyParentResult_RejectsBlankOptionalMetadata(string field)
    {
        var envelope = new ResultEnvelope
        {
            Payload = ByteString.CopyFromUtf8("{}"),
            EnvelopeVersion = 1,
            MediaType = "application/json"
        };
        typeof(ResultEnvelope).GetProperty(field)!.SetValue(envelope, "   ");

        Assert.Throws<ArgumentException>(() => WorkerResultEnvelopeMapper.ApplyParentResult(
            new ExecuteFunction { ParentResult = envelope }, new InternalFunctionContext()));
    }

    [Theory]
    [InlineData(TickerStatus.Done, true, true)]
    [InlineData(TickerStatus.DueDone, true, true)]
    [InlineData(TickerStatus.Failed, false, false)]
    [InlineData(TickerStatus.Cancelled, false, false)]
    [InlineData(TickerStatus.Skipped, false, false)]
    public void CreateExecutionResult_IncludesEnvelopeOnlyForFinalSuccess(
        TickerStatus status, bool success, bool expectEnvelope)
    {
        var outcome = new TickerWorkerExecutionResult(
            status,
            status is TickerStatus.Done or TickerStatus.DueDone ? null : "terminal failure",
            new TickerResultEnvelope(
                System.Text.Encoding.UTF8.GetBytes("null"), 1, "application/json", "contract", "Example.Result")
        );

        var result = WorkerResultEnvelopeMapper.CreateExecutionResult(
            "request", outcome, cancelled: status == TickerStatus.Cancelled);

        Assert.Equal(success, result.Success);
        Assert.Equal(expectEnvelope, result.Result is not null);
        if (expectEnvelope)
            Assert.Equal("null", result.Result!.Payload.ToStringUtf8());
    }

    [Fact]
    public void WorkerOutcome_RejectsNonTerminalStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TickerWorkerExecutionResult(TickerStatus.InProgress, null, null));
    }

    [Fact]
    public void CreateExecutionResult_FinalSuccessfulRetryWithoutResult_OmitsEarlierAttemptResult()
    {
        // Core resets its per-attempt sink before every retry (covered by
        // ParentResultPropagationTests). The transport must preserve the resulting absence.
        var outcome = new TickerWorkerExecutionResult(TickerStatus.Done, null, null);

        var result = WorkerResultEnvelopeMapper.CreateExecutionResult(
            "request", outcome, cancelled: false);

        Assert.True(result.Success);
        Assert.Null(result.Result);
    }

    [Fact]
    public void CreateExecutionResult_StreamCancellationDominatesSuccessfulOutcome()
    {
        var outcome = new TickerWorkerExecutionResult(
            TickerStatus.Done,
            null,
            new TickerResultEnvelope(ByteString.CopyFromUtf8("null").ToByteArray(), 1, "application/json"));

        var result = WorkerResultEnvelopeMapper.CreateExecutionResult(
            "request", outcome, cancelled: true);

        Assert.False(result.Success);
        Assert.True(result.Cancelled);
        Assert.Null(result.Result);
    }
}
