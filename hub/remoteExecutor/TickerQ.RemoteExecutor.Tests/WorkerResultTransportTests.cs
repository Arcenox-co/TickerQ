using Google.Protobuf;
using TickerQ.Worker.V1;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class WorkerResultTransportTests
{
    [Fact]
    public void ResultEnvelope_RoundTrips_AllMetadata_AndExplicitJsonNull()
    {
        var original = new ExecuteFunction
        {
            RequestId = "request",
            ParentResult = new ResultEnvelope
            {
                Payload = ByteString.CopyFromUtf8("null"),
                EnvelopeVersion = 1,
                MediaType = "application/json",
                ContractId = "sha256:abc",
                ContractType = "Example.Result"
            }
        };

        var parsed = ExecuteFunction.Parser.ParseFrom(original.ToByteArray());

        Assert.NotNull(parsed.ParentResult);
        Assert.Equal("null", parsed.ParentResult.Payload.ToStringUtf8());
        Assert.Equal(1, parsed.ParentResult.EnvelopeVersion);
        Assert.Equal("application/json", parsed.ParentResult.MediaType);
        Assert.Equal("sha256:abc", parsed.ParentResult.ContractId);
        Assert.Equal("Example.Result", parsed.ParentResult.ContractType);
    }

    [Fact]
    public void ParentId_RoundTrips_WhenPresent()
    {
        var parentId = Guid.NewGuid();
        var original = new ExecuteFunction { ParentId = parentId.ToString() };

        var parsed = ExecuteFunction.Parser.ParseFrom(original.ToByteArray());

        Assert.True(parsed.HasParentId);
        Assert.Equal(parentId.ToString(), parsed.ParentId);
    }

    [Fact]
    public void OldMessages_Parse_WithNewEnvelopeFieldsAbsent()
    {
        // request_id (field 1), success (field 2), error (field 3), cancelled (field 4)
        var oldExecutionResult = new byte[] { 0x0A, 0x03, (byte)'r', (byte)'e', (byte)'q', 0x10, 0x01 };
        var result = ExecutionResult.Parser.ParseFrom(oldExecutionResult);

        Assert.Equal("req", result.RequestId);
        Assert.True(result.Success);
        Assert.Null(result.Result);

        var oldExecuteFunction = ExecuteFunction.Parser.ParseFrom(
            new byte[] { 0x0A, 0x03, (byte)'r', (byte)'e', (byte)'q' });
        Assert.Equal("req", oldExecuteFunction.RequestId);
        Assert.Null(oldExecuteFunction.ParentResult);
        Assert.False(oldExecuteFunction.HasParentId);
    }

    [Fact]
    public void SchedulerAndSdkWorkerProtoCopies_AreByteForByteIdentical()
    {
        var schedulerProto = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "TickerQ.RemoteExecutor", "Protos", "worker_service.proto"));
        var sdkProto = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sdks", "dotnet", "TickerQ.SDK", "Protos", "worker_service.proto"));

        Assert.Equal(schedulerProto, sdkProto);
    }
}
