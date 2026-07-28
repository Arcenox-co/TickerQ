using TickerQ.SDK.Infrastructure;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.SDK.Tests;

/// <summary>
/// Unit tests for the descriptor → wire mapping the SDK sync service performs BEFORE any RPC. The
/// mapping is the single place that decides how each of the three function shapes reaches the Hub:
/// request-less omits the contract, a complete typed contract is fully serialized, and an incomplete
/// typed contract (declares a request but lacks a schema/dialect/fingerprint) is rejected locally so
/// a half-formed contract never leaves the process.
/// </summary>
public class TickerQFunctionSyncServiceTests
{
    private const string SchemaJson =
        "{\"type\":\"object\",\"properties\":{\"OrderId\":{\"type\":\"string\"}},\"required\":[\"OrderId\"]}";

    private static TickerFunctionDescriptor Requestless() =>
        new("PlainJob", TickerTaskPriority.Normal, "0 * * * *");

    private static TickerFunctionDescriptor CompleteTyped()
    {
        var contract = new TickerRequestContract(
            typeName: "Sample.OrderRequest",
            mediaType: TickerRequestContractConstants.DefaultMediaType,
            required: true,
            schemaDialect: TickerRequestContractConstants.SchemaDialect2020_12,
            schemaJson: SchemaJson);
        return new TickerFunctionDescriptor("TypedJob", TickerTaskPriority.Normal, "0 * * * *", 1, contract);
    }

    private static TickerFunctionDescriptor IncompleteTyped()
    {
        // Declares a request contract but carries no schema — so no fingerprint is derivable. This is
        // exactly the half-formed typed contract the sync must refuse.
        var contract = new TickerRequestContract("Sample.OrderRequest");
        return new TickerFunctionDescriptor("IncompleteJob", TickerTaskPriority.Normal, "0 * * * *", 1, contract);
    }

    [Fact]
    public void Requestless_Omits_RequestContract()
    {
        var wire = TickerQFunctionSyncService.BuildFunctionDescriptor(Requestless());

        Assert.Null(wire.RequestContract);
        Assert.Equal("PlainJob", wire.FunctionName);
        Assert.Equal(string.Empty, wire.RequestType);
    }

    [Fact]
    public void CompleteTyped_Serializes_Schema_Dialect_And_Fingerprint()
    {
        var wire = TickerQFunctionSyncService.BuildFunctionDescriptor(CompleteTyped());

        Assert.NotNull(wire.RequestContract);
        Assert.False(string.IsNullOrEmpty(wire.RequestContract.SchemaJson));
        Assert.False(string.IsNullOrEmpty(wire.RequestContract.Fingerprint));
        Assert.Equal(TickerRequestContractConstants.SchemaDialect2020_12, wire.RequestContract.SchemaDialect);
        Assert.Equal("Sample.OrderRequest", wire.RequestContract.TypeName);
        Assert.True(wire.RequestContract.Required);
    }

    [Fact]
    public void IncompleteTyped_Throws_Before_Any_Rpc()
    {
        Assert.Throws<InvalidOperationException>(
            () => TickerQFunctionSyncService.BuildFunctionDescriptor(IncompleteTyped()));
    }

    [Fact]
    public void BuildSyncRequest_Rejects_Manifest_Containing_An_Incomplete_Contract()
    {
        var descriptors = new[] { Requestless(), CompleteTyped(), IncompleteTyped() };

        Assert.Throws<InvalidOperationException>(
            () => TickerQFunctionSyncService.BuildSyncRequest("node-a", "dotnet", descriptors));
    }

    [Fact]
    public void BuildSyncRequest_Maps_Requestless_And_CompleteTyped_Together()
    {
        var descriptors = new[] { Requestless(), CompleteTyped() };

        var request = TickerQFunctionSyncService.BuildSyncRequest("node-a", "dotnet", descriptors);

        Assert.Equal("node-a", request.NodeName);
        Assert.Equal(2, request.Functions.Count);
        var plain = Assert.Single(request.Functions, f => f.FunctionName == "PlainJob");
        Assert.Null(plain.RequestContract);
        var typed = Assert.Single(request.Functions, f => f.FunctionName == "TypedJob");
        Assert.NotNull(typed.RequestContract);
        Assert.False(string.IsNullOrEmpty(typed.RequestContract.Fingerprint));
    }
}
