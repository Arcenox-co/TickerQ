using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.RemoteExecutor.Hub;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
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

    private static RemoteFunctionsSyncService NewService(bool supportsNodeCallbacks = false)
    {
        var options = new TickerQRemoteExecutionOptions();
        options.EnablePrivateNodeCallbackAddressesForLocalDevelopment();
        options.WebHookSignature = "dotnet-node-e2e-secret";
        return new(options, (_, _) => Task.CompletedTask,
            capabilities: new RemoteExecutorPersistenceCapabilities(supportsNodeCallbacks, supportsNodeCallbacks));
    }

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
            }, capabilities: new RemoteExecutorPersistenceCapabilities(true, true)), calls);
    }

    [Fact]
    public void CapabilitySnapshot_IsLazilyDerivedFromTheFinalSingletonProviderRegistration()
    {
        var builder = new TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());
        builder.AddTickerRemoteExecutor<TimeTickerEntity, CronTickerEntity>(options => options.SetApiKey("test-key"));
        var services = new ServiceCollection();
        builder.ExternalProviderConfigServiceAction(services);
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.SupportsAcknowledgedTerminalUpdates.Returns(true);
        provider.SupportsDurableNodeFinalizationOutbox.Returns(true);
        services.AddSingleton(provider);

        using var serviceProvider = services.BuildServiceProvider();
        var snapshot = serviceProvider.GetRequiredService<RemoteExecutorPersistenceCapabilities>();

        Assert.True(snapshot.SupportsNodeCallbacks);
        Assert.Same(provider, serviceProvider.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>());
    }

    [Fact]
    public async Task NodeRegistration_IsFailClosedWithoutDurableProviderCapabilities_AndSupportedSnapshotInvokesCallback()
    {
        var root = FindRepositoryRoot();
        var sdkRoot = Path.Combine(root, "hub", "sdks", "node");
        var fixture = Path.Combine(root, "hub", "remoteExecutor", "TickerQ.RemoteExecutor.Tests", "Fixtures", "node-callback-server.mjs");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node", ArgumentList = { fixture, sdkRoot }, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start Node callback fixture.");
        try
        {
            var startup = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var port = int.Parse(startup!["PORT:".Length..]);
            const string functionName = "dotnet-cancel-first";
            const string nodeName = "capability-node";
            var node = new HubNode
            {
                NodeName = nodeName, SdkType = "nodejs", CallbackUrl = $"http://127.0.0.1:{port}",
                NodeEpoch = "8fef33e7-826b-49e4-a36d-8eb0aa570ef1"
            };
            node.Functions.Add(new HubFunction { FunctionName = functionName, IsActive = true });
            var response = new GetRegisteredFunctionsResponse { WebhookSignature = "dotnet-node-e2e-secret" };
            response.Nodes.Add(node);

            await NewService().RegisterFunctionsFromResponse(response, CancellationToken.None);
            var key = $"{functionName}@{nodeName}";
            Assert.False(TickerFunctionProvider.TickerFunctions.ContainsKey(key));
            Assert.False(RemoteFunctionRegistry.IsRemote(functionName));
            using var client = new HttpClient();
            using (var before = await JsonDocument.ParseAsync(await client.GetStreamAsync($"http://127.0.0.1:{port}/stats")))
                Assert.Equal(0, before.RootElement.GetProperty("invocationCount").GetInt32());

            var supported = NewService(supportsNodeCallbacks: true);
            await supported.RegisterFunctionsFromResponse(response, CancellationToken.None);
            var callback = TickerFunctionProvider.TickerFunctions[key].Delegate;
            await using var services = new ServiceCollection()
                .AddSingleton<IRemotePayloadLoader>(new NullPayloadLoader()).BuildServiceProvider();
            var context = new TickerFunctionContext
            {
                Id = Guid.NewGuid(), AcquisitionToken = Guid.NewGuid(), FunctionName = key,
                Type = TickerType.TimeTicker, ScheduledFor = DateTime.UtcNow, ResultSink = new TickerResultSink()
            };
            await callback(CancellationToken.None, services, context).WaitAsync(TimeSpan.FromSeconds(10));
            using var after = await JsonDocument.ParseAsync(await client.GetStreamAsync($"http://127.0.0.1:{port}/stats"));
            Assert.Equal(1, after.RootElement.GetProperty("invocationCount").GetInt32());
            RemoteFunctionRegistry.Remove(functionName);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }


    private sealed class NullPayloadLoader : IRemotePayloadLoader
    {
        public Task<byte[]?> LoadPayloadAsync(Guid tickerId, TickerType type, CancellationToken ct)
            => Task.FromResult<byte[]?>(null);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "hub", "sdks", "node", "package.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate repository root.");
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
