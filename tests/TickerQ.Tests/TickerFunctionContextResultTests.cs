using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public sealed class ResultPayload
{
    public int Value { get; set; }
    public string Label { get; set; }
}

[JsonSerializable(typeof(ResultPayload))]
[JsonSerializable(typeof(string))]
internal sealed partial class ResultJsonContext : JsonSerializerContext;

public class TickerFunctionContextResultTests
{
    private static TickerFunctionContext NewRuntimeContext(Guid? parentId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Type = TickerType.TimeTicker,
            FunctionName = "Fn",
            ResultSink = new TickerResultSink()
        };

    [Fact]
    public void FreshContext_HasNoResult_And_NoParentResult()
    {
        var context = NewRuntimeContext();
        Assert.False(context.ResultSink.HasResult);
        Assert.False(context.HasParentResult);
    }

    [Fact]
    public void SetResult_WithTypeInfo_ProducesJsonEnvelope()
    {
        var context = NewRuntimeContext();
        context.SetResult(new ResultPayload { Value = 7, Label = "ok" }, ResultJsonContext.Default.ResultPayload);

        Assert.True(context.ResultSink.HasResult);
        var envelope = context.ResultSink.Envelope;
        Assert.Equal("application/json", envelope.MediaType);
        Assert.Equal(TickerResultEnvelope.CurrentVersion, envelope.Version);

        var roundTrip = JsonSerializer.Deserialize(envelope.Payload.Span, ResultJsonContext.Default.ResultPayload);
        Assert.Equal(7, roundTrip.Value);
        Assert.Equal("ok", roundTrip.Label);
    }

    [Fact]
    public void SetResult_WithCanonicalContractId_PublishesIdentityAndFullWireType()
    {
        var context = NewRuntimeContext();
        var contract = new TickerResultContract(
            typeof(ResultPayload).FullName,
            schemaJson: "{\"type\":\"object\"}");

        context.SetResult(new ResultPayload { Value = 7 }, ResultJsonContext.Default.ResultPayload, contract);

        Assert.Equal(contract.ContractId, context.ResultSink.Envelope.ContractId);
        Assert.Equal(typeof(ResultPayload).FullName, context.ResultSink.Envelope.ContractType);
    }

    [Fact]
    public void SetResult_WithSchemaLessContract_Throws()
    {
        var context = NewRuntimeContext();
        var contract = new TickerResultContract(typeof(ResultPayload).FullName);

        Assert.Throws<ArgumentException>(() =>
            context.SetResult(new ResultPayload(), ResultJsonContext.Default.ResultPayload, contract));
        Assert.False(context.ResultSink.HasResult);
    }

    [Fact]
    public void SetResult_JsonNull_IsDistinguishable_From_Absence()
    {
        // Absence.
        var absent = NewRuntimeContext();
        Assert.False(absent.ResultSink.HasResult);

        // Explicit JSON null is a present result whose payload deserializes to null.
        var nulled = NewRuntimeContext();
        nulled.SetResult<ResultPayload>(null, ResultJsonContext.Default.ResultPayload);

        Assert.True(nulled.ResultSink.HasResult);
        var roundTrip = JsonSerializer.Deserialize(nulled.ResultSink.Envelope.Payload.Span, ResultJsonContext.Default.ResultPayload);
        Assert.Null(roundTrip);
    }

    [Fact]
    public void SetResult_On_GenericContext_RoutesTo_SharedSink_Of_Base()
    {
        var baseContext = NewRuntimeContext();
        var generic = new TickerFunctionContext<ResultPayload>(baseContext, new ResultPayload { Value = 1 });

        generic.SetResult(new ResultPayload { Value = 55, Label = "child" }, ResultJsonContext.Default.ResultPayload);

        // The runtime reads the result off the base context; the generic must write through.
        Assert.True(baseContext.ResultSink.HasResult);
        var roundTrip = JsonSerializer.Deserialize(baseContext.ResultSink.Envelope.Payload.Span, ResultJsonContext.Default.ResultPayload);
        Assert.Equal(55, roundTrip.Value);
    }

    [Fact]
    public void GetParentResult_Returns_DirectParent_CommittedResult()
    {
        var context = NewRuntimeContext(parentId: Guid.NewGuid());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
            new ResultPayload { Value = 9, Label = "parent" }, ResultJsonContext.Default.ResultPayload);
        context.ParentResultEnvelope = new TickerResultEnvelope(payloadBytes, TickerResultEnvelope.CurrentVersion, "application/json");

        Assert.True(context.HasParentResult);
        var parent = context.GetParentResult(ResultJsonContext.Default.ResultPayload);
        Assert.Equal(9, parent.Value);
        Assert.Equal("parent", parent.Label);
    }

    [Fact]
    public void GetParentResult_On_Root_Throws()
    {
        var root = NewRuntimeContext();
        Assert.False(root.HasParentResult);
        Assert.Throws<InvalidOperationException>(() => root.GetParentResult(ResultJsonContext.Default.ResultPayload));
    }

    [Fact]
    public void GetParentResult_FailsClosed_On_UnknownEnvelopeVersion()
    {
        var context = NewRuntimeContext(parentId: Guid.NewGuid());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
            new ResultPayload { Value = 1 }, ResultJsonContext.Default.ResultPayload);
        context.ParentResultEnvelope = new TickerResultEnvelope(
            payloadBytes, TickerResultEnvelope.CurrentVersion + 1, "application/json");

        Assert.Throws<NotSupportedException>(() => context.GetParentResult(ResultJsonContext.Default.ResultPayload));
    }

    [Fact]
    public void GenericContext_Propagates_ParentResultEnvelope_From_Base()
    {
        var baseContext = NewRuntimeContext(parentId: Guid.NewGuid());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ResultPayload { Value = 3 }, ResultJsonContext.Default.ResultPayload);
        baseContext.ParentResultEnvelope = new TickerResultEnvelope(bytes, TickerResultEnvelope.CurrentVersion, "application/json");

        var generic = new TickerFunctionContext<ResultPayload>(baseContext, new ResultPayload());
        Assert.True(generic.HasParentResult);
        Assert.Equal(3, generic.GetParentResult(ResultJsonContext.Default.ResultPayload).Value);
    }
}
