using System.Text.Json;
using TickerQ.RemoteExecutor.Hub;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

/// <summary>
/// Local transport-policy tests for <see cref="RemoteFunctionsSyncService"/>: request-bearing remote
/// registrations MUST carry a canonical Draft 2020-12 full schema, request-less registrations work, and
/// the reconstructed contract validates raw remote payloads. No external Hub is contacted — the sync
/// entry point is driven directly with an in-memory <see cref="GetRegisteredFunctionsResponse"/>.
/// </summary>
public sealed class RemoteFunctionsSyncServiceTests
{
    private const string Dialect2020_12 = "https://json-schema.org/draft/2020-12/schema";

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static RemoteFunctionsSyncService NewService()
        => new(new TickerQRemoteExecutionOptions(), new NullServiceProvider());

    private static GetRegisteredFunctionsResponse ResponseWith(HubFunction function, string nodeName)
    {
        var node = new HubNode { NodeName = nodeName };
        node.Functions.Add(function);
        var response = new GetRegisteredFunctionsResponse();
        response.Nodes.Add(node);
        return response;
    }

    private static async Task RegisterAsync(GetRegisteredFunctionsResponse response)
        => await NewService().RegisterFunctionsFromResponse(response, CancellationToken.None);

    private static (RemoteFunctionsSyncService Service, List<DefinedCronTickerSeed[]> Calls) ServiceWithManager()
    {
        var calls = new List<DefinedCronTickerSeed[]>();
        return (new RemoteFunctionsSyncService(
            new TickerQRemoteExecutionOptions(),
            (seeds, _) =>
            {
                calls.Add(seeds);
                return Task.CompletedTask;
            }), calls);
    }

    // ── Request-less registration works: descriptor with a null request contract. ──
    [Fact]
    public async Task Requestless_Function_Registers_With_Null_Contract()
    {
        var node = "node-requestless";
        var fn = new HubFunction
        {
            FunctionName = "PlainJob",
            IsActive = true,
            NodeExpression = "0 * * * *",
            ContractVersion = 1
        };

        await RegisterAsync(ResponseWith(fn, node));

        var key = $"PlainJob@{node}";
        Assert.True(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey(key));
        Assert.Null(TickerFunctionProvider.TickerFunctionDescriptors[key].Request);
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey(key));

        RemoteFunctionRegistry.Remove("PlainJob");
    }

    // ── Canonical nested schema reconstructs into a validating contract. ──
    [Fact]
    public async Task Canonical_NestedSchema_Reconstructs_And_Validates_Raw_Payload()
    {
        var node = "node-nested";
        const string nestedSchema =
            """
            {
              "type": "object",
              "properties": {
                "inner": {
                  "type": "object",
                  "properties": { "count": { "type": "integer" } },
                  "required": ["count"]
                }
              },
              "required": ["inner"]
            }
            """;

        var contract = new RequestContract
        {
            TypeName = "NestedRequest",
            MediaType = "application/json",
            Required = true,
            SchemaDialect = Dialect2020_12,
            SchemaJson = nestedSchema
        };
        contract.Examples.Add(new RequestExample { Key = "default", ValueJson = "{\"inner\":{\"count\":1}}" });

        var fn = new HubFunction
        {
            FunctionName = "NestedJob",
            IsActive = true,
            ContractVersion = 1,
            RequestContract = contract
        };

        await RegisterAsync(ResponseWith(fn, node));

        var key = $"NestedJob@{node}";
        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors[key];
        Assert.NotNull(descriptor.Request);
        Assert.True(descriptor.Request!.Schema.HasValue);

        // Nested structure survived reconstruction + canonicalization.
        var schema = descriptor.Request.Schema!.Value;
        var inner = schema.GetProperty("properties").GetProperty("inner");
        Assert.Equal("object", inner.GetProperty("type").GetString());
        Assert.Equal("integer",
            inner.GetProperty("properties").GetProperty("count").GetProperty("type").GetString());

        // Fingerprint is derived (never trusted from the wire) and the example survived.
        Assert.StartsWith("sha256:", descriptor.Request.Fingerprint);
        Assert.Single(descriptor.Request.Examples);
        Assert.Equal("default", descriptor.Request.Examples[0].Key);

        // Full schema-only contract validates a raw remote payload (no local JsonTypeInfo).
        var validPayload = TickerHelper.CreateTickerRequest(
            JsonSerializer.Deserialize<JsonElement>("{\"inner\":{\"count\":5}}"));
        var okResult = TickerRequestPayloadValidator.Validate(key, validPayload);
        Assert.True(okResult.IsValid, string.Join(",", okResult.Errors.Select(e => e.Message)));
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, okResult.SchemaValidation);

        // A payload that violates the nested schema is rejected by the reconstructed contract.
        var badPayload = TickerHelper.CreateTickerRequest(
            JsonSerializer.Deserialize<JsonElement>("{\"inner\":{\"count\":\"not-an-int\"}}"));
        var badResult = TickerRequestPayloadValidator.Validate(key, badPayload);
        Assert.False(badResult.IsValid);
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, badResult.SchemaValidation);

        RemoteFunctionRegistry.Remove("NestedJob");
    }

    // ── Canonical request-bearing descriptor WITHOUT a schema is rejected before merging. ──
    [Fact]
    public async Task Canonical_Without_Schema_Is_Rejected_Before_Merge()
    {
        var node = "node-noschema-canonical";
        var contract = new RequestContract
        {
            TypeName = "SchemaLess",
            MediaType = "application/json",
            Required = true,
            SchemaDialect = Dialect2020_12
            // no schema_json
        };
        var fn = new HubFunction
        {
            FunctionName = "NoSchemaCanonical",
            IsActive = true,
            ContractVersion = 1,
            RequestContract = contract
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => RegisterAsync(ResponseWith(fn, node)));

        // Nothing was merged.
        Assert.False(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey($"NoSchemaCanonical@{node}"));
        Assert.False(RemoteFunctionRegistry.IsRemote("NoSchemaCanonical"));
    }

    // ── Legacy request-bearing descriptor (request_type only, no canonical schema) is rejected. ──
    [Fact]
    public async Task Legacy_SchemaLess_RequestBearing_Is_Rejected()
    {
        var node = "node-legacy";
        var fn = new HubFunction
        {
            FunctionName = "LegacyJob",
            IsActive = true,
            ContractVersion = 1,
            RequestType = "LegacyRequest",
            RequestExampleJson = "{\"a\":1}"
            // no canonical request_contract
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => RegisterAsync(ResponseWith(fn, node)));

        Assert.False(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey($"LegacyJob@{node}"));
    }

    // ── A supplied (wire) fingerprint that disagrees with the derived one is rejected. ──
    [Fact]
    public async Task Supplied_Fingerprint_Mismatch_Is_Rejected()
    {
        var node = "node-fp";
        var contract = new RequestContract
        {
            TypeName = "FpRequest",
            MediaType = "application/json",
            Required = true,
            SchemaDialect = Dialect2020_12,
            SchemaJson = "{\"type\":\"object\"}",
            Fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000"
        };
        var fn = new HubFunction
        {
            FunctionName = "FpJob",
            IsActive = true,
            ContractVersion = 1,
            RequestContract = contract
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => RegisterAsync(ResponseWith(fn, node)));

        Assert.False(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey($"FpJob@{node}"));
        Assert.False(RemoteFunctionRegistry.IsRemote("FpJob"));
    }

    [Fact]
    public async Task Allowed_To_Required_Cron_Emits_Blocked_Seed()
    {
        var (service, calls) = ServiceWithManager();
        var contract = new RequestContract
        {
            TypeName = "RequiredRequest",
            MediaType = "application/json",
            Required = false,
            SchemaDialect = Dialect2020_12,
            SchemaJson = "{\"type\":\"object\"}"
        };
        var node = new HubNode { NodeName = "required-node", AutoMigrateExpressions = true };
        node.Functions.Add(new HubFunction
        {
            FunctionName = "RequiredCron",
            IsActive = true,
            NodeExpression = "0 * * * *",
            ContractVersion = 1,
            RequestContract = contract
        });
        var response = new GetRegisteredFunctionsResponse();
        response.Nodes.Add(node);

        await service.RegisterFunctionsFromResponse(response, CancellationToken.None);
        Assert.True(Assert.Single(Assert.Single(calls)).CanSeed);

        contract.Required = true;
        await service.RegisterFunctionsFromResponse(response, CancellationToken.None);

        var seed = Assert.Single(calls[1]);
        Assert.Equal("RequiredCron@required-node", seed.Function);
        Assert.False(seed.CanSeed);
        RemoteFunctionRegistry.Remove("RequiredCron");
    }

    [Fact]
    public async Task Present_To_Removed_Reconciliation_Runs_Empty_Seed_Cleanup()
    {
        var (service, calls) = ServiceWithManager();
        var node = new HubNode { NodeName = "removed-node", AutoMigrateExpressions = true };
        node.Functions.Add(new HubFunction
        {
            FunctionName = "RemovedCron",
            IsActive = true,
            NodeExpression = "0 * * * *",
            ContractVersion = 1
        });
        var present = new GetRegisteredFunctionsResponse();
        present.Nodes.Add(node);

        await service.RegisterFunctionsFromResponse(present, CancellationToken.None);
        await service.RegisterFunctionsFromResponse(
            new GetRegisteredFunctionsResponse(), CancellationToken.None);

        Assert.True(Assert.Single(calls[0]).CanSeed);
        Assert.Empty(calls[1]);
        RemoteFunctionRegistry.Remove("RemovedCron");
    }
}
